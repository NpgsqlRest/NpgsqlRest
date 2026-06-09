using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using NpgsqlRest.Mcp;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("McpAuthGateFixture")]
public class McpAuthGateFixtureCollection : ICollectionFixture<McpAuthGateTestFixture> { }

/// <summary>
/// Fixture for the MCP OAuth 2.1 Resource Server transport gate. The Mcp plugin runs with
/// <c>RequireAuthorization = true</c> and an Authorization Server configured, but the host registers
/// NO authentication middleware — so every request is unauthenticated and must be rejected with 401
/// and a Protected Resource Metadata (RFC 9728) challenge.
/// </summary>
public class McpAuthGateTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly LogCollector _logCollector = new();

    public HttpClient Client => _client;

    /// <summary>Logs emitted during build/startup (used to assert the no-auth-scheme warning).</summary>
    public IReadOnlyList<LogEntry> StartupLogs { get; }

    public McpAuthGateTestFixture()
    {
        Database.Create();
        var connectionString = Database.CreateAdditional("mcp_auth_gate_test");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddProvider(new CollectingLoggerProvider(_logCollector));
        _app = builder.Build();

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            IncludeSchemas = ["mcp"],
            CommentsMode = CommentsMode.OnlyAnnotated,
            EndpointCreateHandlers =
            [
                new Mcp(new McpOptions
                {
                    Enabled = true,
                    Authorization = new McpAuthorizationOptions
                    {
                        RequireAuthorization = true,
                        AuthorizationServers = ["https://as.example.com"],
                    }
                })
            ]
        });

        _app.StartAsync().GetAwaiter().GetResult();
        StartupLogs = _logCollector.Snapshot();
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
