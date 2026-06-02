using System.Collections.Frozen;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using NpgsqlRestClient;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("RateLimiterPerPolicyTestFixture")]
public class RateLimiterPerPolicyTestFixtureCollection : ICollectionFixture<RateLimiterPerPolicyTestFixture> { }

/// <summary>
/// End-to-end fixture verifying per-policy <c>StatusCode</c>/<c>StatusMessage</c> overrides for rejected
/// (rate-limited) requests. The framework exposes only a single global <c>OnRejected</c>/
/// <c>RejectionStatusCode</c>, so <see cref="Builder.ApplyRateLimiterRejectionAsync"/> resolves the policy
/// that rejected the current request (via the endpoint's <see cref="EnableRateLimitingAttribute"/>) and
/// applies that policy's override, falling back to the global values when a policy has none. This fixture
/// wires the limiter the exact way <see cref="Builder.BuildRateLimiter"/> does and exercises the real helper.
///
/// Three FixedWindow policies, each PermitLimit=1 and a 10-minute window (no replenishment during a run),
/// so the second call to a policy reliably returns its configured rejection:
///   - <c>rlpm-login</c>   → override message only (status inherits global 429)
///   - <c>rlpm-strict</c>  → override BOTH status (503) and message
///   - <c>rlpm-inherit</c> → no override; inherits the global 429 + global message
/// </summary>
public class RateLimiterPerPolicyTestFixture : IDisposable
{
    public const string GlobalMessage = "Global rate limit hit.";
    public const string LoginMessage = "Too many login attempts. Please wait a minute and try again.";
    public const string StrictMessage = "Service busy. Retry shortly.";

    private readonly WebApplication _app;

    public string ServerAddress { get; }

    public RateLimiterPerPolicyTestFixture()
    {
        var connectionString = Database.Create();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Per-policy overrides keyed by policy name — mirrors the FrozenDictionary BuildRateLimiter assembles
        // from each policy's StatusCode/StatusMessage. "rlpm-inherit" is intentionally absent so it falls back.
        var overrides = new Dictionary<string, (int? StatusCode, string? Message)>
        {
            ["rlpm-login"] = (null, LoginMessage),
            ["rlpm-strict"] = (503, StrictMessage)
        }.ToFrozenDictionary();

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, cancellationToken) =>
                Builder.ApplyRateLimiterRejectionAsync(
                    context.HttpContext, overrides, StatusCodes.Status429TooManyRequests, GlobalMessage, cancellationToken);

            foreach (var name in new[] { "rlpm-login", "rlpm-strict", "rlpm-inherit" })
            {
                options.AddFixedWindowLimiter(name, config =>
                {
                    config.PermitLimit = 1;
                    config.Window = TimeSpan.FromMinutes(10);
                    config.QueueLimit = 0;
                    config.AutoReplenishment = false;
                });
            }
        });

        _app = builder.Build();
        _app.UseRateLimiter();

        _app.UseNpgsqlRest(new(connectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "rlpm[_]%",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
        });

        _app.StartAsync().GetAwaiter().GetResult();

        ServerAddress = _app.Urls.First();
    }

    public HttpClient CreateClient() =>
        new() { BaseAddress = new Uri(ServerAddress), Timeout = TimeSpan.FromMinutes(5) };

#pragma warning disable CA1816
    public void Dispose()
#pragma warning restore CA1816
    {
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }
}
