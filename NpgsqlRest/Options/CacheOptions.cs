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
}
