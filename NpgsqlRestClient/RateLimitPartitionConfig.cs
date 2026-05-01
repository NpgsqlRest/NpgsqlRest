namespace NpgsqlRestClient;

/// <summary>
/// Per-policy partitioning configuration. When set on a rate-limiter policy, each request resolves a partition
/// key and gets its own rate-limit bucket — typical pattern is "one bucket per user" or "one bucket per IP".
///
/// Without partitioning (the default), all requests hitting a named policy share a single global bucket.
/// </summary>
public class RateLimitPartitionConfig
{
    /// <summary>
    /// Ordered list of sources. Walked top-to-bottom at request time; the first source returning a non-empty
    /// key wins. If no source matches, a fixed fallback key (<c>"unpartitioned"</c>) is used so the policy
    /// still rate-limits coherently.
    /// </summary>
    public RateLimitPartitionSource[] Sources { get; set; } = [];

    /// <summary>
    /// When true, requests from authenticated users (<c>HttpContext.User.Identity.IsAuthenticated</c>) bypass
    /// rate limiting entirely (the policy returns no-limiter for that request). Useful for "anonymous users
    /// get throttled, signed-in users don't" patterns. Default false.
    ///
    /// Evaluated BEFORE <see cref="Sources"/> — if this is true and the user is authenticated, sources are
    /// not consulted at all.
    /// </summary>
    public bool BypassAuthenticated { get; set; } = false;
}

/// <summary>
/// One entry in <see cref="RateLimitPartitionConfig.Sources"/>. Each source describes how to derive a
/// partition key string from the current request.
/// </summary>
public class RateLimitPartitionSource
{
    /// <summary>What this source reads from the request to produce a key.</summary>
    public RateLimitPartitionSourceType Type { get; set; }

    /// <summary>
    /// For <see cref="RateLimitPartitionSourceType.Claim"/>: the claim type (e.g. <c>"name_identifier"</c>).
    /// For <see cref="RateLimitPartitionSourceType.Header"/>: the header name.
    /// Ignored for IpAddress and Static.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// For <see cref="RateLimitPartitionSourceType.Static"/>: the literal key value (terminal fallback).
    /// Ignored for other source types.
    /// </summary>
    public string? Value { get; set; }
}

public enum RateLimitPartitionSourceType
{
    /// <summary>Resolve from <c>HttpContext.User.FindFirst(Name)?.Value</c>. Returns null if claim missing.</summary>
    Claim,
    /// <summary>
    /// Resolve from the client IP via <c>HttpRequest.GetClientIpAddress()</c>: checks
    /// <c>X-Forwarded-For</c> (leftmost address) first, then <c>X-Real-IP</c> /
    /// <c>HTTP_X_FORWARDED_FOR</c> / <c>REMOTE_ADDR</c>, finally falling back to
    /// <c>Connection.RemoteIpAddress</c>. Returns null if no source yields a value.
    /// </summary>
    IpAddress,
    /// <summary>Resolve from <c>HttpContext.Request.Headers[Name]</c>. Returns null if header missing.</summary>
    Header,
    /// <summary>Always returns the configured <see cref="RateLimitPartitionSource.Value"/>. Use as a terminal fallback.</summary>
    Static,
}
