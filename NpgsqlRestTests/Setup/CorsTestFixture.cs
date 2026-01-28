using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("CorsTestFixture")]
public class CorsTestFixtureCollection : ICollectionFixture<CorsTestFixture> { }

/// <summary>
/// Test fixture for CORS (Cross-Origin Resource Sharing) tests.
/// Creates a web application with CORS enabled to verify:
/// - Preflight OPTIONS requests work correctly
/// - AllowedOrigins restricts correctly
/// - AllowCredentials header is present when configured
/// - Methods/Headers filtering works
/// </summary>
public class CorsTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public HttpClient Client => _client;
    public string ServerAddress { get; }

    /// <summary>
    /// The allowed origin used in CORS configuration
    /// </summary>
    public const string AllowedOrigin = "https://allowed-origin.example.com";

    /// <summary>
    /// A disallowed origin for testing rejection
    /// </summary>
    public const string DisallowedOrigin = "https://disallowed-origin.example.com";

    public CorsTestFixture()
    {
        var connectionString = Database.Create();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Configure CORS with specific settings
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("TestCorsPolicy", policy =>
            {
                policy.WithOrigins(AllowedOrigin)
                      .WithMethods("GET", "POST", "PUT", "DELETE")
                      .WithHeaders("Content-Type", "Authorization", "X-Custom-Header")
                      .AllowCredentials()
                      .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
            });
        });

        _app = builder.Build();

        // CORS must be before routing/endpoints
        _app.UseCors("TestCorsPolicy");

        // Add NpgsqlRest for API endpoint testing
        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            IncludeSchemas = ["public"],
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false
        });

        _app.StartAsync().GetAwaiter().GetResult();

        ServerAddress = _app.Urls.First();

        // Create HttpClient without any default headers
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
