using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using NpgsqlRest.SqlFileSource;
using WireMock.Server;
using WireMock.Settings;

namespace NpgsqlRestTests.SqlFileSourceTests;

[CollectionDefinition("SqlFileProxyFixture")]
public class SqlFileProxyFixtureCollection : ICollectionFixture<SqlFileProxyFixture> { }

/// <summary>
/// Test fixture for SQL file endpoints with proxy features enabled.
/// Combines SqlFileSource with ProxyOptions and a WireMock server.
/// </summary>
public class SqlFileProxyFixture : IDisposable
{
    public const int MockPort = 50955;

    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly string _sqlDir;

    public HttpClient Client => _client;
    public WireMockServer Server { get; }
    public string BaseAddress { get; }

    public SqlFileProxyFixture()
    {
        var connectionString = Database.Create();

        Server = WireMockServer.Start(new WireMockServerSettings { Port = MockPort });

        // Create temp directory for SQL files
        _sqlDir = Path.Combine(Path.GetTempPath(), "npgsqlrest_sf_proxy_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_sqlDir);

        // Write test SQL files
        WriteSqlFiles(_sqlDir);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        _app = builder.Build();

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            CommentsMode = CommentsMode.ParseAll,
            ProxyOptions = new()
            {
                Enabled = true,
                Host = $"http://localhost:{MockPort}",
            },
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

        BaseAddress = _app.Urls.First();
        _client = new HttpClient { BaseAddress = new Uri(BaseAddress), Timeout = TimeSpan.FromHours(1) };

        // Set up self-client for relative path proxy calls
        var selfClient = new HttpClient { BaseAddress = new Uri(BaseAddress), Timeout = TimeSpan.FromHours(1) };
        NpgsqlRest.Proxy.ProxyRequestHandler.SetSelfClient(selfClient);
    }

    private static void WriteSqlFiles(string dir)
    {
        // 1. Proxy passthrough — forwards request to upstream, returns upstream response directly
        File.WriteAllText(Path.Combine(dir, "sf_proxy_passthrough.sql"), """
            -- HTTP GET
            -- proxy
            select 1;
            """);

        // 2. Proxy passthrough with explicit method override
        File.WriteAllText(Path.Combine(dir, "sf_proxy_method_override.sql"), """
            -- HTTP GET
            -- proxy POST
            select 1;
            """);

        // 3. Proxy passthrough with explicit host URL
        File.WriteAllText(Path.Combine(dir, "sf_proxy_explicit_host.sql"), """
            -- HTTP GET
            -- proxy GET http://localhost:50955
            select 1;
            """);

        // 4. Proxy with response body parameter — executes SQL with proxy response
        //    param must come before proxy so parameter names are set before proxy detection
        File.WriteAllText(Path.Combine(dir, "sf_proxy_transform_body.sql"), """
            -- HTTP GET
            -- param $1 _proxy_body text
            -- proxy
            select 'Received: ' || coalesce($1, 'NULL') as result;
            """);

        // 5. Proxy with multiple response parameters
        File.WriteAllText(Path.Combine(dir, "sf_proxy_transform_all.sql"), """
            -- HTTP GET
            -- param $1 _proxy_status_code integer
            -- param $2 _proxy_body text
            -- param $3 _proxy_success boolean
            -- proxy
            select $1::integer as status_code, $2::text as body, $3::boolean as success;
            """);

        // 6. Proxy out — executes SQL first, then forwards result to upstream
        File.WriteAllText(Path.Combine(dir, "sf_proxy_out_basic.sql"), """
            -- HTTP GET
            -- proxy_out POST
            select '{"key":"value","number":42}'::json as result;
            """);

        // 7. Self-referencing proxy — calls another endpoint in this same app
        //    This endpoint returns a simple text, which the proxy passthrough will return
        File.WriteAllText(Path.Combine(dir, "sf_self_target.sql"), """
            -- HTTP GET
            select 'Hello from SQL file' as message;
            """);

        // 8. Self-referencing proxy passthrough
        File.WriteAllText(Path.Combine(dir, "sf_proxy_self_passthrough.sql"), """
            -- HTTP GET
            -- proxy GET /api/sf-self-target
            select 1;
            """);

        // 9. Self-referencing proxy with response parameters
        File.WriteAllText(Path.Combine(dir, "sf_proxy_self_transform.sql"), """
            -- HTTP GET
            -- param $1 _proxy_status_code integer
            -- param $2 _proxy_body text
            -- param $3 _proxy_success boolean
            -- proxy GET /api/sf-self-target
            select $1::integer as status_code, $2::text as body, $3::boolean as success;
            """);

        // 10. Proxy with query parameters — user params + proxy response params
        File.WriteAllText(Path.Combine(dir, "sf_proxy_with_user_param.sql"), """
            -- HTTP GET
            -- param $1 name text
            -- param $2 _proxy_body text
            -- proxy
            select $1 as name, $2 as proxy_response;
            """);

        // 11. Limitation #1 test: proxy annotation BEFORE param annotations
        //     Tests that annotation ordering doesn't matter for proxy response parameter detection
        File.WriteAllText(Path.Combine(dir, "sf_proxy_order_body.sql"), """
            -- HTTP GET
            -- proxy
            -- param $1 _proxy_body text
            select 'Got: ' || coalesce($1, 'NULL') as result;
            """);

        // 12. Limitation #1 test: proxy annotation BEFORE multiple param annotations
        File.WriteAllText(Path.Combine(dir, "sf_proxy_order_all.sql"), """
            -- HTTP GET
            -- proxy
            -- param $1 _proxy_status_code integer
            -- param $2 _proxy_body text
            -- param $3 _proxy_success boolean
            select $1::integer as status_code, $2::text as body, $3::boolean as success;
            """);

        // 13. Limitation #2 test: proxy response params with types but WITHOUT explicit SQL casts
        //     Tests that param type annotations alone produce typed JSON output
        File.WriteAllText(Path.Combine(dir, "sf_proxy_no_cast.sql"), """
            -- HTTP GET
            -- param $1 _proxy_status_code integer
            -- param $2 _proxy_body text
            -- param $3 _proxy_success boolean
            -- proxy
            select $1 as status_code, $2 as body, $3 as success;
            """);

        // 14. Self-referencing proxy with response params, no explicit casts
        File.WriteAllText(Path.Combine(dir, "sf_proxy_self_no_cast.sql"), """
            -- HTTP GET
            -- param $1 _proxy_status_code integer
            -- param $2 _proxy_body text
            -- param $3 _proxy_success boolean
            -- proxy GET /api/sf-self-target
            select $1 as status_code, $2 as body, $3 as success;
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
