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

-- @mcp + @internal, NO HTTP tag -> created (mcp requests endpoint) + InternalOnly (explicit)
create function mcp.tool_mcp_only() returns text language sql as 'select ''o''';
comment on function mcp.tool_mcp_only() is '
@mcp
@internal';

-- bare @mcp, no HTTP tag -> MCP-ONLY: created (mcp requests endpoint) and InternalOnly by default.
-- A plugin-requested endpoint with no HTTP tag must not silently open a public HTTP route.
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

-- inline `@mcp <text>` is explicit -> it is the description and SUPPRESSES the comment prose below it
create function mcp.tool_inline_and_prose() returns text language sql as 'select ''ip''';
comment on function mcp.tool_inline_and_prose() is '
HTTP GET
@mcp Lead description.
This prose is ignored because an explicit description is present.';

-- `@mcp_description` is authoritative: it wins over inline `@mcp <text>` and suppresses the prose
create function mcp.tool_explicit_desc() returns text language sql as 'select ''ed''';
comment on function mcp.tool_explicit_desc() is '
HTTP GET
@mcp inline text that must be ignored
@mcp_description The authoritative tool description.
This prose must also be ignored.';

-- composite-type parameter -> NpgsqlRest flattens it into typed scalar params (pX, pY)
create type mcp.point as (x int, y int);
create function mcp.tool_composite_param(p mcp.point) returns text language sql as 'select ''ok''';
comment on function mcp.tool_composite_param(mcp.point) is '
HTTP POST
@mcp Takes a composite point.';

-- array-of-composite parameter -> can't flatten; should render as an array of objects (not a string)
create function mcp.tool_point_array(pts mcp.point[]) returns text language sql as 'select ''ok''';
comment on function mcp.tool_point_array(mcp.point[]) is '
HTTP POST
@mcp Takes an array of points.';
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
    public void Mcp_inline_text_is_recorded_as_the_inline_description()
    {
        var info = Info(test.Endpoints["tool_inline_desc"])!;
        info.Enabled.Should().BeTrue();
        info.InlineText.Should().Be("Cancel a booking and release the room.");
        info.Description.Should().BeNull();   // no explicit @mcp_description
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
    public void Mcp_alone_without_http_tag_is_mcp_only()
    {
        // Bare @mcp, no HTTP tag: the endpoint exists only because the plugin requested it, so it
        // defaults to internal-only — opting into MCP must not silently open a public HTTP route.
        // (`HTTP GET` + @mcp = dual exposure; tool_basic covers that.)
        var e = test.Endpoints["tool_mcp_no_http"];
        Info(e)!.Enabled.Should().BeTrue();
        e.InternalOnly.Should().BeTrue();
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

    // The full tool definitions. (tool_basic's plain definition is asserted in McpServerTests' tools/list.)
    // GET tools carry readOnlyHint:true; outputSchema is derived from the return type.

    [Fact]
    public void Description_derives_from_inline_mcp_text()
    {
        test.Tools["tool_inline_desc"]!.ToJsonString().Should().Be(
            """{"name":"tool_inline_desc","description":"Cancel a booking and release the room.","inputSchema":{"type":"object","properties":{}},"annotations":{"readOnlyHint":true},"outputSchema":{"type":"object","properties":{"value":{"type":["string","null"]}}}}""");
    }

    [Fact]
    public void Description_falls_back_to_the_routine_name_when_there_is_no_text()
    {
        test.Tools["tool_nodesc"]!.ToJsonString().Should().Be(
            """{"name":"tool_nodesc","description":"tool_nodesc","inputSchema":{"type":"object","properties":{}},"annotations":{"readOnlyHint":true},"outputSchema":{"type":"object","properties":{"value":{"type":["string","null"]}}}}""");
    }

    [Fact]
    public void Composite_type_parameter_is_flattened_into_typed_scalar_fields()
        => test.Tools["tool_composite_param"]!["inputSchema"]!.ToJsonString().Should().Be(
            """{"type":"object","properties":{"pX":{"type":"integer","format":"int32"},"pY":{"type":"integer","format":"int32"}},"required":["pX","pY"]}""");

    [Fact]
    public void Array_of_composite_parameter_renders_as_an_array_of_strings()
        // Known limitation: parameter TypeDescriptors don't carry composite-element field metadata, so an
        // array-of-composite argument is described as an array of strings (the value still binds). Scalar
        // composite params, by contrast, are flattened into typed fields (see the test above).
        => test.Tools["tool_point_array"]!["inputSchema"]!.ToJsonString().Should().Be(
            """{"type":"object","properties":{"pts":{"type":"array","items":{"type":"string"}}},"required":["pts"]}""");

    [Fact]
    public void Inline_mcp_text_is_the_description_and_suppresses_comment_prose()
        => test.Tools["tool_inline_and_prose"]!.ToJsonString().Should().Be(
            """{"name":"tool_inline_and_prose","description":"Lead description.","inputSchema":{"type":"object","properties":{}},"annotations":{"readOnlyHint":true},"outputSchema":{"type":"object","properties":{"value":{"type":["string","null"]}}}}""");

    [Fact]
    public void Mcp_description_is_authoritative_over_inline_text_and_prose()
        => test.Tools["tool_explicit_desc"]!.ToJsonString().Should().Be(
            """{"name":"tool_explicit_desc","description":"The authoritative tool description.","inputSchema":{"type":"object","properties":{}},"annotations":{"readOnlyHint":true},"outputSchema":{"type":"object","properties":{"value":{"type":["string","null"]}}}}""");

    [Fact]
    public void ToolDescriptionSuffix_is_appended_to_every_tool_description()
    {
        // A fresh plugin instance with a suffix, fed the same parsed endpoint. Handle() only reads the
        // endpoint and writes to its own catalog, so the fixture's suffix-less Tools are unaffected.
        var mcp = new Mcp(new McpOptions { Enabled = true, ToolDescriptionSuffix = "(Acme demo, read-only.)" });
        mcp.Handle(test.Endpoints["tool_basic"]);
        mcp.Tools["tool_basic"]!.ToJsonString().Should().Be(
            """{"name":"tool_basic","description":"Fetch basic data for the agent. (Acme demo, read-only.)","inputSchema":{"type":"object","properties":{}},"annotations":{"readOnlyHint":true},"outputSchema":{"type":"object","properties":{"value":{"type":["string","null"]}}}}""");
    }

    [Fact]
    public void InputSchema_lists_params_and_marks_only_non_default_as_required()
    {
        // tool_params(id int, label text default 'x'): both params listed; only `id` (no DEFAULT) required.
        test.Tools["tool_params"]!.ToJsonString().Should().Be(
            """{"name":"tool_params","description":"Fetch by id.","inputSchema":{"type":"object","properties":{"id":{"type":"integer","format":"int32"},"label":{"type":"string"}},"required":["id"]},"annotations":{"readOnlyHint":true},"outputSchema":{"type":"object","properties":{"value":{"type":["string","null"]}}}}""");
    }

    [Fact]
    public void Server_resolved_params_are_excluded_from_inputSchema()
    {
        // tool_resolved(id int, secret text): `secret` is resolved server-side, so it is absent from inputSchema.
        test.Tools["tool_resolved"]!.ToJsonString().Should().Be(
            """{"name":"tool_resolved","description":"tool_resolved","inputSchema":{"type":"object","properties":{"id":{"type":"integer","format":"int32"}},"required":["id"]},"annotations":{"readOnlyHint":true},"outputSchema":{"type":"object","properties":{"value":{"type":["string","null"]}}}}""");
    }
}
