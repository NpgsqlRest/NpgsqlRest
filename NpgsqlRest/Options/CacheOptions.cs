namespace NpgsqlRest;

public class CacheOptions
{
    /// <summary>
    /// Default routine cache object. Inject custom cache object to override default cache. Set to null to disable caching.
    /// </summary>
    public IRoutineCache? DefaultRoutineCache { get; set; } = new RoutineCache();

    /// <summary>
    /// When cache is enabled, this value sets the interval in minutes for cache pruning (removing expired entries). Default is 1 minute.
    /// </summary>
    public int MemoryCachePruneIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum number of rows that can be cached for set-returning functions.
    /// If a result set exceeds this limit, it will not be cached (but will still be returned).
    /// Set to 0 to disable caching for sets entirely. Set to null for unlimited (use with caution).
    /// Default is 1000 rows.
    /// </summary>
    public int? MaxCacheableRows { get; set; } = 1000;

    /// <summary>
    /// When true, cache keys longer than HashKeyThreshold characters are hashed to a fixed-length SHA256 string.
    /// This reduces memory usage for long cache keys and improves Redis performance with large keys.
    /// Default is false (cache keys are stored as-is).
    /// </summary>
    public bool UseHashedCacheKeys { get; set; } = false;

    /// <summary>
    /// Cache keys longer than this threshold (in characters) will be hashed when UseHashedCacheKeys is true.
    /// Keys shorter than this threshold are stored as-is for better debuggability.
    /// Default is 256 characters.
    /// </summary>
    public int HashKeyThreshold { get; set; } = 256;

    /// <summary>
    /// When set, creates an additional invalidation endpoint for each cached endpoint.
    /// The invalidation endpoint has the same path with this suffix appended.
    /// For example, if a cached endpoint is /api/my-endpoint/ and this is set to "invalidate",
    /// an invalidation endpoint /api/my-endpoint/invalidate will be created.
    /// Calling the invalidation endpoint with the same parameters removes the cached entry.
    /// Default is null (no invalidation endpoints created).
    /// </summary>
    public string? InvalidateCacheSuffix { get; set; } = null;
}
