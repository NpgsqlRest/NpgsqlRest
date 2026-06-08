using NpgsqlRest.Mcp;
using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    // MCP plugin annotation test functions, in an isolated `mcp` schema (the shared
    // Program/IncludeSchemas list excludes `mcp`, so these never appear in any other fixture).
    // The fixture runs in CommentsMode.OnlyAnnotated with the Mcp + OpenApi handlers loaded.
    public static void McpPluginAnnotationTests()
    {
        script.Append(@"
create schema if not exists mcp;

-- @mcp opt-in (no inline text) -> description derived from prose; keeps HTTP route
create function mcp.tool_basic() returns text language sql as 'select ''basic''';
comment on function mcp.tool_basic() is '
HTTP GET
Fetch basic data for the agent.
@mcp';

-- @mcp <text> -> inline description override
create function mcp.tool_inline_desc() returns text language sql as 'select ''d''';
comment on function mcp.tool_inline_desc() is '
HTTP GET
@mcp Cancel a booking and release the room.';

-- @mcp_name -> explicit tool name
create function mcp.tool_named() returns text language sql as 'select ''n''';
comment on function mcp.tool_named() is '
HTTP GET
@mcp
@mcp_name cancel_booking';

-- MCP-only: @mcp + @internal, NO HTTP tag -> created (mcp requests endpoint) + InternalOnly
create function mcp.tool_mcp_only() returns text language sql as 'select ''o''';
comment on function mcp.tool_mcp_only() is '
@mcp
@internal';

-- @mcp alone, no HTTP tag, no @internal -> created (mcp requests endpoint), HTTP route kept
create function mcp.tool_mcp_no_http() returns text language sql as 'select ''nh''';
comment on function mcp.tool_mcp_no_http() is '
@mcp';

-- HTTP only (no @mcp) -> created via HTTP tag, no MCP metadata
create function mcp.tool_http_only() returns text language sql as 'select ''h''';
comment on function mcp.tool_http_only() is '
HTTP GET
A normal endpoint, not exposed to MCP.';

-- modifier only (authorize), no HTTP, no exposure -> NOT created under OnlyAnnotated
create function mcp.tool_modifier_only() returns text language sql as 'select ''m''';
comment on function mcp.tool_modifier_only() is '
authorize';

-- plugin modifier only (openapi hide), no HTTP, no exposure -> NOT created under OnlyAnnotated
create function mcp.tool_openapi_hide_only() returns text language sql as 'select ''oh''';
comment on function mcp.tool_openapi_hide_only() is '
openapi hide';
");
    }
}

[Collection("McpPluginFixture")]
public class McpPluginAnnotationTests(McpPluginTestFixture test)
{
    private static McpToolInfo? Info(RoutineEndpoint e)
        => e.TryGetItem(Mcp.ItemsKey, out var v) ? v as McpToolInfo : null;

    [Fact]
    public void Mcp_opt_in_records_metadata_keeps_http_and_leaves_prose()
    {
        var e = test.Endpoints["tool_basic"];
        var info = Info(e);
        info.Should().NotBeNull();
        info!.Enabled.Should().BeTrue();
        info.ToolName.Should().BeNull();
        info.Description.Should().BeNull();          // no inline text -> derived from prose later
        e.InternalOnly.Should().BeFalse();           // HTTP route kept
        e.UnhandledCommentLines.Should().Equal("Fetch basic data for the agent.");
    }

    [Fact]
    public void Mcp_inline_text_is_the_description()
    {
        var info = Info(test.Endpoints["tool_inline_desc"])!;
        info.Enabled.Should().BeTrue();
        info.Description.Should().Be("Cancel a booking and release the room.");
    }

    [Fact]
    public void Mcp_name_sets_tool_name()
    {
        var info = Info(test.Endpoints["tool_named"])!;
        info.Enabled.Should().BeTrue();
        info.ToolName.Should().Be("cancel_booking");
    }

    [Fact]
    public void Mcp_plus_internal_is_mcp_only_no_http_route()
    {
        var e = test.Endpoints["tool_mcp_only"];   // created despite no HTTP tag (OnlyAnnotated)
        Info(e)!.Enabled.Should().BeTrue();
        e.InternalOnly.Should().BeTrue();           // MCP-only: no public HTTP route
    }

    [Fact]
    public void Mcp_alone_creates_endpoint_and_keeps_http()
    {
        var e = test.Endpoints["tool_mcp_no_http"]; // created despite no HTTP tag (mcp requests it)
        Info(e)!.Enabled.Should().BeTrue();
        e.InternalOnly.Should().BeFalse();          // HTTP+MCP (no @internal)
    }

    [Fact]
    public void Http_only_endpoint_has_no_mcp_metadata()
    {
        var e = test.Endpoints["tool_http_only"];
        Info(e).Should().BeNull();
        e.InternalOnly.Should().BeFalse();
    }

    [Fact]
    public void Modifier_only_comments_do_not_create_an_endpoint()
    {
        // OnlyAnnotated: a comment with only a modifier (no HTTP tag, no exposure request) creates nothing.
        test.Endpoints.ContainsKey("tool_modifier_only").Should().BeFalse();   // `authorize` (core modifier)
        test.Endpoints.ContainsKey("tool_openapi_hide_only").Should().BeFalse(); // `openapi hide` (plugin modifier)
    }
}
