using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

[Collection("McpPluginFixture")]
public class McpServerTests(McpPluginTestFixture test)
{
    /// <summary>POSTs a JSON-RPC request to /mcp, asserts 200, and returns the raw response body.</summary>
    private async Task<string> RpcAsync(string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/mcp", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Initialize_returns_protocol_capabilities_and_serverinfo()
    {
        // ServerName is unset in the fixture, so serverInfo.name falls back to the database name.
        var body = await RpcAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        body.Should().Be("""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-11-25","capabilities":{"tools":{}},"serverInfo":{"name":"npgsql_rest_test","version":"1.0.0"}}}""");
    }

    [Fact]
    public async Task Tools_list_returns_the_catalog()
    {
        // The catalog grows as demo tools are added, so assert the full shape of one representative tool.
        var r = JsonNode.Parse(await RpcAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}"""))!;
        var basic = r["result"]!["tools"]!.AsArray().First(t => t!["name"]!.GetValue<string>() == "tool_basic");
        basic!.ToJsonString().Should().Be("""{"name":"tool_basic","description":"Fetch basic data for the agent.","inputSchema":{"type":"object","properties":{}},"annotations":{"readOnlyHint":true},"outputSchema":{"type":"object","properties":{"value":{"type":["string","null"]}}}}""");
    }

    [Fact]
    public async Task Ping_returns_empty_result()
    {
        var body = await RpcAsync("""{"jsonrpc":"2.0","id":3,"method":"ping"}""");
        body.Should().Be("""{"jsonrpc":"2.0","id":3,"result":{}}""");
    }

    [Fact]
    public async Task Unknown_method_returns_jsonrpc_method_not_found()
    {
        var body = await RpcAsync("""{"jsonrpc":"2.0","id":4,"method":"bogus/method"}""");
        body.Should().Be("""{"jsonrpc":"2.0","id":4,"error":{"code":-32601,"message":"Method not found: bogus/method"}}""");
    }

    [Fact]
    public async Task Initialized_notification_returns_202_with_no_body()
    {
        using var content = new StringContent(
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/mcp", content);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Tools_call_executes_the_routine_and_returns_text_and_structured_content()
    {
        // tool_basic returns the scalar text 'basic' → structuredContent { "value": "basic" }; the text
        // content block carries that serialized (with relaxed JSON escaping, so quotes are \").
        var body = await RpcAsync("""{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"tool_basic","arguments":{}}}""");
        body.Should().Be("""{"jsonrpc":"2.0","id":5,"result":{"content":[{"type":"text","text":"{\"value\":\"basic\"}"}],"isError":false,"structuredContent":{"value":"basic"}}}""");
    }

    [Fact]
    public async Task Tools_call_passes_arguments_through()
    {
        // tool_params(id int, label default) ignores its args (returns 'p'); exercises the query-string path.
        var body = await RpcAsync("""{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"tool_params","arguments":{"id":5}}}""");
        body.Should().Be("""{"jsonrpc":"2.0","id":6,"result":{"content":[{"type":"text","text":"{\"value\":\"p\"}"}],"isError":false,"structuredContent":{"value":"p"}}}""");
    }

    [Fact]
    public async Task Tools_call_unknown_tool_is_a_jsonrpc_error()
    {
        var body = await RpcAsync("""{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"nope","arguments":{}}}""");
        body.Should().Be("""{"jsonrpc":"2.0","id":7,"error":{"code":-32602,"message":"Unknown tool: nope"}}""");
    }

    [Fact]
    public async Task Tools_call_on_an_authorized_tool_by_anonymous_caller_returns_401()
    {
        // tool_authorized has `@authorize admin`; the fixture has no auth middleware, so the forwarded
        // principal is anonymous and the execution pipeline returns 401 → translated to an HTTP challenge.
        using var content = new StringContent(
            """{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"tool_authorized","arguments":{}}}""",
            Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/mcp", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("resource_metadata=");
    }

    [Fact]
    public async Task Protected_resource_metadata_is_served_at_the_well_known_path()
    {
        // RFC 9728: the well-known path is "/.well-known/oauth-protected-resource" + the resource path ("/mcp").
        using var response = await test.Client.GetAsync("/.well-known/oauth-protected-resource/mcp");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        // resource is derived from the request origin (host:port vary) + UrlPath, no explicit Audience.
        var origin = test.Client.BaseAddress!.GetLeftPart(UriPartial.Authority);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("{\"resource\":\"" + origin + "/mcp\",\"authorization_servers\":[\"https://as.example.com\"]," +
                         "\"bearer_methods_supported\":[\"header\"],\"scopes_supported\":[\"mcp.read\",\"mcp.write\"]}");
    }
}
