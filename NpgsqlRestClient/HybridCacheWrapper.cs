using Microsoft.Extensions.Caching.Hybrid;
using NpgsqlRest;

namespace NpgsqlRestClient;

public class HybridCacheWrapper : IRoutineCache
{
    private readonly HybridCache _cache;
    private readonly ILogger? _logger;
    private readonly CacheOptions _cacheOptions;

    public HybridCacheWrapper(HybridCache cache, ILogger? logger = null, CacheOptions? cacheOptions = null)
    {
        _cache = cache;
        _logger = logger;
        _cacheOptions = cacheOptions ?? new CacheOptions();
        _logger?.LogInformation("HybridCache wrapper initialized");
    }

    private string GetEffectiveKey(string key)
    {
        return CacheKeyHasher.GetEffectiveKey(key, _cacheOptions);
    }

    public bool Get(RoutineEndpoint endpoint, string key, out object? result)
    {
        result = null;

        try
        {
            var effectiveKey = GetEffectiveKey(key);

            // HybridCache uses async API, we need to block here since IRoutineCache is synchronous
            var task = _cache.GetOrCreateAsync<string?>(
                effectiveKey,
                cancel => new ValueTask<string?>((string?)null),
                new HybridCacheEntryOptions
                {
                    Flags = HybridCacheEntryFlags.DisableUnderlyingData
                });

            var cachedValue = task.AsTask().GetAwaiter().GetResult();

            if (cachedValue is not null)
            {
                result = cachedValue;
                _logger?.LogTrace("Cache hit for key: {Key}", key);
                return true;
            }

            _logger?.LogTrace("Cache miss for key: {Key}", key);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error while getting key from HybridCache: {Key}", key);
            return false;
        }
    }

    public void AddOrUpdate(RoutineEndpoint endpoint, string key, object? value, TimeSpan? overrideExpiration = null)
    {
        try
        {
            var effectiveKey = GetEffectiveKey(key);
            var stringValue = value?.ToString();
            var expiry = overrideExpiration ?? endpoint.CacheExpiresIn;

            var entryOptions = expiry.HasValue
                ? new HybridCacheEntryOptions { Expiration = expiry.Value, LocalCacheExpiration = expiry.Value }
                : new HybridCacheEntryOptions();

            // HybridCache.SetAsync is used to store values directly
            var task = _cache.SetAsync(effectiveKey, stringValue, entryOptions);
            task.AsTask().GetAwaiter().GetResult();

            _logger?.LogTrace("Cached value for key: {Key} with expiry: {Expiry}", key, expiry);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error while setting key in HybridCache: {Key}", key);
        }
    }

    public async ValueTask<object?> GetOrCreateAsync(
        RoutineEndpoint endpoint,
        string key,
        Func<CancellationToken, ValueTask<object?>> factory,
        TimeSpan? overrideExpiration = null,
        CancellationToken cancellationToken = default)
    {
        // Delegate straight to HybridCache.GetOrCreateAsync: N concurrent calls with the same key
        // share ONE factory invocation (its built-in stampede protection). Values are stored as the
        // serialized string form, matching AddOrUpdate above (binary payloads are not supported by the
        // Hybrid backend, same as before). The factory's exceptions propagate without being cached.
        var effectiveKey = GetEffectiveKey(key);
        var expiry = overrideExpiration ?? endpoint.CacheExpiresIn;
        var options = expiry.HasValue
            ? new HybridCacheEntryOptions { Expiration = expiry.Value, LocalCacheExpiration = expiry.Value }
            : new HybridCacheEntryOptions();

        return await _cache.GetOrCreateAsync<string?>(
            effectiveKey,
            async ct => (await factory(ct))?.ToString(),
            options,
            cancellationToken: cancellationToken);
    }

    public bool Remove(string key)
    {
        try
        {
            var effectiveKey = GetEffectiveKey(key);
            var task = _cache.RemoveAsync(effectiveKey);
            task.AsTask().GetAwaiter().GetResult();

            _logger?.LogTrace("Removed cache key: {Key}", key);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error while removing key from HybridCache: {Key}", key);
            return false;
        }
    }
}
