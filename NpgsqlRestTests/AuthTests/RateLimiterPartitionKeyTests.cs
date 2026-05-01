using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NpgsqlRestClient;

namespace NpgsqlRestTests.AuthTests;

/// <summary>
/// Unit tests for <see cref="Builder.ResolvePartitionKey"/>. These exercise the partition-key
/// resolution logic in isolation (no HTTP server, no rate limiter) so failure cases like "claim
/// missing on this request" or "header not set" can be probed without spinning up a full pipeline.
///
/// The contract under test:
/// - Sources are walked top-to-bottom; the first source that returns a non-empty value wins.
/// - If no source matches, the fallback key <c>"unpartitioned"</c> is returned (so the policy
///   still rate-limits coherently rather than throwing).
/// - <c>BypassAuthenticated</c> is NOT evaluated here — the caller short-circuits on it before
///   calling ResolvePartitionKey.
/// </summary>
public class RateLimiterPartitionKeyTests
{
    private static HttpContext NewContext(
        IEnumerable<Claim>? claims = null,
        string? remoteIp = null,
        IDictionary<string, string>? headers = null)
    {
        var ctx = new DefaultHttpContext();
        if (claims is not null)
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
        }
        if (remoteIp is not null)
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        }
        if (headers is not null)
        {
            foreach (var kvp in headers)
            {
                ctx.Request.Headers[kvp.Key] = kvp.Value;
            }
        }
        return ctx;
    }

    [Fact]
    public void Claim_source_returns_claim_value_when_present()
    {
        var partition = new RateLimitPartitionConfig
        {
            Sources = [new() { Type = RateLimitPartitionSourceType.Claim, Name = "name_identifier" }]
        };
        var ctx = NewContext(claims: [new Claim("name_identifier", "user_a")]);

        Builder.ResolvePartitionKey(ctx, partition).Should().Be("user_a");
    }

    [Fact]
    public void Claim_source_falls_back_to_unpartitioned_when_claim_missing()
    {
        var partition = new RateLimitPartitionConfig
        {
            Sources = [new() { Type = RateLimitPartitionSourceType.Claim, Name = "name_identifier" }]
        };
        var ctx = NewContext(claims: [new Claim("other_claim", "x")]);

        Builder.ResolvePartitionKey(ctx, partition).Should().Be("unpartitioned");
    }

    [Fact]
    public void IpAddress_source_returns_remote_ip_string()
    {
        var partition = new RateLimitPartitionConfig
        {
            Sources = [new() { Type = RateLimitPartitionSourceType.IpAddress }]
        };
        var ctx = NewContext(remoteIp: "203.0.113.42");

        Builder.ResolvePartitionKey(ctx, partition).Should().Be("203.0.113.42");
    }

    [Fact]
    public void IpAddress_source_falls_back_to_unpartitioned_when_no_remote_ip()
    {
        var partition = new RateLimitPartitionConfig
        {
            Sources = [new() { Type = RateLimitPartitionSourceType.IpAddress }]
        };
        var ctx = NewContext();

        Builder.ResolvePartitionKey(ctx, partition).Should().Be("unpartitioned");
    }

    [Fact]
    public void IpAddress_source_honors_x_forwarded_for_over_remote_ip()
    {
        // Reverse-proxy scenario: Connection.RemoteIpAddress is the proxy's address; the actual client
        // IP is in X-Forwarded-For. ResolvePartitionKey delegates to GetClientIpAddress() which prefers
        // the forwarded header, so per-client buckets work behind a load balancer.
        var partition = new RateLimitPartitionConfig
        {
            Sources = [new() { Type = RateLimitPartitionSourceType.IpAddress }]
        };
        var ctx = NewContext(
            remoteIp: "10.0.0.1", // proxy
            headers: new Dictionary<string, string> { { "X-Forwarded-For", "203.0.113.42, 10.0.0.1" } });

        Builder.ResolvePartitionKey(ctx, partition).Should().Be("203.0.113.42");
    }

    [Fact]
    public void Header_source_returns_header_value_when_present()
    {
        var partition = new RateLimitPartitionConfig
        {
            Sources = [new() { Type = RateLimitPartitionSourceType.Header, Name = "X-Api-Key" }]
        };
        var ctx = NewContext(headers: new Dictionary<string, string> { { "X-Api-Key", "abc-123" } });

        Builder.ResolvePartitionKey(ctx, partition).Should().Be("abc-123");
    }

    [Fact]
    public void Header_source_falls_back_to_unpartitioned_when_header_missing()
    {
        var partition = new RateLimitPartitionConfig
        {
            Sources = [new() { Type = RateLimitPartitionSourceType.Header, Name = "X-Api-Key" }]
        };
        var ctx = NewContext();

        Builder.ResolvePartitionKey(ctx, partition).Should().Be("unpartitioned");
    }

    [Fact]
    public void Static_source_always_returns_configured_value()
    {
        var partition = new RateLimitPartitionConfig
        {
            Sources = [new() { Type = RateLimitPartitionSourceType.Static, Value = "global" }]
        };
        var ctx = NewContext();

        Builder.ResolvePartitionKey(ctx, partition).Should().Be("global");
    }

    [Fact]
    public void First_matching_source_wins_when_multiple_sources_resolve()
    {
        // Both Claim and IpAddress would resolve. Claim is listed first so it must win.
        var partition = new RateLimitPartitionConfig
        {
            Sources =
            [
                new() { Type = RateLimitPartitionSourceType.Claim, Name = "name_identifier" },
                new() { Type = RateLimitPartitionSourceType.IpAddress }
            ]
        };
        var ctx = NewContext(
            claims: [new Claim("name_identifier", "user_a")],
            remoteIp: "203.0.113.42");

        Builder.ResolvePartitionKey(ctx, partition).Should().Be("user_a");
    }

    [Fact]
    public void Falls_through_to_next_source_when_first_source_returns_empty()
    {
        // Claim is missing, so resolution falls through to IpAddress.
        var partition = new RateLimitPartitionConfig
        {
            Sources =
            [
                new() { Type = RateLimitPartitionSourceType.Claim, Name = "name_identifier" },
                new() { Type = RateLimitPartitionSourceType.IpAddress }
            ]
        };
        var ctx = NewContext(remoteIp: "203.0.113.42");

        Builder.ResolvePartitionKey(ctx, partition).Should().Be("203.0.113.42");
    }

    [Fact]
    public void Static_fallback_at_end_replaces_unpartitioned()
    {
        // Common pattern: Claim → IpAddress → Static fallback. With nothing available, we land
        // on the Static source instead of "unpartitioned".
        var partition = new RateLimitPartitionConfig
        {
            Sources =
            [
                new() { Type = RateLimitPartitionSourceType.Claim, Name = "name_identifier" },
                new() { Type = RateLimitPartitionSourceType.IpAddress },
                new() { Type = RateLimitPartitionSourceType.Static, Value = "anonymous" }
            ]
        };
        var ctx = NewContext();

        Builder.ResolvePartitionKey(ctx, partition).Should().Be("anonymous");
    }

    [Fact]
    public void Empty_sources_array_returns_unpartitioned()
    {
        var partition = new RateLimitPartitionConfig { Sources = [] };
        var ctx = NewContext();

        Builder.ResolvePartitionKey(ctx, partition).Should().Be("unpartitioned");
    }
}
