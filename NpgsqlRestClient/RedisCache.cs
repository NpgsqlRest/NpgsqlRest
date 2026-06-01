using System.Collections.Concurrent;
using NpgsqlRest;
using StackExchange.Redis;

namespace NpgsqlRestClient
{
    public class RedisCache : IRoutineCache, IDisposable
    {
        private readonly ConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        private readonly ILogger? _logger;
        private readonly CacheOptions _cacheOptions;
        // In-process in-flight factory invocations (single-instance coalescing). Cross-process
        // coalescing across NpgsqlRest instances is intentionally out of scope; this only collapses
        // a burst hitting THIS instance into one execution. See memory RoutineCache for the pattern.
        private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _inflight = new(StringComparer.Ordinal);
        private bool _disposed;

        public RedisCache(string configuration, ILogger? logger = null, CacheOptions? cacheOptions = null)
        {
            _logger = logger;
            _cacheOptions = cacheOptions ?? new CacheOptions();
            ConnectionMultiplexer? redis = null;

            try
            {
                _logger?.LogDebug("Connecting to Redis with configuration: {RedisConfiguration}", configuration);
                redis = ConnectionMultiplexer.Connect(configuration);
                _db = redis.GetDatabase();

                // Test the connection
                _db.Ping();
                _redis = redis; // Only assign after successful initialization
                _logger?.LogInformation("Successfully connected to Redis cache");
            }
            catch (Exception ex)
            {
                // Dispose the connection if initialization failed
                redis?.Dispose();
                _disposed = true;
                _logger?.LogError(ex, "Failed to initialize Redis connection");
                throw new InvalidOperationException("Failed to initialize Redis cache", ex);
            }
        }

        private string GetEffectiveKey(string key)
        {
            return CacheKeyHasher.GetEffectiveKey(key, _cacheOptions);
        }

        public bool Get(RoutineEndpoint endpoint, string key, out object? result)
        {
            result = null;

            if (_disposed)
            {
                _logger?.LogWarning("Attempted to get from disposed Redis cache");
                return false;
            }

            try
            {
                var effectiveKey = GetEffectiveKey(key);
                var redisValue = _db.StringGet(effectiveKey);
                if (redisValue.HasValue)
                {
                    result = redisValue.ToString();
                    _logger?.LogTrace("Cache hit for key: {Key}", key);
                    return true;
                }

                _logger?.LogTrace("Cache miss for key: {Key}", key);
                return false;
            }
            catch (RedisException ex)
            {
                _logger?.LogError(ex, "Redis error while getting key: {Key}", key);
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unexpected error while getting key from Redis: {Key}", key);
                return false;
            }
        }

        public void AddOrUpdate(RoutineEndpoint endpoint, string key, object? value, TimeSpan? overrideExpiration = null)
        {
            if (_disposed)
            {
                _logger?.LogWarning("Attempted to set on disposed Redis cache");
                return;
            }

            try
            {
                var effectiveKey = GetEffectiveKey(key);
                var stringValue = value?.ToString();
                var expiry = overrideExpiration ?? endpoint.CacheExpiresIn;

                _db.StringSet(effectiveKey, stringValue, expiry.HasValue ? new Expiration(expiry.Value) : default);
                _logger?.LogTrace("Cached value for key: {Key} with expiry: {Expiry}", key, expiry);
            }
            catch (RedisException ex)
            {
                _logger?.LogError(ex, "Redis error while setting key: {Key}", key);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unexpected error while setting key in Redis: {Key}", key);
            }
        }

        public async ValueTask<object?> GetOrCreateAsync(
            RoutineEndpoint endpoint,
            string key,
            Func<CancellationToken, ValueTask<object?>> factory,
            TimeSpan? overrideExpiration = null,
            CancellationToken cancellationToken = default)
        {
            var effectiveKey = GetEffectiveKey(key);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Get(endpoint, key, out var cached))
                {
                    return cached;
                }

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
                        throw;
                    }
                    // Lead caller cancelled before this waiter; retry (value may be cached now, or this
                    // caller becomes the new lead with its own live resources).
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
                _inflight.TryRemove(effectiveKey, out _);
            }
        }

        public bool Remove(string key)
        {
            if (_disposed)
            {
                _logger?.LogWarning("Attempted to remove from disposed Redis cache");
                return false;
            }

            try
            {
                var effectiveKey = GetEffectiveKey(key);
                var result = _db.KeyDelete(effectiveKey);
                _logger?.LogTrace("Removed cache key: {Key}, success: {Result}", key, result);
                return result;
            }
            catch (RedisException ex)
            {
                _logger?.LogError(ex, "Redis error while removing key: {Key}", key);
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unexpected error while removing key from Redis: {Key}", key);
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _redis.Dispose();
                _logger?.LogInformation("Redis cache connection disposed");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error while disposing Redis cache connection");
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}