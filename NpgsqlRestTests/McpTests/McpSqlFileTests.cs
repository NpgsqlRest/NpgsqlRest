using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

/// <summary>
/// MCP works with SqlFileSource endpoints (generated from .sql files), not just database routines —
/// for both single-command and multi-command SQL files.
/// </summary>
[Collection("McpSqlFileFixture")]
public class McpSqlFileTests(McpSqlFileTestFixture test)
{
    private async Task<string> CallAsync(string tool)
    {
        var json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\""
            + tool + "\",\"arguments\":{}}}";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/mcp", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public void SqlFile_tools_are_in_the_catalog()
        // mcp_sql_mcp_only is the bare-@mcp file (no HTTP tag); not_an_endpoint (no annotations) is absent.
        => string.Join(",", test.Tools.Keys.OrderBy(k => k)).Should().Be("mcp_sql_mcp_only,mcp_sql_multi,mcp_sql_single");

    [Fact]
    public async Task Bare_mcp_sql_file_without_http_tag_is_an_mcp_only_tool()
    {
        // Callable over MCP...
        (await CallAsync("mcp_sql_mcp_only")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"items\":[7]}"}],"isError":false,"structuredContent":{"items":[7]}}}""");

        // ...but with NO public REST route (internal-only by default — no HTTP tag was declared).
        using var rest = await test.Client.GetAsync("/api/mcp-sql-mcp-only");
        rest.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sql_file_without_any_annotation_is_not_exposed_anywhere()
    {
        // Not a tool (asserted in the catalog test) and not a REST endpoint.
        using var rest = await test.Client.GetAsync("/api/not-an-endpoint");
        rest.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Single_command_sql_file_tool_executes()
        => (await CallAsync("mcp_sql_single")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"items\":[42]}"}],"isError":false,"structuredContent":{"items":[42]}}}""");

    [Fact]
    public async Task Multi_command_sql_file_tool_executes_and_returns_each_result_set()
        => (await CallAsync("mcp_sql_multi")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"result1\":[1],\"result2\":[2]}"}],"isError":false,"structuredContent":{"result1":[1],"result2":[2]}}}""");

    [Fact]
    public void Multi_command_tool_omits_outputSchema_since_its_shape_is_not_derivable()
        => test.Tools["mcp_sql_multi"]!.ToJsonString().Should().Be(
            """{"name":"mcp_sql_multi","description":"Multi-command SQL file tool.","inputSchema":{"type":"object","properties":{}},"annotations":{"readOnlyHint":true}}""");
}
