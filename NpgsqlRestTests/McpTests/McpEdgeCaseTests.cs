using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    // Edge-case tools: NULL/empty/void results, non-text scalar result types, and NULL/JSON parameters.
    public static void McpEdgeCaseTools()
    {
        script.Append(@"
create schema if not exists mcp;

create function mcp.edge_null_scalar() returns int language sql as 'select null::int';
comment on function mcp.edge_null_scalar() is '
HTTP GET
@mcp A null scalar.';

create function mcp.edge_empty_set() returns setof int language sql as 'select 1 where false';
comment on function mcp.edge_empty_set() is '
HTTP GET
@mcp An empty set.';

create function mcp.edge_void() returns void language plpgsql as 'begin end';
comment on function mcp.edge_void() is '
HTTP POST
@mcp A void routine.';

create function mcp.edge_bool() returns bool language sql as 'select true';
comment on function mcp.edge_bool() is '
HTTP GET
@mcp A boolean.';

create function mcp.edge_numeric() returns numeric language sql as 'select 3.14::numeric';
comment on function mcp.edge_numeric() is '
HTTP GET
@mcp A numeric.';

create function mcp.edge_json() returns json language sql as 'select ''{""a"":1,""b"":[2,3]}''::json';
comment on function mcp.edge_json() is '
HTTP GET
@mcp A json scalar.';

create function mcp.edge_nullarg(x int) returns text language sql as 'select coalesce(x::text, ''WAS_NULL'')';
comment on function mcp.edge_nullarg(int) is '
HTTP POST
@mcp Reports whether its argument was null.';

create function mcp.edge_jsonparam(data json) returns json language sql as 'select data';
comment on function mcp.edge_jsonparam(json) is '
HTTP POST
@mcp Echoes a json argument.';
");
    }
}

/// <summary>
/// MCP wire edge cases: NULL/empty/void results, non-text scalar result types, NULL/JSON arguments, and
/// malformed/unusual JSON-RPC inputs (batch array, missing name, non-integer id).
/// </summary>
[Collection("McpPluginFixture")]
public class McpEdgeCaseTests(McpPluginTestFixture test)
{
    private async Task<(HttpStatusCode Status, string Body)> PostAsync(string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/mcp", content);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<string> CallAsync(string tool, string arguments = "{}")
    {
        var json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\""
            + tool + "\",\"arguments\":" + arguments + "}}";
        var (status, body) = await PostAsync(json);
        status.Should().Be(HttpStatusCode.OK);
        return body;
    }

    // ---- result shapes ----------------------------------------------------

    [Fact]
    public async Task Null_scalar_result_has_empty_text_and_no_structured_content()
        => (await CallAsync("edge_null_scalar")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":""}],"isError":false}}""");

    [Fact]
    public async Task Empty_set_result_is_an_empty_items_array()
        => (await CallAsync("edge_empty_set")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"items\":[]}"}],"isError":false,"structuredContent":{"items":[]}}}""");

    [Fact]
    public async Task Void_result_has_empty_text_and_no_structured_content()
        => (await CallAsync("edge_void")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":""}],"isError":false}}""");

    [Fact]
    public async Task Boolean_result_is_a_json_boolean()
        => (await CallAsync("edge_bool")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"value\":true}"}],"isError":false,"structuredContent":{"value":true}}}""");

    [Fact]
    public async Task Numeric_result_is_a_json_number()
        => (await CallAsync("edge_numeric")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"value\":3.14}"}],"isError":false,"structuredContent":{"value":3.14}}}""");

    [Fact]
    public async Task Json_scalar_result_is_embedded_as_a_parsed_object()
        => (await CallAsync("edge_json")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"value\":{\"a\":1,\"b\":[2,3]}}"}],"isError":false,"structuredContent":{"value":{"a":1,"b":[2,3]}}}}""");

    // ---- argument shapes --------------------------------------------------

    [Fact]
    public async Task Null_argument_binds_as_sql_null()
        => (await CallAsync("edge_nullarg", """{"x":null}""")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"value\":\"WAS_NULL\"}"}],"isError":false,"structuredContent":{"value":"WAS_NULL"}}}""");

    [Fact]
    public async Task Json_argument_round_trips()
        => (await CallAsync("edge_jsonparam", """{"data":{"k":[1,2]}}""")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"value\":{\"k\":[1,2]}}"}],"isError":false,"structuredContent":{"value":{"k":[1,2]}}}}""");

    // ---- malformed / unusual JSON-RPC -------------------------------------

    [Fact]
    public async Task A_batch_array_request_is_rejected_as_invalid_not_a_crash()
    {
        // MCP 2025-11-25 removed JSON-RPC batching; an array body is invalid → -32600, not an HTTP 500.
        var (status, body) = await PostAsync("""[{"jsonrpc":"2.0","id":1,"method":"ping"}]""");
        status.Should().Be(HttpStatusCode.OK);
        body.Should().Be("""{"jsonrpc":"2.0","id":null,"error":{"code":-32600,"message":"Invalid Request"}}""");
    }

    [Fact]
    public async Task Tools_call_with_no_name_is_an_unknown_tool_error()
    {
        var (_, body) = await PostAsync("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"arguments":{}}}""");
        body.Should().Be("""{"jsonrpc":"2.0","id":1,"error":{"code":-32602,"message":"Unknown tool: "}}""");
    }

    [Fact]
    public async Task A_string_id_is_echoed_back_as_a_string()
    {
        var (_, body) = await PostAsync("""{"jsonrpc":"2.0","id":"abc-1","method":"ping"}""");
        body.Should().Be("""{"jsonrpc":"2.0","id":"abc-1","result":{}}""");
    }
}
