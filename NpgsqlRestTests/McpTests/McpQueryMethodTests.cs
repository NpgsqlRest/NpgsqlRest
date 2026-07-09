using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    // QUERY-method tool: QUERY is a safe, idempotent HTTP method, so the MCP tool annotation
    // readOnlyHint must be true (same as GET).
    public static void McpQueryMethodTools()
    {
        script.Append(@"
create schema if not exists mcp;

create function mcp.tool_query(_q text) returns text
language sql as 'select _q';
comment on function mcp.tool_query(text) is '
HTTP QUERY
@mcp Run a read-only query.';
");
    }
}

[Collection("McpPluginFixture")]
public class McpQueryMethodTests(McpPluginTestFixture test)
{
    [Fact]
    public void Query_method_tool_is_marked_read_only()
    {
        var tool = test.Tools["tool_query"];
        tool["annotations"]!["readOnlyHint"]!.GetValue<bool>().Should().BeTrue(
            "QUERY is a safe method, same as GET");
        tool["annotations"]!.AsObject().ContainsKey("destructiveHint").Should().BeFalse();
    }
}
