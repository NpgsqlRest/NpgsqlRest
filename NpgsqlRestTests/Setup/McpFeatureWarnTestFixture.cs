using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using NpgsqlRest.Mcp;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("McpFeatureWarnFixture")]
public class McpFeatureWarnFixtureCollection : ICollectionFixture<McpFeatureWarnTestFixture> { }

/// <summary>
/// Fixture for the MCP non-applicable-feature warning. The isolated <c>mcp_warn</c> schema holds a
/// routine that is annotated <c>@mcp</c> but is also a <c>login</c> endpoint — a feature that does not
/// translate to an MCP tool call. The plugin should log a warning at endpoint-creation time. Logs are
/// captured so the test can assert the warning fired.
/// </summary>
public class McpFeatureWarnTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly LogCollector _logCollector = new();

    public IReadOnlyList<LogEntry> StartupLogs { get; }

    public McpFeatureWarnTestFixture()
    {
        Database.Create();
        var connectionString = Database.CreateAdditional("mcp_feature_warn_test");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddProvider(new CollectingLoggerProvider(_logCollector));
        _app = builder.Build();

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            IncludeSchemas = ["mcp_warn"],
            CommentsMode = CommentsMode.OnlyAnnotated,
            EndpointCreateHandlers = [new Mcp(new McpOptions { Enabled = true })]
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
