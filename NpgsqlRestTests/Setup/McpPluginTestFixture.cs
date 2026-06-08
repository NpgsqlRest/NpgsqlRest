using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using NpgsqlRest.Mcp;
using NpgsqlRest.OpenAPI;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("McpPluginFixture")]
public class McpPluginFixtureCollection : ICollectionFixture<McpPluginTestFixture> { }

/// <summary>
/// Fixture for the NpgsqlRest.Mcp plugin annotation layer (Phase 2 increment 1). Loads the Mcp
/// plugin as an endpoint-create handler so its <c>HandleCommentLine</c> hook runs during core's
/// comment-parse pass, scoped to an isolated <c>mcp</c> schema (excluded from every other fixture).
/// Captures the parsed endpoints via EndpointsCreated so tests can assert the per-endpoint
/// <see cref="McpToolInfo"/> stored in <c>RoutineEndpoint.Items["mcp"]</c>, plus InternalOnly and the
/// prose left in UnhandledCommentLines.
/// </summary>
public class McpPluginTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly Mcp _mcp = new(new McpOptions
    {
        Enabled = true,
        // OAuth 2.1 Resource Server config: an AS is configured, so the Protected Resource Metadata
        // (RFC 9728) document is served. RequireAuthorization stays false — anonymous requests still work.
        Authorization = new McpAuthorizationOptions
        {
            AuthorizationServers = ["https://as.example.com"],
            ScopesSupported = ["mcp.read", "mcp.write"],
        }
    });

    public HttpClient Client => _client;

    /// <summary>Fully-parsed endpoints captured at build time, keyed by routine name.</summary>
    public Dictionary<string, RoutineEndpoint> Endpoints { get; } = new(StringComparer.Ordinal);

    /// <summary>The MCP tool catalog produced by the plugin (tool name → tools/list Tool object).</summary>
    public IReadOnlyDictionary<string, JsonObject> Tools => _mcp.Tools;

    public McpPluginTestFixture()
    {
        Database.Create();
        var connectionString = Database.CreateAdditional("mcp_plugin_test");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); // random available port

        _app = builder.Build();

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            IncludeSchemas = ["mcp"],
            // OnlyAnnotated: an endpoint is created only if its comment has an HTTP tag OR a plugin
            // requests an endpoint (e.g. `mcp`). OpenApi is loaded too so we can assert that a pure
            // modifier (`openapi hide`) on a non-HTTP routine does NOT spawn an endpoint.
            CommentsMode = CommentsMode.OnlyAnnotated,
            EndpointCreateHandlers = [_mcp, new OpenApi(new OpenApiOptions())],
            EndpointsCreated = endpoints =>
            {
                foreach (var endpoint in endpoints)
                {
                    Endpoints[endpoint.Routine.Name] = endpoint;
                }
            }
        });

        _app.StartAsync().GetAwaiter().GetResult();

        var serverAddress = _app.Urls.First();
        _client = new HttpClient { BaseAddress = new Uri(serverAddress) };
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
