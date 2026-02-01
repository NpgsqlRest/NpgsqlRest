using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using NpgsqlRestClient;
using Npgsql;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("StatsTestFixture")]
public class StatsTestFixtureCollection : ICollectionFixture<StatsTestFixture> { }

/// <summary>
/// Test fixture for PostgreSQL Stats endpoint tests.
/// Creates a web application with stats endpoints enabled to verify:
/// - /stats/routines endpoint returns function/procedure statistics
/// - /stats/tables endpoint returns table statistics
/// - /stats/indexes endpoint returns index statistics
/// - /stats/activity endpoint returns current session activity
/// </summary>
public class StatsTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly string _connectionString;

    public HttpClient Client => _client;
    public string ServerAddress { get; }

    /// <summary>
    /// Stats endpoint paths
    /// </summary>
    public const string RoutinesPath = "/stats/routines";
    public const string TablesPath = "/stats/tables";
    public const string IndexesPath = "/stats/indexes";
    public const string ActivityPath = "/stats/activity";

    public StatsTestFixture()
    {
        _connectionString = Database.Create();

        // Create some test data for stats
        CreateTestData();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        _app = builder.Build();

        // Map stats endpoints manually (simulating what Program.cs does)
        _app.MapGet(RoutinesPath, async (Microsoft.AspNetCore.Http.HttpContext context) =>
            await StatsEndpoints.HandleRoutinesStats(context, _connectionString, "json", null, null));

        _app.MapGet(TablesPath, async (Microsoft.AspNetCore.Http.HttpContext context) =>
            await StatsEndpoints.HandleTablesStats(context, _connectionString, "json", null, null));

        _app.MapGet(IndexesPath, async (Microsoft.AspNetCore.Http.HttpContext context) =>
            await StatsEndpoints.HandleIndexesStats(context, _connectionString, "json", null, null));

        _app.MapGet(ActivityPath, async (Microsoft.AspNetCore.Http.HttpContext context) =>
            await StatsEndpoints.HandleActivityStats(context, _connectionString, "json", null));

        // Add HTML endpoints for testing HTML format
        _app.MapGet("/stats/tables-html", async (Microsoft.AspNetCore.Http.HttpContext context) =>
            await StatsEndpoints.HandleTablesStats(context, _connectionString, "html", null, null));

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

    private void CreateTestData()
    {
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();

        // Create a test table for table stats
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS stats_test_table (
                id serial PRIMARY KEY,
                name text NOT NULL,
                created_at timestamptz DEFAULT now()
            );

            CREATE INDEX IF NOT EXISTS idx_stats_test_name ON stats_test_table(name);

            INSERT INTO stats_test_table (name)
            SELECT 'test_' || i FROM generate_series(1, 10) AS i
            ON CONFLICT DO NOTHING;

            CREATE OR REPLACE FUNCTION stats_test_function() RETURNS integer AS $$
            BEGIN
                RETURN 42;
            END;
            $$ LANGUAGE plpgsql;

            SELECT stats_test_function();
            """;
        command.ExecuteNonQuery();
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
