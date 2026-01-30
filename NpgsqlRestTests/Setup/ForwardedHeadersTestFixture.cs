using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("ForwardedHeadersTestFixture")]
public class ForwardedHeadersTestFixtureCollection : ICollectionFixture<ForwardedHeadersTestFixture> { }

/// <summary>
/// Test fixture for Forwarded Headers middleware tests.
/// Creates a web application with forwarded headers enabled to verify:
/// - X-Forwarded-For header is processed correctly
/// - X-Forwarded-Proto header is processed correctly
/// - X-Forwarded-Host header is processed correctly
/// - Known proxies filtering works
/// </summary>
public class ForwardedHeadersTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public HttpClient Client => _client;
    public string ServerAddress { get; }

    /// <summary>
    /// Test values for forwarded headers
    /// </summary>
    public const string TestClientIp = "203.0.113.195";
    public const string TestProxyIp = "70.41.3.18";
    public const string TestForwardedProto = "https";
    public const string TestForwardedHost = "example.com";

    public ForwardedHeadersTestFixture()
    {
        var connectionString = Database.Create();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Configure forwarded headers
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = 2; // Allow 2 proxies in chain
            // Clear defaults to allow any proxy for testing
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
        });

        _app = builder.Build();

        // Use forwarded headers middleware
        _app.UseForwardedHeaders();

        // Add an endpoint that returns connection info for verification
        _app.MapGet("/connection-info", (HttpContext context) =>
        {
            return Results.Ok(new
            {
                remoteIpAddress = context.Connection.RemoteIpAddress?.ToString(),
                scheme = context.Request.Scheme,
                host = context.Request.Host.ToString(),
                isHttps = context.Request.IsHttps
            });
        });

        // Add NpgsqlRest for API endpoint testing
        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            IncludeSchemas = ["public"],
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false
        });

        _app.StartAsync().GetAwaiter().GetResult();

        ServerAddress = _app.Urls.First();

        _client = new HttpClient { BaseAddress = new Uri(ServerAddress) };
        _client.Timeout = TimeSpan.FromMinutes(5);
    }

#pragma warning disable CA1816
    public void Dispose()
#pragma warning restore CA1816
    {
        _client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }
}
