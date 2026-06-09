using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using NpgsqlRest.Mcp;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("McpToolNameFixture")]
public class McpToolNameFixtureCollection : ICollectionFixture<McpToolNameTestFixture> { }

/// <summary>
/// Tool-name collision handling. The isolated <c>mcp_names</c> schema has two routines forced to the
/// same tool name via <c>@mcp_name</c>; the plugin must keep one and warn about the other.
/// </summary>
public class McpToolNameTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly LogCollector _logCollector = new();
    private readonly Mcp _mcp = new(new McpOptions { Enabled = true });

    public IReadOnlyList<LogEntry> StartupLogs { get; }
    public IReadOnlyDictionary<string, JsonObject> Tools => _mcp.Tools;

    public McpToolNameTestFixture()
    {
        Database.Create();
        var connectionString = Database.CreateAdditional("mcp_toolname_test");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddProvider(new CollectingLoggerProvider(_logCollector));
        _app = builder.Build();

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            IncludeSchemas = ["mcp_names"],
            CommentsMode = CommentsMode.OnlyAnnotated,
            EndpointCreateHandlers = [_mcp],
        });

        _app.StartAsync().GetAwaiter().GetResult();
        StartupLogs = _logCollector.Snapshot();
    }

#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
    public void Dispose()
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
    {
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }
}
