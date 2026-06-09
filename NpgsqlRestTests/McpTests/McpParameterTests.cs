using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    // Tools that actually USE and RETURN their parameters, so tools/call argument mapping is observable:
    // multiple typed args, a path parameter, an optional (DEFAULT) parameter, and a POST JSON body.
    public static void McpParameterTools()
    {
        script.Append(@"
create schema if not exists mcp;

-- multiple typed (int) params via GET query string; computes a result so the bound values are visible.
create function mcp.param_sum(a int, b int) returns int language sql as 'select a + b';
comment on function mcp.param_sum(int, int) is '
HTTP GET
@mcp Add two numbers.';

-- optional parameter with a DEFAULT: omitted → default applies; supplied → overrides.
create function mcp.param_opts(req text, opt text default 'D') returns text language sql as 'select req || ''/'' || opt';
comment on function mcp.param_opts(text, text) is '
HTTP GET
@mcp Join a required and an optional value.';

-- path parameter: `id` binds from the URL path segment, not the query string.
create function mcp.param_path(id int) returns int language sql as 'select id';
comment on function mcp.param_path(int) is '
HTTP GET /api/mcp-item/{id}
@mcp Fetch an item by path id.';

-- POST tool with two params → arguments are mapped onto a JSON request body.
create function mcp.param_body(first text, second text) returns text language sql as 'select first || ''-'' || second';
comment on function mcp.param_body(text, text) is '
HTTP POST
@mcp Concatenate two values via a JSON body.';

-- array parameter AND array result: the int[] argument maps into the JSON body and round-trips back.
create function mcp.param_array(vals int[]) returns int[] language sql as 'select vals';
comment on function mcp.param_array(int[]) is '
HTTP POST
@mcp Echo an int array.';
");
    }
}

/// <summary>
/// tools/call argument mapping: arguments flow to the routine as a query string (GET/DELETE), a JSON
/// body (POST/PUT), or path-segment substitution — and typed/optional parameters bind correctly. Each
/// tool returns a value derived from its arguments, so the round-trip is asserted in the full body.
/// </summary>
[Collection("McpPluginFixture")]
public class McpParameterTests(McpPluginTestFixture test)
{
    private async Task<string> CallAsync(string requestJson)
    {
        using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/mcp", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Multiple_typed_int_arguments_bind_and_compute_via_query_string()
    {
        (await CallAsync("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"param_sum","arguments":{"a":2,"b":3}}}""")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"value\":5}"}],"isError":false,"structuredContent":{"value":5}}}""");
    }

    [Fact]
    public async Task An_omitted_optional_argument_uses_the_postgres_default()
    {
        (await CallAsync("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"param_opts","arguments":{"req":"a"}}}""")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"value\":\"a/D\"}"}],"isError":false,"structuredContent":{"value":"a/D"}}}""");
    }

    [Fact]
    public async Task A_supplied_optional_argument_overrides_the_default()
    {
        (await CallAsync("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"param_opts","arguments":{"req":"a","opt":"b"}}}""")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"value\":\"a/b\"}"}],"isError":false,"structuredContent":{"value":"a/b"}}}""");
    }

    [Fact]
    public async Task A_path_parameter_argument_is_substituted_into_the_url()
    {
        (await CallAsync("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"param_path","arguments":{"id":7}}}""")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"value\":7}"}],"isError":false,"structuredContent":{"value":7}}}""");
    }

    [Fact]
    public async Task Multiple_arguments_map_to_a_json_body_for_a_post_tool()
    {
        (await CallAsync("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"param_body","arguments":{"first":"x","second":"y"}}}""")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"value\":\"x-y\"}"}],"isError":false,"structuredContent":{"value":"x-y"}}}""");
    }

    [Fact]
    public async Task Argument_values_with_special_characters_round_trip_through_the_query_string()
    {
        // demo_echo returns its argument verbatim; '&', '=' and spaces must survive URL-encoding.
        (await CallAsync("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"demo_echo","arguments":{"message":"a & b = c"}}}""")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"value\":\"a & b = c\"}"}],"isError":false,"structuredContent":{"value":"a & b = c"}}}""");
    }

    [Fact]
    public async Task Unicode_and_emoji_arguments_round_trip()
    {
        // Non-ASCII survives the round-trip (query-string percent-encoding + relaxed encoder). BMP chars
        // (é, Cyrillic) are emitted as literal UTF-8; astral-plane chars (the 🚀 emoji) as a \uXXXX\uXXXX
        // surrogate-pair escape — both faithfully decode back to the original.
        (await CallAsync("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"demo_echo","arguments":{"message":"héllo 🚀 мир"}}}""")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"value\":\"héllo \\uD83D\\uDE80 мир\"}"}],"isError":false,"structuredContent":{"value":"héllo \uD83D\uDE80 мир"}}}""");
    }

    [Fact]
    public async Task An_array_argument_round_trips_through_the_json_body()
    {
        (await CallAsync("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"param_array","arguments":{"vals":[1,2,3]}}}""")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"value\":[1,2,3]}"}],"isError":false,"structuredContent":{"value":[1,2,3]}}}""");
    }

    [Fact]
    public async Task Concurrent_tools_calls_do_not_cross_talk()
    {
        // The catalog is read-only after startup and invocation is stateless — fire many parallel calls
        // with distinct arguments and confirm each response carries its own value (no shared-state bleed).
        async Task<(int I, string Body)> Call(int i)
        {
            var req = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"demo_echo\","
                + "\"arguments\":{\"message\":\"c" + i + "\"}}}";
            return (i, await CallAsync(req));
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(Call));

        foreach (var (i, body) in results)
        {
            body.Should().Be(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"value\\\":\\\"c"
                + i + "\\\"}\"}],\"isError\":false,\"structuredContent\":{\"value\":\"c" + i + "\"}}}");
        }
    }

    [Fact]
    public void An_array_parameter_is_declared_as_an_array_in_inputSchema()
    {
        // Arrays render precisely in inputSchema; the array-typed result uses a permissive outputSchema value.
        test.Tools["param_array"]!.ToJsonString().Should().Be(
            """{"name":"param_array","description":"Echo an int array.","inputSchema":{"type":"object","properties":{"vals":{"type":"array","items":{"type":"integer","format":"int32"}}},"required":["vals"]},"annotations":{"readOnlyHint":false},"outputSchema":{"type":"object","properties":{"value":{}}}}""");
    }
}
