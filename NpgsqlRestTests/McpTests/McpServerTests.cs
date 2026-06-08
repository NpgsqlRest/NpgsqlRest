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
}
