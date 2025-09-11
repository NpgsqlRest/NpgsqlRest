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

    private static readonly ConcurrentDictionary<int, CacheEntry> _cache = new();
    private static readonly ConcurrentDictionary<int, string> _originalKeys = new();
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
        _originalKeys.Clear();
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
            _originalKeys.TryRemove(key, out _);
        }
    }

    public bool Get(RoutineEndpoint endpoint, string key, out object? result)
    {
        var hashedKey = key.GetHashCode();

        if (_cache.TryGetValue(hashedKey, out var entry))
        {
            if (_originalKeys.TryGetValue(hashedKey, out var originalKey) && originalKey == key)
            {
                if (entry.IsExpired)
                {
                    // Remove expired entry
                    _cache.TryRemove(hashedKey, out _);
                    _originalKeys.TryRemove(hashedKey, out _);
                    result = null;
                    return false;
                }

                result = entry.Value;
                return true;
            }
        }

        result = null;
        return false;
    }

    public void AddOrUpdate(RoutineEndpoint endpoint, string key, object? value)
    {
        var hashedKey = key.GetHashCode();
        var entry = new CacheEntry
        {
            Value = value,
            ExpirationTime = endpoint.CacheExpiresIn.HasValue ? DateTime.UtcNow + endpoint.CacheExpiresIn.Value : null
        };

        _cache[hashedKey] = entry;
        _originalKeys[hashedKey] = key;
    }
}
