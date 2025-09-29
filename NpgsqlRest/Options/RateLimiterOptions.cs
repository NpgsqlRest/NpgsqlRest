namespace NpgsqlRest;

public class RateLimiterOptions
{
    /// <summary>
    /// Rate limiting is disabled by default.
    /// </summary>
    public bool Enabled { get; set; } = false;
    /// <summary>
    /// Status code returned when rate limit is exceeded. Default is 429 (Too Many Requests).
    /// </summary>
    public int StatusCode { get; set; } = 429;
    /// <summary>
    /// Message returned when rate limit is exceeded.
    /// </summary>
    public string? Message { get; set; } = "Too many requests. Please try again later.";
    /// <summary>
    /// Default rate limiting policy for all requests. Policy must be configured in RateLimitingOptions.
    /// This can be overridden by comment annotations in the database or setting policy for specific endpoints.
    /// </summary>
    public string? DefaultPolicy { get; set; } = null;
}
