using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using NpgsqlRestClient;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("RateLimiterPartitionTestFixture")]
public class RateLimiterPartitionTestFixtureCollection : ICollectionFixture<RateLimiterPartitionTestFixture> { }

/// <summary>
/// End-to-end fixture verifying that partitioned rate-limiter policies actually deliver per-key
/// buckets. Wires up three policies via the same pattern <see cref="Builder.BuildRateLimiter"/>
/// emits — <c>options.AddPolicy(name, ctx => RateLimitPartition.GetFixedWindowLimiter(Builder.ResolvePartitionKey(ctx, p), ...))</c>
/// — and exposes three NpgsqlRest endpoints, one per policy.
///
/// Why this is structured as an integration test rather than a unit test:
/// per-key bucketing is a behavior of <c>System.Threading.RateLimiting</c>'s partitioned limiter,
/// not of our resolver. The unit tests in <c>RateLimiterPartitionKeyTests</c> already cover the
/// resolver in isolation. This fixture proves the wiring (resolver → AddPolicy → RequireRateLimiting)
/// holds together: that the partition key actually selects the bucket for the current request, and
/// that BypassAuthenticated short-circuits the limiter as advertised.
///
/// Three policies registered:
///   - <c>rlpt-per-claim</c>      → partitioned by claim "name_identifier"; fallback "anonymous"
///   - <c>rlpt-per-ip-fallback</c> → tries claim first, falls through to IP
///   - <c>rlpt-bypass-auth</c>     → BypassAuthenticated=true; anonymous users hit a Static bucket
///
/// Each policy has PermitLimit=2 and a 10-minute window (effectively no replenishment during a
/// test run), so the third call to a single bucket reliably returns 429.
/// </summary>
public class RateLimiterPartitionTestFixture : IDisposable
{
    private readonly WebApplication _app;

    public string ServerAddress { get; }

    public RateLimiterPartitionTestFixture()
    {
        var connectionString = Database.Create();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddAuthentication().AddCookie();

        // Pre-build partition configs that mirror what Builder.ReadPartitionConfig produces from JSON.
        var perClaimPartition = new RateLimitPartitionConfig
        {
            Sources =
            [
                new() { Type = RateLimitPartitionSourceType.Claim, Name = "name_identifier" },
                new() { Type = RateLimitPartitionSourceType.Static, Value = "anonymous" }
            ]
        };
        var perIpFallbackPartition = new RateLimitPartitionConfig
        {
            Sources =
            [
                new() { Type = RateLimitPartitionSourceType.Claim, Name = "name_identifier" },
                new() { Type = RateLimitPartitionSourceType.IpAddress }
            ]
        };
        var bypassAuthPartition = new RateLimitPartitionConfig
        {
            BypassAuthenticated = true,
            Sources = [new() { Type = RateLimitPartitionSourceType.Static, Value = "all-anon-share-bucket" }]
        };

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                await context.HttpContext.Response.WriteAsync("Rate limit exceeded. Please try again later.", cancellationToken);
            };

            // Policy 1: partitioned by claim. Each user_id gets its own bucket.
            options.AddPolicy("rlpt-per-claim", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    Builder.ResolvePartitionKey(httpContext, perClaimPartition),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 2,
                        Window = TimeSpan.FromMinutes(10),
                        QueueLimit = 0,
                        AutoReplenishment = false
                    }));

            // Policy 2: claim-with-IP-fallback. Unauthenticated requests get an IP bucket.
            options.AddPolicy("rlpt-per-ip-fallback", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    Builder.ResolvePartitionKey(httpContext, perIpFallbackPartition),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 2,
                        Window = TimeSpan.FromMinutes(10),
                        QueueLimit = 0,
                        AutoReplenishment = false
                    }));

            // Policy 3: BypassAuthenticated — auth users get NoLimiter, anonymous share one bucket.
            options.AddPolicy("rlpt-bypass-auth", httpContext =>
                bypassAuthPartition.BypassAuthenticated && httpContext.User?.Identity?.IsAuthenticated == true
                    ? RateLimitPartition.GetNoLimiter<string>("__authenticated__")
                    : RateLimitPartition.GetFixedWindowLimiter(
                        Builder.ResolvePartitionKey(httpContext, bypassAuthPartition),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 2,
                            Window = TimeSpan.FromMinutes(10),
                            QueueLimit = 0,
                            AutoReplenishment = false
                        }));
        });

        _app = builder.Build();
        _app.UseRateLimiter();

        // Sign-in endpoints: each issues a cookie tying the client to a specific name_identifier.
        _app.MapGet("/rlpt-login-a", () => Results.SignIn(new ClaimsPrincipal(new ClaimsIdentity(
            claims: new[] { new Claim("name_identifier", "rlpt_user_a") },
            authenticationType: CookieAuthenticationDefaults.AuthenticationScheme))));

        _app.MapGet("/rlpt-login-b", () => Results.SignIn(new ClaimsPrincipal(new ClaimsIdentity(
            claims: new[] { new Claim("name_identifier", "rlpt_user_b") },
            authenticationType: CookieAuthenticationDefaults.AuthenticationScheme))));

        _app.UseNpgsqlRest(new(connectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "rlpt[_]%",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
        });

        _app.StartAsync().GetAwaiter().GetResult();

        ServerAddress = _app.Urls.First();
    }

    /// <summary>
    /// Creates a fresh HttpClient bound to this fixture. Independent cookie containers per client
    /// allow two clients to sign in as different identities and exercise per-claim partitioning.
    /// </summary>
    public HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new System.Net.CookieContainer() };
        return new HttpClient(handler) { BaseAddress = new Uri(ServerAddress), Timeout = TimeSpan.FromMinutes(5) };
    }

#pragma warning disable CA1816
    public void Dispose()
#pragma warning restore CA1816
    {
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }
}
