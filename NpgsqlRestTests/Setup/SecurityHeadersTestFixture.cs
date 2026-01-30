using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("SecurityHeadersTestFixture")]
public class SecurityHeadersTestFixtureCollection : ICollectionFixture<SecurityHeadersTestFixture> { }

/// <summary>
/// Test fixture for Security Headers middleware tests.
/// Creates a web application with security headers enabled to verify:
/// - X-Content-Type-Options header is set correctly
/// - X-Frame-Options header is set correctly
/// - Referrer-Policy header is set correctly
/// - Content-Security-Policy header is set when configured
/// - Other security headers (COOP, COEP, CORP) work correctly
/// </summary>
public class SecurityHeadersTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public HttpClient Client => _client;
    public string ServerAddress { get; }

    /// <summary>
    /// Expected security header values (matching appsettings defaults)
    /// </summary>
    public const string ExpectedXContentTypeOptions = "nosniff";
    public const string ExpectedXFrameOptions = "DENY";
    public const string ExpectedReferrerPolicy = "strict-origin-when-cross-origin";
    public const string ExpectedContentSecurityPolicy = "default-src 'self'; script-src 'self'";
    public const string ExpectedPermissionsPolicy = "geolocation=(), microphone=()";
    public const string ExpectedCrossOriginOpenerPolicy = "same-origin";
    public const string ExpectedCrossOriginEmbedderPolicy = "require-corp";
    public const string ExpectedCrossOriginResourcePolicy = "same-origin";

    public SecurityHeadersTestFixture()
    {
        var connectionString = Database.Create();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        _app = builder.Build();

        // Add security headers middleware (mimics what NpgsqlRestClient does)
        _app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;

            headers["X-Content-Type-Options"] = ExpectedXContentTypeOptions;
            headers["X-Frame-Options"] = ExpectedXFrameOptions;
            headers["Referrer-Policy"] = ExpectedReferrerPolicy;
            headers["Content-Security-Policy"] = ExpectedContentSecurityPolicy;
            headers["Permissions-Policy"] = ExpectedPermissionsPolicy;
            headers["Cross-Origin-Opener-Policy"] = ExpectedCrossOriginOpenerPolicy;
            headers["Cross-Origin-Embedder-Policy"] = ExpectedCrossOriginEmbedderPolicy;
            headers["Cross-Origin-Resource-Policy"] = ExpectedCrossOriginResourcePolicy;

            await next();
        });

        // Add a simple test endpoint
        _app.MapGet("/test", () => Results.Ok(new { message = "Hello" }));

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
