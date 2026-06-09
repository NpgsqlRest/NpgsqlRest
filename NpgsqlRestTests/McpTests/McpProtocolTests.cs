using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    // Demonstrative tools for the end-to-end / edge-case MCP protocol tests. Added to the same isolated
    // `mcp` schema as the annotation tests (excluded from every other fixture). These exercise argument
    // flow (GET query string + POST JSON body), method hints (DELETE), and the business-error channel.
    public static void McpProtocolDemoTools()
    {
        script.Append(@"
create schema if not exists mcp;

-- echoes its argument back verbatim → shows arguments reach the routine and the result returns as-is.
create function mcp.demo_echo(message text) returns text language sql as 'select message';
comment on function mcp.demo_echo(text) is '
HTTP GET
@mcp Echo the message back.';

-- POST tool → arguments are mapped onto a JSON request body.
create function mcp.demo_create(label text) returns text language sql as 'select ''created: '' || label';
comment on function mcp.demo_create(text) is '
HTTP POST
@mcp Create a labelled item.';

-- DELETE tool → tools/list advertises destructiveHint.
create function mcp.demo_remove(id int) returns text language sql as 'select ''removed''';
comment on function mcp.demo_remove(int) is '
HTTP DELETE
@mcp Remove an item by id.';

-- raises → exercises the business-error channel (isError:true result, NOT a JSON-RPC error).
create function mcp.demo_fail() returns text language plpgsql as $$
begin
  raise exception 'boom';
end;
$$;
comment on function mcp.demo_fail() is '
HTTP GET
@mcp Always fails.';
");
    }
}

/// <summary>
/// End-to-end / edge-case walk-through of the MCP wire protocol against a live endpoint. Reads top to
/// bottom as a description of how the server behaves: lifecycle, the two error channels, argument
/// mapping for GET vs POST, method-derived hints, renamed tools, and malformed input.
/// </summary>
[Collection("McpPluginFixture")]
public class McpProtocolTests(McpPluginTestFixture test)
{
    /// <summary>POSTs to /mcp and returns the status and the raw response body.</summary>
    private async Task<(HttpStatusCode Status, string Body)> PostAsync(string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/mcp", content);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    // ---- Lifecycle ---------------------------------------------------------

    [Fact]
    public async Task Initialize_negotiates_the_protocol_version_and_advertises_tools()
    {
        var (status, body) = await PostAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        status.Should().Be(HttpStatusCode.OK);
        body.Should().Be("""{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-11-25","capabilities":{"tools":{}},"serverInfo":{"name":"npgsql_rest_test","version":"1.0.0"}}}""");
    }

    // ---- Argument mapping --------------------------------------------------

    [Fact]
    public async Task Tools_call_passes_arguments_to_a_GET_tool_via_query_string_and_returns_the_result()
    {
        // demo_echo(message) returns the message verbatim → it flows through the query string and back.
        var (status, body) = await PostAsync(
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"demo_echo","arguments":{"message":"hello agent"}}}""");
        status.Should().Be(HttpStatusCode.OK);
        body.Should().Be("""{"jsonrpc":"2.0","id":2,"result":{"content":[{"type":"text","text":"{\"value\":\"hello agent\"}"}],"isError":false,"structuredContent":{"value":"hello agent"}}}""");
    }

    [Fact]
    public async Task Tools_call_maps_arguments_to_a_JSON_body_for_a_POST_tool()
    {
        var (status, body) = await PostAsync(
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"demo_create","arguments":{"label":"widget"}}}""");
        status.Should().Be(HttpStatusCode.OK);
        body.Should().Be("""{"jsonrpc":"2.0","id":3,"result":{"content":[{"type":"text","text":"{\"value\":\"created: widget\"}"}],"isError":false,"structuredContent":{"value":"created: widget"}}}""");
    }

    [Fact]
    public async Task Tools_call_with_no_arguments_object_is_treated_as_empty()
    {
        var (status, body) = await PostAsync(
            """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"tool_basic"}}""");
        status.Should().Be(HttpStatusCode.OK);
        body.Should().Be("""{"jsonrpc":"2.0","id":4,"result":{"content":[{"type":"text","text":"{\"value\":\"basic\"}"}],"isError":false,"structuredContent":{"value":"basic"}}}""");
    }

    [Fact]
    public async Task Tools_call_by_a_renamed_tool_executes_under_the_mcp_name()
    {
        // tool_named (returns 'n') is published as `cancel_booking` via @mcp_name.
        var (_, ok) = await PostAsync(
            """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"cancel_booking","arguments":{}}}""");
        ok.Should().Be("""{"jsonrpc":"2.0","id":5,"result":{"content":[{"type":"text","text":"{\"value\":\"n\"}"}],"isError":false,"structuredContent":{"value":"n"}}}""");

        // ...so calling the original routine name is an unknown tool.
        var (_, gone) = await PostAsync(
            """{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"tool_named","arguments":{}}}""");
        gone.Should().Be("""{"jsonrpc":"2.0","id":6,"error":{"code":-32602,"message":"Unknown tool: tool_named"}}""");
    }

    // ---- Method-derived hints ---------------------------------------------

    [Fact]
    public async Task Tools_list_marks_a_DELETE_tool_as_destructive()
    {
        // Assert the full shape of the DELETE tool (catalog grows, so target the one tool).
        var (_, body) = await PostAsync("""{"jsonrpc":"2.0","id":7,"method":"tools/list"}""");
        var remove = JsonNode.Parse(body)!["result"]!["tools"]!.AsArray()
            .First(t => t!["name"]!.GetValue<string>() == "demo_remove");
        remove!.ToJsonString().Should().Be("""{"name":"demo_remove","description":"Remove an item by id.","inputSchema":{"type":"object","properties":{"id":{"type":"integer","format":"int32"}},"required":["id"]},"annotations":{"readOnlyHint":false,"destructiveHint":true},"outputSchema":{"type":"object","properties":{"value":{"type":["string","null"]}}}}""");
    }

    // ---- Error channels ----------------------------------------------------

    [Fact]
    public async Task Tools_call_business_failure_is_a_result_with_isError_true_not_a_jsonrpc_error()
    {
        // demo_fail raises → business-error channel: transport 200, isError:true, the ProblemDetails
        // serialized into the text block (title = the exception message, detail = the SQLSTATE), and NO
        // structuredContent. It is NOT a structural JSON-RPC error.
        var (status, body) = await PostAsync(
            """{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"demo_fail","arguments":{}}}""");
        status.Should().Be(HttpStatusCode.OK);
        body.Should().Be("""{"jsonrpc":"2.0","id":8,"result":{"content":[{"type":"text","text":"{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.5.1\",\"title\":\"boom\",\"status\":400,\"detail\":\"P0001\"}"}],"isError":true}}""");
    }

    [Fact]
    public async Task Malformed_json_is_a_jsonrpc_parse_error()
    {
        var (status, body) = await PostAsync("{ this is not json ");
        status.Should().Be(HttpStatusCode.OK);
        body.Should().Be("""{"jsonrpc":"2.0","id":null,"error":{"code":-32700,"message":"Parse error"}}""");
    }

    [Fact]
    public async Task Unknown_method_is_method_not_found()
    {
        var (_, body) = await PostAsync("""{"jsonrpc":"2.0","id":9,"method":"does/not/exist"}""");
        body.Should().Be("""{"jsonrpc":"2.0","id":9,"error":{"code":-32601,"message":"Method not found: does/not/exist"}}""");
    }

    // ---- Transport ---------------------------------------------------------

    [Fact]
    public async Task A_GET_to_the_mcp_endpoint_is_method_not_allowed()
    {
        using var response = await test.Client.GetAsync("/mcp");
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    private static HttpRequestMessage Ping(string? origin = null, string? protocolVersion = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", Encoding.UTF8, "application/json"),
        };
        if (origin is not null) req.Headers.TryAddWithoutValidation("Origin", origin);
        if (protocolVersion is not null) req.Headers.TryAddWithoutValidation("MCP-Protocol-Version", protocolVersion);
        return req;
    }

    [Fact]
    public async Task A_request_from_an_untrusted_origin_is_forbidden()
    {
        // DNS-rebinding protection: a present, non-matching Origin is rejected.
        using var response = await test.Client.SendAsync(Ping(origin: "https://evil.example.com"));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_request_from_the_servers_own_origin_is_allowed()
    {
        var self = test.Client.BaseAddress!.GetLeftPart(UriPartial.Authority);
        using var response = await test.Client.SendAsync(Ping(origin: self));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unsupported_protocol_version_header_is_a_bad_request()
    {
        using var response = await test.Client.SendAsync(Ping(protocolVersion: "1999-01-01"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_negotiated_protocol_version_header_is_accepted()
    {
        using var response = await test.Client.SendAsync(Ping(protocolVersion: "2025-11-25"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
