using NpgsqlRest.Mcp;
using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    // Tools for the ToolSchemas documents, isolated in the `mcp_schemas` schema:
    // - search_orders: GET with two params, one with a PG default (required-array behavior)
    // - create_order:  POST (method rendering in llms.txt)
    // - whoami:        `_user_id` is claim-mapped in the fixture, so it must be excluded from the
    //                  generated schemas (exclusion inherited from BuildTool, not rebuilt)
    // - weird_tool:    explicit mcp_name with characters outside [a-zA-Z0-9_-] (sanitization)
    public static void McpToolSchemaTools()
    {
        script.Append(@"
create schema if not exists mcp_schemas;

create function mcp_schemas.search_orders(_customer text, _limit int default 10) returns text
language sql as 'select _customer';
comment on function mcp_schemas.search_orders(text, int) is '
HTTP GET
@mcp Search orders by customer name.';

create function mcp_schemas.create_order(_title text) returns int
language sql as 'select 1';
comment on function mcp_schemas.create_order(text) is '
HTTP POST
@mcp Create a new order.';

create function mcp_schemas.whoami(_user_id text) returns text
language sql as 'select coalesce(_user_id, ''anonymous'')';
comment on function mcp_schemas.whoami(text) is '
HTTP GET
@mcp Return the calling user id.';

create function mcp_schemas.weird_tool() returns text
language sql as 'select ''w''';
comment on function mcp_schemas.weird_tool() is '
HTTP GET
mcp_name web.tool/name!
@mcp Tool with a name that needs sanitization.';
");
    }
}

[Collection("McpToolSchemaFixture")]
public class McpToolSchemaTests(McpToolSchemaTestFixture test)
{
    private async Task<(JsonArray Doc, string ContentType)> GetJsonArrayAsync(string path)
    {
        using var response = await test.Client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var contentType = response.Content.Headers.ContentType!.ToString();
        var body = await response.Content.ReadAsStringAsync();
        return (JsonNode.Parse(body)!.AsArray(), contentType);
    }

    [Fact]
    public async Task OpenAi_document_envelope_and_parameters_match_the_mcp_inputSchema()
    {
        var (doc, contentType) = await GetJsonArrayAsync("/tools/openai.json");
        contentType.Should().StartWith("application/json");

        var entry = doc.First(t => t!["function"]!["name"]!.GetValue<string>() == "search_orders")!;
        entry["type"]!.GetValue<string>().Should().Be("function");
        var function = entry["function"]!.AsObject();
        function["description"]!.GetValue<string>().Should().Be("Search orders by customer name.");

        // parameters must be the MCP inputSchema, verbatim.
        var expected = test.Tools["search_orders"]["inputSchema"]!.ToJsonString();
        function["parameters"]!.ToJsonString().Should().Be(expected);

        // required excludes the defaulted parameter.
        var required = function["parameters"]!["required"]!.AsArray().Select(r => r!.GetValue<string>());
        required.Should().BeEquivalentTo(["customer"]);

        // No strict / additionalProperties / annotations / outputSchema projection.
        function.ContainsKey("strict").Should().BeFalse();
        entry.AsObject().ContainsKey("annotations").Should().BeFalse();
        entry.AsObject().ContainsKey("outputSchema").Should().BeFalse();
    }

    [Fact]
    public async Task Anthropic_document_input_schema_matches_the_mcp_inputSchema_with_no_extra_keys()
    {
        var (doc, contentType) = await GetJsonArrayAsync("/tools/anthropic.json");
        contentType.Should().StartWith("application/json");

        var entry = doc.First(t => t!["name"]!.GetValue<string>() == "search_orders")!.AsObject();
        entry.Select(kv => kv.Key).Should().Equal("name", "description", "input_schema");
        entry["description"]!.GetValue<string>().Should().Be("Search orders by customer name.");

        var expected = test.Tools["search_orders"]["inputSchema"]!.ToJsonString();
        entry["input_schema"]!.ToJsonString().Should().Be(expected);
    }

    [Fact]
    public async Task Claim_mapped_parameter_is_excluded_from_both_documents()
    {
        var (openAi, _) = await GetJsonArrayAsync("/tools/openai.json");
        var openAiProps = openAi.First(t => t!["function"]!["name"]!.GetValue<string>() == "whoami")!
            ["function"]!["parameters"]!["properties"]!.AsObject();
        openAiProps.Count.Should().Be(0, "the claim-mapped _user_id must be excluded (inherited from BuildTool)");

        var (anthropic, _) = await GetJsonArrayAsync("/tools/anthropic.json");
        var anthropicProps = anthropic.First(t => t!["name"]!.GetValue<string>() == "whoami")!
            ["input_schema"]!["properties"]!.AsObject();
        anthropicProps.Count.Should().Be(0);
    }

    [Fact]
    public async Task Tool_name_is_sanitized_in_json_documents_and_original_in_llms_txt()
    {
        var (openAi, _) = await GetJsonArrayAsync("/tools/openai.json");
        openAi.Select(t => t!["function"]!["name"]!.GetValue<string>())
            .Should().Contain("web_tool_name_")
            .And.NotContain("web.tool/name!");

        var (anthropic, _) = await GetJsonArrayAsync("/tools/anthropic.json");
        anthropic.Select(t => t!["name"]!.GetValue<string>()).Should().Contain("web_tool_name_");

        using var response = await test.Client.GetAsync("/llms.txt");
        var llms = await response.Content.ReadAsStringAsync();
        llms.Should().Contain("### web.tool/name!", "llms.txt headings keep the original tool name");
    }

    [Fact]
    public void Colliding_sanitized_names_fail_fast()
    {
        var tools = new Dictionary<string, JsonObject>(StringComparer.Ordinal)
        {
            ["a.b"] = new JsonObject { ["name"] = "a.b", ["description"] = "x", ["inputSchema"] = new JsonObject() },
            ["a!b"] = new JsonObject { ["name"] = "a!b", ["description"] = "y", ["inputSchema"] = new JsonObject() },
        };

        var act = () => Mcp.BuildFunctionCallingDocuments(tools);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*'a!b'*")
            .WithMessage("*'a.b'*")
            .WithMessage("*'a_b'*");
    }

    [Fact]
    public async Task Llms_txt_is_served_and_written_with_the_expected_markdown()
    {
        using var response = await test.Client.GetAsync("/llms.txt");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.ToString().Should().StartWith("text/markdown");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be(ExpectedLlmsTxt);

        var filePath = Path.Combine(McpToolSchemaTestFixture.OutputPath, "llms.txt");
        File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");
        File.ReadAllText(filePath).Should().Be(ExpectedLlmsTxt);
    }

    private const string ExpectedLlmsTxt = """
# Test API

> Test instructions for agents.

## Endpoints

### create_order
- Method: POST /api/mcp-schemas/create-order
- Description: Create a new order.
- Parameters:
  - `title` (string, required)

### search_orders
- Method: GET /api/mcp-schemas/search-orders
- Description: Search orders by customer name.
- Parameters:
  - `customer` (string, required)
  - `limit` (integer, default available)

### web.tool/name!
- Method: GET /api/mcp-schemas/weird-tool
- Description: Tool with a name that needs sanitization.
- Parameters: none

### whoami
- Method: GET /api/mcp-schemas/whoami
- Description: Return the calling user id.
- Parameters: none

## Machine-readable
- OpenAPI: /openapi.json
- MCP endpoint: /mcp
- OpenAI tools: /tools/openai.json
- Anthropic tools: /tools/anthropic.json

""";
}

[Collection("McpToolSchemaDisabledFixture")]
public class McpToolSchemaDisabledTests(McpToolSchemaDisabledTestFixture test)
{
    [Fact]
    public async Task Documents_are_generated_and_served_even_when_the_mcp_endpoint_is_disabled()
    {
        using var response = await test.Client.GetAsync("/tools/openai.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
        doc.Select(t => t!["function"]!["name"]!.GetValue<string>()).Should().Contain("search_orders");

        // The /mcp endpoint itself is not served.
        using var content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", Encoding.UTF8, "application/json");
        using var mcpResponse = await test.Client.PostAsync("/mcp", content);
        mcpResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
