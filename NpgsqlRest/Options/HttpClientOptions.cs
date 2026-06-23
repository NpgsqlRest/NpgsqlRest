namespace NpgsqlRest;

public class HttpClientOptions
{
    /// <summary>
    /// Enable HTTP client functionality for annotated types.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Default name for the response status code field within annotated types.
    /// </summary>
    public string ResponseStatusCodeField { get; set; } = "status_code";
    
    /// <summary>
    /// Default name for the response body field within annotated types.
    /// </summary>
    public string ResponseBodyField { get; set; } = "body";
    
    /// <summary>
    /// Default name for the response headers field within annotated types.
    /// </summary>
    public string ResponseHeadersField { get; set; } = "headers";
    
    /// <summary>
    /// Default name for the response content type field within annotated types.
    /// </summary>
    public string ResponseContentTypeField { get; set; } = "content_type";

    /// <summary>
    /// Default name for the response success field within annotated types.
    /// </summary>
    public string ResponseSuccessField { get; set; } = "success";
    
    /// <summary>
    /// Default name for the response error message field within annotated types.
    /// </summary>
    public string ResponseErrorMessageField { get; set; } = "error_message";

    /// <summary>
    /// Base URL for resolving relative paths in HTTP type definitions (e.g., "GET /api/test").
    /// When set, relative URLs are prefixed with this base URL. When null, the server's
    /// own listening address is auto-detected at runtime from the first incoming request.
    /// Example: "http://localhost:5000"
    /// </summary>
    public string? SelfBaseUrl { get; set; }

    /// <summary>
    /// Global kill switch for HTTP type response caching. When false, the <c>@cache</c> directive on
    /// individual types is ignored and every request fires a fresh outbound call. Default is true,
    /// so caching is opt-in per type via <c>@cache</c> and globally disable-able here.
    /// </summary>
    public bool CacheEnabled { get; set; } = true;

    /// <summary>
    /// Maximum number of distinct cached HTTP responses held in memory. Once the cache is full, new
    /// responses are not cached (existing entries are still served and expire normally). Bounds memory
    /// for types whose URL/body/headers contain per-request placeholders. Default is 10000.
    /// </summary>
    public int MaxCacheEntries { get; set; } = 10_000;

    /// <summary>
    /// Interval in seconds at which expired cached HTTP responses are pruned from memory.
    /// Default is 60 seconds.
    /// </summary>
    public int CachePruneIntervalSeconds { get; set; } = 60;
}
