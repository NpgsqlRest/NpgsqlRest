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

-- params: required (no default) + optional (default) -> inputSchema properties + required
create function mcp.tool_params(id int, label text default 'x') returns text language sql as 'select ''p''';
comment on function mcp.tool_params(int, text) is '
HTTP GET
@mcp Fetch by id.';

-- a server-resolved param must be excluded from inputSchema (agent must not supply it)
create function mcp.tool_resolved(id int, secret text) returns text language sql as 'select ''r''';
comment on function mcp.tool_resolved(int, text) is '
HTTP GET
@mcp
secret = select ''sekret''';

-- no inline text and no prose -> description falls back to the routine name
create function mcp.tool_nodesc() returns text language sql as 'select ''nd''';
comment on function mcp.tool_nodesc() is '
HTTP GET
@mcp';

-- role-restricted tool: exercises the tools/call auth translation (401 anonymous, 403 wrong role)
create function mcp.tool_authorized() returns text language sql as 'select ''secret''';
comment on function mcp.tool_authorized() is '
HTTP GET
@mcp Admin-only data.
@authorize admin';
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

[Collection("McpPluginFixture")]
public class McpToolCatalogTests(McpPluginTestFixture test)
{
    [Fact]
    public void Catalog_contains_only_mcp_tools_keyed_by_tool_name()
    {
        test.Tools.Should().ContainKey("tool_basic");
        test.Tools.Should().ContainKey("cancel_booking");        // @mcp_name override
        test.Tools.Should().NotContainKey("tool_named");          // superseded by the explicit name
        test.Tools.Should().NotContainKey("tool_http_only");      // HTTP, not MCP
        test.Tools.Should().NotContainKey("tool_modifier_only");  // not created at all
    }

    [Fact]
    public void Description_derives_from_prose_inline_text_and_falls_back_to_name()
    {
        test.Tools["tool_basic"]!["description"]!.GetValue<string>().Should().Be("Fetch basic data for the agent.");
        test.Tools["tool_inline_desc"]!["description"]!.GetValue<string>().Should().Be("Cancel a booking and release the room.");
        test.Tools["tool_nodesc"]!["description"]!.GetValue<string>().Should().Be("tool_nodesc"); // fallback
    }

    [Fact]
    public void ToolDescriptionSuffix_is_appended_to_every_tool_description()
    {
        // A fresh plugin instance with a suffix, fed the same parsed endpoints. Handle() only reads the
        // endpoint and writes to its own catalog, so the fixture's suffix-less Tools are unaffected.
        var mcp = new Mcp(new McpOptions { Enabled = true, ToolDescriptionSuffix = "(Acme demo — read-only.)" });
        mcp.Handle(test.Endpoints["tool_basic"]);  // derived-prose description
        mcp.Handle(test.Endpoints["tool_nodesc"]); // name-fallback description

        mcp.Tools["tool_basic"]!["description"]!.GetValue<string>()
            .Should().Be("Fetch basic data for the agent. (Acme demo — read-only.)");
        mcp.Tools["tool_nodesc"]!["description"]!.GetValue<string>()
            .Should().Be("tool_nodesc (Acme demo — read-only.)");
    }

    [Fact]
    public void InputSchema_lists_params_and_marks_only_non_default_as_required()
    {
        var schema = test.Tools["tool_params"]!["inputSchema"]!;
        var props = schema["properties"]!.AsObject();
        props.ContainsKey("id").Should().BeTrue();
        props.ContainsKey("label").Should().BeTrue();
        props["id"]!["type"]!.GetValue<string>().Should().Be("integer");

        var required = schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        required.Should().Equal("id"); // `label` has a DEFAULT → optional, not required
    }

    [Fact]
    public void Server_resolved_params_are_excluded_from_inputSchema()
    {
        var props = test.Tools["tool_resolved"]!["inputSchema"]!["properties"]!.AsObject();
        props.ContainsKey("id").Should().BeTrue();
        props.ContainsKey("secret").Should().BeFalse(); // resolved server-side → agent must not supply it
    }

    [Fact]
    public void Get_tools_carry_read_only_hint()
    {
        test.Tools["tool_basic"]!["annotations"]!["readOnlyHint"]!.GetValue<bool>().Should().BeTrue();
    }
}
