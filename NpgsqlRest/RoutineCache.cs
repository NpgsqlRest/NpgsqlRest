using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace NpgsqlRest;

public interface IRoutineCache
{
    bool Get(RoutineEndpoint endpoint, string key, out object? result);
    void AddOrUpdate(RoutineEndpoint endpoint, string key, object? value);
    bool Remove(string key);
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

    public void AddOrUpdate(RoutineEndpoint endpoint, string key, object? value)
    {
        var effectiveKey = CacheKeyHasher.GetEffectiveKey(key, _options);

        var entry = new CacheEntry
        {
            Value = value,
            ExpirationTime = endpoint.CacheExpiresIn.HasValue ? DateTime.UtcNow + endpoint.CacheExpiresIn.Value : null
        };

        _cache[effectiveKey] = entry;
    }

    public bool Remove(string key)
    {
        var effectiveKey = CacheKeyHasher.GetEffectiveKey(key, _options);
        return _cache.TryRemove(effectiveKey, out _);
    }
}
