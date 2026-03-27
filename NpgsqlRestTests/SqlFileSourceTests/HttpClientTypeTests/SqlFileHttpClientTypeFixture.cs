using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using NpgsqlRest.SqlFileSource;
using NpgsqlRest.HttpClientType;
using WireMock.Server;
using WireMock.Settings;

namespace NpgsqlRestTests.SqlFileSourceTests;

[CollectionDefinition("SqlFileHttpClientTypeFixture")]
public class SqlFileHttpClientTypeFixtureCollection : ICollectionFixture<SqlFileHttpClientTypeFixture> { }

/// <summary>
/// Test fixture for SQL file endpoints using HTTP client types.
/// Combines SqlFileSource with HttpClientOptions and a WireMock server.
/// </summary>
public class SqlFileHttpClientTypeFixture : IDisposable
{
    public const int MockPort = 50954;

    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly string _sqlDir;

    public HttpClient Client => _client;
    public WireMockServer Server { get; }

    public SqlFileHttpClientTypeFixture()
    {
        var connectionString = Database.Create();

        Server = WireMockServer.Start(new WireMockServerSettings { Port = MockPort });

        // Create SQL types for HTTP client types in the test database
        using var conn = Database.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            create type sf_http_body as (body text);
            comment on type sf_http_body is 'GET http://localhost:{MockPort}/api/sf-test1';

            create type sf_http_full as (body text, status_code int, success bool);
            comment on type sf_http_full is 'GET http://localhost:{MockPort}/api/sf-test2';
        ";
        cmd.ExecuteNonQuery();

        // Create temp directory for SQL files
        _sqlDir = Path.Combine(Path.GetTempPath(), "npgsqlrest_sf_http_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_sqlDir);

        // Write test SQL files
        WriteSqlFiles(_sqlDir);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        _app = builder.Build();

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            CommentsMode = CommentsMode.ParseAll,
            HttpClientOptions = new HttpClientOptions { Enabled = true },
            EndpointSources =
            [
                new SqlFileSource(new SqlFileSourceOptions
                {
                    FilePattern = _sqlDir.Replace('\\', '/') + "/**/*.sql",
                    CommentsMode = CommentsMode.ParseAll,
                    CommentScope = CommentScope.All,
                    ErrorMode = ParseErrorMode.Skip,
                })
            ],
        });

        _app.StartAsync().GetAwaiter().GetResult();

        var serverAddress = _app.Urls.First();
        _client = new HttpClient { BaseAddress = new Uri(serverAddress), Timeout = TimeSpan.FromHours(1) };
    }

    private static void WriteSqlFiles(string dir)
    {
        // Test 1: Basic HTTP type — get body from HTTP call
        File.WriteAllText(Path.Combine(dir, "sf_http_body_test.sql"), """
            -- HTTP GET
            -- @param $1 req sf_http_body
            SELECT ($1::sf_http_body).body as response_body;
            """);

        // Test 2: HTTP type with multiple fields
        File.WriteAllText(Path.Combine(dir, "sf_http_full_test.sql"), """
            -- HTTP GET
            -- @param $1 req sf_http_full
            SELECT ($1::sf_http_full).body as response_body, ($1::sf_http_full).status_code as status, ($1::sf_http_full).success as ok;
            """);
    }

#pragma warning disable CA1816
    public void Dispose()
#pragma warning restore CA1816
    {
        _client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
        Server.Stop();
        try { if (Directory.Exists(_sqlDir)) Directory.Delete(_sqlDir, true); } catch { }
    }
}
