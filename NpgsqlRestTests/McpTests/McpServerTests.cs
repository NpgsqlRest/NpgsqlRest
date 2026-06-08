using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

[Collection("McpPluginFixture")]
public class McpServerTests(McpPluginTestFixture test)
{
    private async Task<JsonNode> RpcAsync(string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/mcp", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        return JsonNode.Parse(body)!;
    }

    [Fact]
    public async Task Initialize_returns_protocol_capabilities_and_serverinfo()
    {
        var r = await RpcAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        r["jsonrpc"]!.GetValue<string>().Should().Be("2.0");
        r["id"]!.GetValue<int>().Should().Be(1);
        var result = r["result"]!;
        result["protocolVersion"]!.GetValue<string>().Should().Be("2025-11-25");
        result["capabilities"]!["tools"].Should().NotBeNull();          // tools capability advertised
        result["serverInfo"]!["name"]!.GetValue<string>().Should().Be("NpgsqlRest");
    }

    [Fact]
    public async Task Tools_list_returns_the_catalog()
    {
        var r = await RpcAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        var tools = r["result"]!["tools"]!.AsArray();
        var basic = tools.FirstOrDefault(t => t!["name"]!.GetValue<string>() == "tool_basic");
        basic.Should().NotBeNull();
        basic!["description"]!.GetValue<string>().Should().Be("Fetch basic data for the agent.");
        basic["inputSchema"]!["type"]!.GetValue<string>().Should().Be("object");
    }

    [Fact]
    public async Task Ping_returns_empty_result()
    {
        var r = await RpcAsync("""{"jsonrpc":"2.0","id":3,"method":"ping"}""");
        r["result"]!.AsObject().Count.Should().Be(0);
    }

    [Fact]
    public async Task Unknown_method_returns_jsonrpc_method_not_found()
    {
        var r = await RpcAsync("""{"jsonrpc":"2.0","id":4,"method":"bogus/method"}""");
        r["error"]!["code"]!.GetValue<int>().Should().Be(-32601);
        r["id"]!.GetValue<int>().Should().Be(4);
    }

    [Fact]
    public async Task Initialized_notification_returns_202_with_no_body()
    {
        using var content = new StringContent(
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/mcp", content);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Tools_call_executes_the_routine_and_returns_text_content()
    {
        var r = await RpcAsync("""{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"tool_basic","arguments":{}}}""");
        var result = r["result"]!;
        result["isError"]!.GetValue<bool>().Should().BeFalse();
        result["content"]!.AsArray()[0]!["type"]!.GetValue<string>().Should().Be("text");
        result["content"]!.AsArray()[0]!["text"]!.GetValue<string>().Should().Be("basic");
    }

    [Fact]
    public async Task Tools_call_passes_arguments_through()
    {
        // tool_params(id int, label default) ignores its args (returns 'p'); this exercises the
        // query-string build path without error.
        var r = await RpcAsync("""{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"tool_params","arguments":{"id":5}}}""");
        r["result"]!["isError"]!.GetValue<bool>().Should().BeFalse();
        r["result"]!["content"]!.AsArray()[0]!["text"]!.GetValue<string>().Should().Be("p");
    }

    [Fact]
    public async Task Tools_call_unknown_tool_is_a_jsonrpc_error()
    {
        var r = await RpcAsync("""{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"nope","arguments":{}}}""");
        r["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
    }
}
