using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlRest;

namespace BenchmarkTests;

/// <summary>
/// Self-contained end-to-end endpoint benchmarks: a full HTTP round trip (HttpClient -> Kestrel on loopback ->
/// NpgsqlRest -> PostgreSQL -> JSON response) for two typical requests.
///
/// Purpose: a before/after yardstick for dependency and runtime upgrades. Run it before upgrading, keep the
/// report, run it again after upgrading on the same machine, and compare. No manual server is needed: global
/// setup creates its own database and starts an in-process server; global cleanup stops it and drops the database.
///
/// Requires PostgreSQL on localhost:5432 (postgres/postgres), the same instance the NpgsqlRestTests use,
/// or the connection string in NPGSQLREST_BENCHMARK_CONNECTION (maintenance database, e.g. Database=postgres).
///
/// Run with: dotnet run -c Release --project BenchmarkTests -- --endpoints
/// </summary>
[MemoryDiagnoser]
public class EndpointBenchmarks
{
    // Maintenance-database connection used to create/drop the benchmark database.
    // Override with the NPGSQLREST_BENCHMARK_CONNECTION environment variable to use a different PostgreSQL instance
    // (always compare runs made against the same instance).
    private static readonly string AdminConnectionString =
        Environment.GetEnvironmentVariable("NPGSQLREST_BENCHMARK_CONNECTION") is { Length: > 0 } connection
            ? connection
            : "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";
    private const string DatabaseName = "npgsql_rest_benchmark";

    private const string Script = """
        create function benchmark_get_text()
        returns text
        language sql
        stable
        as $$
        select 'Hello, World!'
        $$;

        create function benchmark_get_rows(_count int)
        returns table (
            id int,
            name text,
            active boolean,
            amount numeric,
            created_at timestamptz,
            tags text[],
            data jsonb
        )
        language sql
        stable
        as $$
        select
            i,
            'name ' || i,
            i % 2 = 0,
            i * 1.25,
            timestamptz '2026-01-01 00:00:00+00' + make_interval(mins => i),
            array['a' || i, 'b' || i],
            jsonb_build_object('id', i, 'label', 'item ' || i)
        from generate_series(1, _count) as i
        $$;

        comment on function benchmark_get_text() is 'HTTP GET';
        comment on function benchmark_get_rows(int) is 'HTTP GET';
        """;

    private const int RowCount = 100;

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private NpgsqlDataSource _dataSource = null!;

    [GlobalSetup]
    public void Setup()
    {
        var connectionString = CreateDatabase();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        // No logging: per-request log output would dominate the measurement
        builder.Logging.ClearProviders();

        _app = builder.Build();
        // Reference endpoint for HttpOnly: same routing style as NpgsqlRest (endpoint + RequestDelegate), no database
        _app.MapGet("/ping", context =>
        {
            context.Response.ContentType = "text/plain";
            return context.Response.WriteAsync("Hello, World!");
        });
        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "benchmark\\_%",
            RequiresAuthorization = false
        });
        _app.StartAsync().GetAwaiter().GetResult();

        _client = new HttpClient
        {
            BaseAddress = new Uri(_app.Urls.First()),
            Timeout = TimeSpan.FromSeconds(30)
        };

        _dataSource = NpgsqlDataSource.Create(connectionString);

        Verify();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _dataSource.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
        NpgsqlConnection.ClearAllPools();
        Execute(AdminConnectionString, $"drop database if exists {DatabaseName} with (force)");
    }

    /// <summary>
    /// Smallest possible round trip: one scalar text value. Measures fixed per-request overhead
    /// (routing, parameter handling, connection rent, command execution, response write).
    /// </summary>
    [Benchmark(Baseline = true)]
    public async Task<string> GetText()
    {
        using var response = await _client.GetAsync("/api/benchmark-get-text");
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// 100 rows with mixed column types (int, text, bool, numeric, timestamptz, text[], jsonb).
    /// Measures query parameter binding plus row-reading and JSON serialization throughput.
    /// </summary>
    [Benchmark]
    public async Task<string> GetRows100()
    {
        using var response = await _client.GetAsync($"/api/benchmark-get-rows?count={RowCount}");
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Reference: the HTTP stack alone (HttpClient -> Kestrel -> routing -> RequestDelegate), same response text,
    /// no NpgsqlRest, no database. Separates platform (ASP.NET Core / runtime) cost from NpgsqlRest cost.
    /// </summary>
    [Benchmark]
    public async Task<string> HttpOnly()
    {
        using var response = await _client.GetAsync("/ping");
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Reference: the database call alone (pooled connection open, execute, read scalar), no HTTP.
    /// Separates Npgsql + runtime cost from HTTP and NpgsqlRest cost.
    /// </summary>
    [Benchmark]
    public async Task<string> NpgsqlOnly()
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("select benchmark_get_text()", connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static string CreateDatabase()
    {
        Execute(AdminConnectionString, $"drop database if exists {DatabaseName} with (force)");
        Execute(AdminConnectionString, $"create database {DatabaseName}");
        var connectionString = new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = DatabaseName }.ConnectionString;
        Execute(connectionString, Script);
        return connectionString;
    }

    private static void Execute(string connectionString, string sql)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Fail fast if an endpoint is not what the benchmark assumes, so a broken setup (404, 500, wrong shape)
    /// is never measured and reported as a valid result.
    /// </summary>
    private void Verify()
    {
        using (var response = _client.GetAsync("/ping").GetAwaiter().GetResult())
        {
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode || body != "Hello, World!")
            {
                throw new InvalidOperationException($"ping: unexpected response {(int)response.StatusCode}: {body}");
            }
        }

        if (NpgsqlOnly().GetAwaiter().GetResult() != "Hello, World!")
        {
            throw new InvalidOperationException("NpgsqlOnly: unexpected result");
        }

        using (var response = _client.GetAsync("/api/benchmark-get-text").GetAwaiter().GetResult())
        {
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode || body != "Hello, World!")
            {
                throw new InvalidOperationException($"benchmark-get-text: unexpected response {(int)response.StatusCode}: {body}");
            }
        }

        using (var response = _client.GetAsync($"/api/benchmark-get-rows?count={RowCount}").GetAwaiter().GetResult())
        {
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"benchmark-get-rows: unexpected response {(int)response.StatusCode}: {body}");
            }
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() != RowCount)
            {
                throw new InvalidOperationException($"benchmark-get-rows: expected a JSON array of {RowCount} rows, got: {body[..Math.Min(body.Length, 300)]}");
            }
        }
    }
}
