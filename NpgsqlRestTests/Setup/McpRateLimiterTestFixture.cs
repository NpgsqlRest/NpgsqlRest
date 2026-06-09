using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NpgsqlRest.Mcp;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("McpRateLimiterFixture")]
public class McpRateLimiterFixtureCollection : ICollectionFixture<McpRateLimiterTestFixture> { }

/// <summary>
/// Fixture proving <see cref="McpOptions.RateLimiterPolicy"/> is applied to the whole <c>/mcp</c> endpoint.
/// The host registers a fixed-window policy that allows a single request per (long) window; the second
/// request to <c>/mcp</c> must be rejected by the rate limiter with 429.
/// </summary>
public class McpRateLimiterTestFixture : IDisposable
{
    public const string PolicyName = "mcp_test_policy";

    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public HttpClient Client => _client;

    public McpRateLimiterTestFixture()
    {
        Database.Create();
        var connectionString = Database.CreateAdditional("mcp_rate_limiter_test");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        // One permit per (long) window, no queue → the second request inside the window is rejected.
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddFixedWindowLimiter(PolicyName, opt =>
            {
                opt.PermitLimit = 1;
                opt.Window = TimeSpan.FromMinutes(10);
                opt.QueueLimit = 0;
            });
        });

        _app = builder.Build();
        _app.UseRateLimiter();

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            IncludeSchemas = ["mcp"],
            CommentsMode = CommentsMode.OnlyAnnotated,
            EndpointCreateHandlers =
            [
                new Mcp(new McpOptions
                {
                    Enabled = true,
                    RateLimiterPolicy = PolicyName,
                })
            ]
        });

        _app.StartAsync().GetAwaiter().GetResult();
        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
        _client.Timeout = TimeSpan.FromHours(1);
    }

#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
    public void Dispose()
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
    {
        _client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }
}
