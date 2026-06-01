using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace NpgsqlRest;

public interface IRoutineCache
{
    bool Get(RoutineEndpoint endpoint, string key, out object? result);
    /// <summary>
    /// Stores or updates a cached value.
    /// <paramref name="overrideExpiration"/> (when non-null) takes precedence over <see cref="RoutineEndpoint.CacheExpiresIn"/>
    /// and is used by <see cref="CacheWhenRule"/> "Then" overrides to apply a per-request TTL.
    /// </summary>
    void AddOrUpdate(RoutineEndpoint endpoint, string key, object? value, TimeSpan? overrideExpiration = null);
    bool Remove(string key);

    /// <summary>
    /// Atomic get-or-compute. On a cache hit the cached value is returned and <paramref name="factory"/>
    /// is NOT invoked. On a miss the factory runs the underlying work (e.g. opening the connection,
    /// executing SQL and serializing the response) and its result is stored before returning.
    /// <para>
    /// Backends that override this provide stampede protection: a burst of concurrent calls with the
    /// same <paramref name="key"/> coalesce into a single factory invocation; the remaining callers
    /// await the in-flight result instead of each hitting the database. The default implementation
    /// below performs a plain probe-then-compute with no coalescing, so pre-existing custom
    /// implementations keep working unchanged (they simply gain no stampede protection).
    /// </para>
    /// </summary>
    /// <param name="endpoint">The endpoint whose response is being cached (carries the default TTL).</param>
    /// <param name="key">The cache key; identical keys coalesce into one factory invocation.</param>
    /// <param name="factory">Runs the underlying work when the cache is cold. Should respect the token.</param>
    /// <param name="overrideExpiration">When non-null, the per-request TTL to use instead of the endpoint default.</param>
    /// <param name="cancellationToken">Cancels this caller's wait; see backend notes for coalescing semantics.</param>
    async ValueTask<object?> GetOrCreateAsync(
        RoutineEndpoint endpoint,
        string key,
        Func<CancellationToken, ValueTask<object?>> factory,
        TimeSpan? overrideExpiration = null,
        CancellationToken cancellationToken = default)
    {
        if (Get(endpoint, key, out var existing))
        {
            return existing;
        }
        var result = await factory(cancellationToken);
        AddOrUpdate(endpoint, key, result, overrideExpiration);
        return result;
    }
}

public static class CacheKeyHasher
{
    /// <summary>
    /// Computes a SHA256 hash of the cache key and returns it as a hex string.
    /// The result is always 64 characters (256 bits / 4 bits per hex char).
    /// </summary>
    public static string ComputeHash(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Returns the effective cache key based on the hashing configuration.
    /// If UseHashedCacheKeys is true and the key length exceeds HashKeyThreshold, returns a hashed key.
    /// Otherwise, returns the original key.
    /// </summary>
    public static string GetEffectiveKey(string key, CacheOptions options)
    {
        if (options.UseHashedCacheKeys && key.Length > options.HashKeyThreshold)
        {
            return ComputeHash(key);
        }
        return key;
    }
}

public class RoutineCache : IRoutineCache
{
    private class CacheEntry
    {
        public object? Value { get; set; }
        public DateTime? ExpirationTime { get; set; }
        public bool IsExpired => ExpirationTime.HasValue && DateTime.UtcNow > ExpirationTime.Value;
    }

    private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    // In-flight factory invocations, keyed by effective (post-hash) cache key. Used to coalesce a burst
    // of identical concurrent requests into a single factory run (stampede protection). The value is a
    // Lazy so that even if ConcurrentDictionary.GetOrAdd runs its value-factory more than once under
    // contention, only the stored Lazy's task is ever started — guaranteeing a single execution.
    private static readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _inflight = new(StringComparer.Ordinal);
    private static Timer? _cleanupTimer;
    private static CacheOptions _options = new();

    public static void Start(NpgsqlRestOptions options)
    {
        _options = options.CacheOptions;
        _cleanupTimer = new Timer(
            _ => CleanupExpiredEntriesInternal(),
            null,
            TimeSpan.FromSeconds(options.CacheOptions.MemoryCachePruneIntervalSeconds),
            TimeSpan.FromSeconds(options.CacheOptions.MemoryCachePruneIntervalSeconds));
    }

    public static void Shutdown()
    {
        _cleanupTimer?.Dispose();
        _cache.Clear();
    }

    private static void CleanupExpiredEntriesInternal()
    {
        var expiredKeys = _cache
            .Where(kvp => kvp.Value.IsExpired)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _cache.TryRemove(key, out _);
        }
    }

    public bool Get(RoutineEndpoint endpoint, string key, out object? result)
    {
        var effectiveKey = CacheKeyHasher.GetEffectiveKey(key, _options);

        if (_cache.TryGetValue(effectiveKey, out var entry))
        {
            if (entry.IsExpired)
            {
                _cache.TryRemove(effectiveKey, out _);
                result = null;
                return false;
            }

            result = entry.Value;
            return true;
        }

        result = null;
        return false;
    }

    public void AddOrUpdate(RoutineEndpoint endpoint, string key, object? value, TimeSpan? overrideExpiration = null)
    {
        var effectiveKey = CacheKeyHasher.GetEffectiveKey(key, _options);

        var ttl = overrideExpiration ?? endpoint.CacheExpiresIn;
        var entry = new CacheEntry
        {
            Value = value,
            ExpirationTime = ttl.HasValue ? DateTime.UtcNow + ttl.Value : null
        };

        _cache[effectiveKey] = entry;
    }

    public bool Remove(string key)
    {
        var effectiveKey = CacheKeyHasher.GetEffectiveKey(key, _options);
        return _cache.TryRemove(effectiveKey, out _);
    }

    public async ValueTask<object?> GetOrCreateAsync(
        RoutineEndpoint endpoint,
        string key,
        Func<CancellationToken, ValueTask<object?>> factory,
        TimeSpan? overrideExpiration = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveKey = CacheKeyHasher.GetEffectiveKey(key, _options);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Get(endpoint, key, out var cached))
            {
                return cached;
            }

            // Coalesce: the first caller for this key creates the in-flight task and runs the factory
            // (with its own live connection/command and its own token); concurrent callers await it.
            var lazy = _inflight.GetOrAdd(
                effectiveKey,
                _ => new Lazy<Task<object?>>(() => RunFactoryAsync(endpoint, key, effectiveKey, factory, overrideExpiration, cancellationToken)));

            try
            {
                return await lazy.Value.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    // This caller itself was cancelled — propagate.
                    throw;
                }
                // The lead caller (whose token drove the shared run) was cancelled before this waiter.
                // Its in-flight entry has already been removed; loop and retry — the value may now be
                // cached, or this caller becomes the new lead and runs with its own live resources.
            }
        }
    }

    private async Task<object?> RunFactoryAsync(
        RoutineEndpoint endpoint,
        string key,
        string effectiveKey,
        Func<CancellationToken, ValueTask<object?>> factory,
        TimeSpan? overrideExpiration,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await factory(cancellationToken).ConfigureAwait(false);
            AddOrUpdate(endpoint, key, result, overrideExpiration);
            return result;
        }
        finally
        {
            // Always free the slot so a later miss (or a failed/cancelled run) can start fresh.
            _inflight.TryRemove(effectiveKey, out _);
        }
    }
}
