using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using NpgsqlRest.Mcp;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("McpToolSchemaFixture")]
public class McpToolSchemaFixtureCollection : ICollectionFixture<McpToolSchemaTestFixture> { }

/// <summary>
/// Fixture for the MCP ToolSchemas documents (OpenAI/Anthropic function-calling schemas + llms.txt),
/// scoped to the isolated <c>mcp_schemas</c> schema. The /mcp endpoint is enabled, ToolSchemas write
/// files to a temp directory and serve the default URL paths; <c>_user_id</c> is claim-mapped so its
/// exclusion from the generated documents can be asserted.
/// </summary>
public class McpToolSchemaTestFixture : IDisposable
{
    public static string OutputPath { get; } = Path.Combine(Path.GetTempPath(), "NpgsqlRestTests", "McpToolSchemas");

    private readonly WebApplication _app;
    private readonly Mcp _mcp;

    public HttpClient Client { get; }
    public IReadOnlyDictionary<string, JsonObject> Tools => _mcp.Tools;

    public McpToolSchemaTestFixture()
    {
        Database.Create();
        var connectionString = Database.CreateAdditional("mcp_toolschemas_test");

        _mcp = new Mcp(new McpOptions
        {
            Enabled = true,
            ServerName = "Test API",
            Instructions = "Test instructions for agents.",
            ToolSchemas = new McpToolSchemaOptions
            {
                Enabled = true,
                FileOverwrite = true,
                OpenAiFileName = Path.Combine(OutputPath, "tools_openai.json"),
                AnthropicFileName = Path.Combine(OutputPath, "tools_anthropic.json"),
                LlmsTxtFileName = Path.Combine(OutputPath, "llms.txt"),
                OpenApiUrlPath = "/openapi.json",
            },
        });

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            IncludeSchemas = ["mcp_schemas"],
            CommentsMode = CommentsMode.OnlyAnnotated,
            AuthenticationOptions = new()
            {
                DefaultUserIdClaimType = "name_identifier",
                UseUserParameters = true,
                ParameterNameClaimsMapping = new() { { "_user_id", "name_identifier" } },
            },
            EndpointCreateHandlers = [_mcp],
        });

        _app.StartAsync().GetAwaiter().GetResult();
        Client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()), Timeout = TimeSpan.FromHours(1) };
    }

#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
    public void Dispose()
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
    {
        Client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }
}

[CollectionDefinition("McpToolSchemaDisabledFixture")]
public class McpToolSchemaDisabledFixtureCollection : ICollectionFixture<McpToolSchemaDisabledTestFixture> { }

/// <summary>
/// Fixture proving the ToolSchemas documents are generated and served from `mcp` annotations even
/// when the /mcp endpoint itself is disabled (McpOptions.Enabled = false). No files are written
/// (FileNames null); documents are served at the default URL paths only.
/// </summary>
public class McpToolSchemaDisabledTestFixture : IDisposable
{
    private readonly WebApplication _app;

    public HttpClient Client { get; }

    public McpToolSchemaDisabledTestFixture()
    {
        Database.Create();
        var connectionString = Database.CreateAdditional("mcp_toolschemas_disabled_test");

        var mcp = new Mcp(new McpOptions
        {
            Enabled = false,
            ToolSchemas = new McpToolSchemaOptions
            {
                Enabled = true,
                OpenAiFileName = null,
                AnthropicFileName = null,
                LlmsTxtFileName = null,
            },
        });

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            IncludeSchemas = ["mcp_schemas"],
            CommentsMode = CommentsMode.OnlyAnnotated,
            EndpointCreateHandlers = [mcp],
        });

        _app.StartAsync().GetAwaiter().GetResult();
        Client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()), Timeout = TimeSpan.FromHours(1) };
    }

#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
    public void Dispose()
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
    {
        Client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }
}
