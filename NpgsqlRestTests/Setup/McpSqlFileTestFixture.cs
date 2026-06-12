using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using NpgsqlRest.Mcp;
using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("McpSqlFileFixture")]
public class McpSqlFileFixtureCollection : ICollectionFixture<McpSqlFileTestFixture> { }

/// <summary>
/// Confirms MCP works with the SqlFileSource (endpoints generated from .sql files), not just
/// database routines: a single-command and a multi-command SQL file, each opted in with `@mcp`.
/// </summary>
public class McpSqlFileTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly string _sqlDir;
    private readonly Mcp _mcp = new(new McpOptions { Enabled = true });

    public HttpClient Client => _client;
    public IReadOnlyDictionary<string, JsonObject> Tools => _mcp.Tools;

    public McpSqlFileTestFixture()
    {
        var connectionString = Database.Create();

        _sqlDir = Path.Combine(Path.GetTempPath(), "npgsqlrest_mcp_sql_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_sqlDir);

        // Single command.
        File.WriteAllText(Path.Combine(_sqlDir, "mcp_sql_single.sql"), """
            -- HTTP GET
            -- @mcp Single-command SQL file tool.
            select 42 as answer;
            """);

        // Multiple commands in one file.
        File.WriteAllText(Path.Combine(_sqlDir, "mcp_sql_multi.sql"), """
            -- HTTP GET
            -- @mcp Multi-command SQL file tool.
            select 1 as a;
            select 2 as b;
            """);

        // MCP-ONLY: bare @mcp, NO HTTP tag. The file must pass the SqlFileSource pre-gate (the `mcp`
        // annotation is endpoint-requesting), become a tool, and default to internal-only (no REST route).
        File.WriteAllText(Path.Combine(_sqlDir, "mcp_sql_mcp_only.sql"), """
            -- @mcp MCP-only SQL file tool.
            select 7 as lucky;
            """);

        // No HTTP tag, no endpoint-requesting annotation: a utility script matching the glob. Must be
        // skipped by the pre-gate — no endpoint, no tool.
        File.WriteAllText(Path.Combine(_sqlDir, "not_an_endpoint.sql"), """
            -- just a utility script, not an endpoint
            select 'should never be exposed';
            """);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            CommentsMode = CommentsMode.OnlyAnnotated,
            EndpointSources =
            [
                new SqlFileSource(new SqlFileSourceOptions
                {
                    FilePattern = _sqlDir.Replace('\\', '/') + "/**/*.sql",
                    CommentsMode = CommentsMode.OnlyAnnotated,
                })
            ],
            EndpointCreateHandlers = [_mcp],
        });

        _app.StartAsync().GetAwaiter().GetResult();
        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()), Timeout = TimeSpan.FromHours(1) };
    }

#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
    public void Dispose()
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
    {
        _client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
        try { Directory.Delete(_sqlDir, recursive: true); } catch { /* best effort */ }
    }
}
