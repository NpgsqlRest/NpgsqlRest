namespace NpgsqlRest.HttpClientType;

public class HttpTypeDefinition
{
    public string Method { get; set; } = default!;
    public string Url { get; set; } = default!;
    public Dictionary<string, string>? Headers { get; set; }
    public string? Body { get; set; }
    public string? ContentType { get; set; }
    public TimeSpan? Timeout { get; set; }
    public TimeSpan[]? RetryDelays { get; set; }
    public HashSet<int>? RetryOnStatusCodes { get; set; }
    public bool NeedsParsing { get; set; }

    /// <summary>
    /// True when the type comment carries a <c>@cache</c> directive, opting this HTTP type into
    /// response caching. Successful responses are cached and reused for matching requests
    /// (same method + resolved URL + headers + body) until <see cref="CacheDuration"/> elapses.
    /// </summary>
    public bool CacheEnabled { get; set; }

    /// <summary>
    /// Time-to-live for cached responses. <c>null</c> when <see cref="CacheEnabled"/> is true means
    /// the response is cached with no expiration (until the process restarts).
    /// </summary>
    public TimeSpan? CacheDuration { get; set; }
}