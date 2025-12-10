using System.Collections.Concurrent;

namespace NpgsqlRest;

public interface IRoutineCache
{
    bool Get(RoutineEndpoint endpoint, string key, out object? result);
    void AddOrUpdate(RoutineEndpoint endpoint, string key, object? value);
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
    private static Timer? _cleanupTimer;

    public static void Start(NpgsqlRestOptions options)
    {
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
        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.IsExpired)
            {
                _cache.TryRemove(key, out _);
                result = null;
                return false;
            }

            result = entry.Value;
            return true;
        }

        result = null;
        return false;
    }

    public void AddOrUpdate(RoutineEndpoint endpoint, string key, object? value)
    {
        var entry = new CacheEntry
        {
            Value = value,
            ExpirationTime = endpoint.CacheExpiresIn.HasValue ? DateTime.UtcNow + endpoint.CacheExpiresIn.Value : null
        };

        _cache[key] = entry;
    }
}
