using Microsoft.Extensions.Logging;
using NpgsqlRest;
using StackExchange.Redis;

namespace NpgsqlRestClient
{
    public class RedisCache : IRoutineCache, IDisposable
    {
        private readonly ConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        private readonly ILogger? _logger;
        private bool _disposed;

        public RedisCache(string configuration, ILogger? logger = null)
        {
            _logger = logger;
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
                var redisValue = _db.StringGet(key);
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

        public void AddOrUpdate(RoutineEndpoint endpoint, string key, object? value)
        {
            if (_disposed)
            {
                _logger?.LogWarning("Attempted to set on disposed Redis cache");
                return;
            }

            try
            {
                var stringValue = value?.ToString();
                var expiry = endpoint.CacheExpiresIn;
            
                _db.StringSet(key, stringValue, expiry);
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