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
}
