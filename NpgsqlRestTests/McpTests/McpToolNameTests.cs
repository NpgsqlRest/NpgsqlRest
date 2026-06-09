using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    // Two routines forced to the same tool name (via @mcp_name) in the isolated `mcp_names` schema.
    public static void McpToolNameTools()
    {
        script.Append(@"
create schema if not exists mcp_names;

create function mcp_names.dup_a() returns text language sql as 'select ''a''';
comment on function mcp_names.dup_a() is '
HTTP GET
@mcp First routine.
@mcp_name dup_tool';

create function mcp_names.dup_b() returns text language sql as 'select ''b''';
comment on function mcp_names.dup_b() is '
HTTP GET
@mcp Second routine.
@mcp_name dup_tool';
");
    }
}

[Collection("McpToolNameFixture")]
public class McpToolNameTests(McpToolNameTestFixture test)
{
    [Fact]
    public void Colliding_tool_names_keep_one_and_warn_about_the_rest()
    {
        // Both dup_a and dup_b map to `dup_tool`; the catalog keeps a single entry...
        test.Tools.Should().ContainKey("dup_tool");
        test.Tools.Keys.Count(k => k == "dup_tool").Should().Be(1);

        // ...and the collision is logged as a warning naming the skipped routine.
        var warning = test.StartupLogs.FirstOrDefault(l =>
            l.Message.Contains("already in use") && l.Message.Contains("dup_tool"));
        warning.Should().NotBeNull("a colliding tool name should be logged");
    }
}
