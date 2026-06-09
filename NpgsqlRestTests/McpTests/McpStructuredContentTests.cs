using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    // The four PostgreSQL return shapes, each opted in as an MCP tool. The structuredContent the server
    // produces is asserted in McpStructuredContentTests. All live in the isolated `mcp` schema.
    public static void McpStructuredContentTools()
    {
        script.Append(@"
create schema if not exists mcp;

-- 1) single scalar value  -> structuredContent { ""value"": 42 }
create function mcp.sc_scalar() returns int language sql as 'select 42';
comment on function mcp.sc_scalar() is '
HTTP GET
@mcp A single number.';

-- 2) single record (set collapsed with `single`)  -> structuredContent { ""total"": 1234, ""status"": ""paid"" }
create function mcp.sc_record() returns table(total int, status text) language sql as 'select 1234, ''paid''';
comment on function mcp.sc_record() is '
HTTP GET
@mcp A single record.
single';

-- 3) set of scalar values  -> structuredContent { ""items"": [1, 2, 3] }
create function mcp.sc_values() returns setof int language sql as 'select * from (values (1),(2),(3)) t(v)';
comment on function mcp.sc_values() is '
HTTP GET
@mcp A set of numbers.';

-- 4) set of records / rows  -> structuredContent { ""items"": [ {id,name}, ... ] }
create function mcp.sc_rows() returns table(id int, name text) language sql as 'select * from (values (1,''a''),(2,''b'')) t(id,name)';
comment on function mcp.sc_rows() is '
HTTP GET
@mcp A set of rows.';

-- 5) a column that is an ARRAY OF a custom composite type — nested result serialization.
create type mcp.tag as (label text, score int);
create function mcp.sc_composite_array() returns table(id int, tags mcp.tag[])
language sql as 'select 1, array[row(''x'',10)::mcp.tag, row(''y'',20)::mcp.tag]';
comment on function mcp.sc_composite_array() is '
HTTP GET
@mcp One row whose column is an array of composite values.';
");
    }
}

/// <summary>
/// Spec (MCP 2025-11-25) requires <c>structuredContent</c> to be a JSON object. This walks the four
/// PostgreSQL return shapes and asserts the wrapping rule: a single value → <c>{ "value": … }</c>, a
/// single record → the object itself, a set → <c>{ "items": [ … ] }</c>. The text content block carries
/// the serialized structuredContent (the spec's backward-compatibility recommendation).
/// </summary>
[Collection("McpPluginFixture")]
public class McpStructuredContentTests(McpPluginTestFixture test)
{
    /// <summary>POSTs a tools/call for the given tool (no arguments) and returns the raw response body.</summary>
    private async Task<string> CallAsync(string tool)
    {
        var json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\""
            + tool + "\",\"arguments\":{}}}";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/mcp", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Fetches tools/list and returns the named tool's full definition object as a JSON string.</summary>
    private async Task<string> ToolDefAsync(string tool)
    {
        using var content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/mcp", content);
        var tools = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["result"]!["tools"]!.AsArray();
        return tools.First(t => t!["name"]!.GetValue<string>() == tool)!.ToJsonString();
    }

    // ---- structuredContent on tools/call (text block carries the same JSON, relaxed-escaped) ----------

    [Fact]
    public async Task Single_scalar_value_is_wrapped_as_value_object()
    {
        (await CallAsync("sc_scalar")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"value\":42}"}],"isError":false,"structuredContent":{"value":42}}}""");
    }

    [Fact]
    public async Task Single_record_is_the_object_itself()
    {
        (await CallAsync("sc_record")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"total\":1234,\"status\":\"paid\"}"}],"isError":false,"structuredContent":{"total":1234,"status":"paid"}}}""");
    }

    [Fact]
    public async Task Set_of_scalar_values_is_wrapped_as_items()
    {
        (await CallAsync("sc_values")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"items\":[1,2,3]}"}],"isError":false,"structuredContent":{"items":[1,2,3]}}}""");
    }

    [Fact]
    public async Task Set_of_records_is_wrapped_as_items()
    {
        (await CallAsync("sc_rows")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"items\":[{\"id\":1,\"name\":\"a\"},{\"id\":2,\"name\":\"b\"}]}"}],"isError":false,"structuredContent":{"items":[{"id":1,"name":"a"},{"id":2,"name":"b"}]}}}""");
    }

    [Fact]
    public async Task A_column_that_is_an_array_of_composite_values_is_wrapped_as_items()
    {
        // A custom composite type (mcp.tag) inside an array column serializes as nested objects; the set
        // is wrapped as { "items": [ … ] } like any other set of rows.
        (await CallAsync("sc_composite_array")).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"items\":[{\"id\":1,\"tags\":[{\"label\":\"x\",\"score\":10},{\"label\":\"y\",\"score\":20}]}]}"}],"isError":false,"structuredContent":{"items":[{"id":1,"tags":[{"label":"x","score":10},{"label":"y","score":20}]}]}}}""");
    }

    // ---- outputSchema in the tools/list tool definition (structuredContent MUST conform) --------------

    [Fact]
    public async Task Tool_definition_for_a_single_scalar_declares_a_nullable_value_output()
    {
        (await ToolDefAsync("sc_scalar")).Should().Be(
            """{"name":"sc_scalar","description":"A single number.","inputSchema":{"type":"object","properties":{}},"annotations":{"readOnlyHint":true},"outputSchema":{"type":"object","properties":{"value":{"type":["integer","null"],"format":"int32"}}}}""");
    }

    [Fact]
    public async Task Tool_definition_for_a_single_record_declares_each_nullable_column()
    {
        (await ToolDefAsync("sc_record")).Should().Be(
            """{"name":"sc_record","description":"A single record.","inputSchema":{"type":"object","properties":{}},"annotations":{"readOnlyHint":true},"outputSchema":{"type":"object","properties":{"total":{"type":["integer","null"],"format":"int32"},"status":{"type":["string","null"]}}}}""");
    }

    [Fact]
    public async Task Tool_definition_for_a_set_of_values_declares_an_items_array()
    {
        (await ToolDefAsync("sc_values")).Should().Be(
            """{"name":"sc_values","description":"A set of numbers.","inputSchema":{"type":"object","properties":{}},"annotations":{"readOnlyHint":true},"outputSchema":{"type":"object","properties":{"items":{"type":"array","items":{"type":["integer","null"],"format":"int32"}}}}}""");
    }

    [Fact]
    public async Task Tool_definition_for_a_set_of_rows_declares_an_items_array_of_objects()
    {
        (await ToolDefAsync("sc_rows")).Should().Be(
            """{"name":"sc_rows","description":"A set of rows.","inputSchema":{"type":"object","properties":{}},"annotations":{"readOnlyHint":true},"outputSchema":{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"id":{"type":["integer","null"],"format":"int32"},"name":{"type":["string","null"]}}}}}}}""");
    }
}
