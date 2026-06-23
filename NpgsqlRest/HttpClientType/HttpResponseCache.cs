using System.Collections.Concurrent;

namespace NpgsqlRest.HttpClientType;

/// <summary>
/// Immutable snapshot of an outbound HTTP type response, suitable for caching and reuse.
/// </summary>
public sealed record CachedHttpResponse(
    int StatusCode,
    string? Body,
    string? ResponseHeaders,
    string? ContentType,
    bool IsSuccess,
    string? ErrorMessage);

/// <summary>
/// In-memory cache of HTTP type responses with stampede protection. A burst of concurrent requests
/// for the same key coalesce into a single outbound call; the rest await the in-flight result.
/// Only successful responses are stored, so a transient upstream failure is never pinned for the TTL.
/// </summary>
public static class HttpResponseCache
{
    private sealed class CacheEntry
    {
        public required CachedHttpResponse Value { get; init; }
        public DateTime? ExpirationTime { get; init; }
        public bool IsExpired => ExpirationTime.HasValue && DateTime.UtcNow > ExpirationTime.Value;
    }

    private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    // In-flight factory invocations keyed by cache key. The value is a Lazy so that even if
    // ConcurrentDictionary.GetOrAdd runs its value-factory more than once under contention, only the
    // stored Lazy's task is ever started — guaranteeing a single outbound call per key.
    private static readonly ConcurrentDictionary<string, Lazy<Task<CachedHttpResponse>>> _inflight = new(StringComparer.Ordinal);

    private static Timer? _cleanupTimer;
    private static int _maxEntries = 10_000;

    public static void Start(NpgsqlRestOptions options)
    {
        _maxEntries = options.HttpClientOptions.MaxCacheEntries;
        var interval = TimeSpan.FromSeconds(options.HttpClientOptions.CachePruneIntervalSeconds);
        _cleanupTimer?.Dispose();
        _cleanupTimer = new Timer(_ => CleanupExpiredEntries(), null, interval, interval);
    }

    public static void Shutdown()
    {
        _cleanupTimer?.Dispose();
        _cleanupTimer = null;
        _cache.Clear();
        _inflight.Clear();
    }

    private static void CleanupExpiredEntries()
    {
        foreach (var kvp in _cache)
        {
            if (kvp.Value.IsExpired)
            {
                _cache.TryRemove(kvp.Key, out _);
            }
        }
    }

    private static bool TryGet(string key, out CachedHttpResponse value)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.IsExpired)
            {
                _cache.TryRemove(key, out _);
                value = null!;
                return false;
            }
            value = entry.Value;
            return true;
        }
        value = null!;
        return false;
    }

    private static void Store(string key, CachedHttpResponse value, TimeSpan? ttl)
    {
        // Bound memory: once full, don't admit new keys (existing entries still serve and expire).
        // Updates to an already-cached key are always allowed.
        if (_cache.Count >= _maxEntries && !_cache.ContainsKey(key))
        {
            return;
        }

        _cache[key] = new CacheEntry
        {
            Value = value,
            ExpirationTime = ttl.HasValue ? DateTime.UtcNow + ttl.Value : null
        };
    }

    /// <summary>
    /// Returns the cached response for <paramref name="key"/> if present and unexpired; otherwise runs
    /// <paramref name="factory"/> (the outbound call), stores the result when it is successful, and
    /// returns it. Concurrent callers for the same key coalesce into one factory invocation.
    /// </summary>
    public static async Task<CachedHttpResponse> GetOrCreateAsync(
        string key,
        TimeSpan? ttl,
        Func<CancellationToken, Task<CachedHttpResponse>> factory,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryGet(key, out var cached))
            {
                return cached;
            }

            var lazy = _inflight.GetOrAdd(
                key,
                _ => new Lazy<Task<CachedHttpResponse>>(() => RunFactoryAsync(key, ttl, factory, cancellationToken)));

            try
            {
                return await lazy.Value.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                // The lead caller (whose token drove the shared run) was cancelled before this waiter.
                // Loop and retry — the value may now be cached, or this caller becomes the new lead.
            }
        }
    }

    private static async Task<CachedHttpResponse> RunFactoryAsync(
        string key,
        TimeSpan? ttl,
        Func<CancellationToken, Task<CachedHttpResponse>> factory,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await factory(cancellationToken).ConfigureAwait(false);
            // Success-only: never pin a transient upstream failure for the whole TTL.
            if (result.IsSuccess)
            {
                Store(key, result, ttl);
            }
            return result;
        }
        finally
        {
            _inflight.TryRemove(key, out _);
        }
    }
}
