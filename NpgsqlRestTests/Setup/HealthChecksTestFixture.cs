using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("HealthChecksTestFixture")]
public class HealthChecksTestFixtureCollection : ICollectionFixture<HealthChecksTestFixture> { }

/// <summary>
/// Test fixture for Health Checks endpoint tests.
/// Creates a web application with health check endpoints enabled to verify:
/// - /health endpoint returns overall health status
/// - /health/ready endpoint checks database connectivity
/// - /health/live endpoint always returns healthy (liveness probe)
/// - Health check caching works correctly
/// </summary>
public class HealthChecksTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly string _connectionString;

    public HttpClient Client => _client;
    public string ServerAddress { get; }

    /// <summary>
    /// Health check endpoint paths
    /// </summary>
    public const string HealthPath = "/health";
    public const string ReadyPath = "/health/ready";
    public const string LivePath = "/health/live";

    public HealthChecksTestFixture()
    {
        _connectionString = Database.Create();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Configure health checks with PostgreSQL check
        builder.Services.AddHealthChecks()
            .AddNpgSql(_connectionString, name: "postgresql", tags: ["ready"]);

        _app = builder.Build();

        // Map health check endpoints
        _app.MapHealthChecks(HealthPath);

        _app.MapHealthChecks(ReadyPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        });

        _app.MapHealthChecks(LivePath, new HealthCheckOptions
        {
            Predicate = _ => false // Always returns healthy - liveness probe
        });

        // Add NpgsqlRest for completeness
        _app.UseNpgsqlRest(new NpgsqlRestOptions(_connectionString)
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
