using Microsoft.Extensions.Logging;
using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    // Isolated schema for the non-applicable-feature warning test. `mcp_warn` is excluded from every
    // other fixture (they include only "mcp"/"public"), so this routine never pollutes other catalogs.
    public static void McpFeatureWarnTests()
    {
        script.Append(@"
create schema if not exists mcp_warn;

-- @mcp on a login endpoint: login is an auth flow that does not translate to an MCP tool call.
-- (login routines must return a named result set, not a scalar/void.)
create function mcp_warn.tool_login() returns table(status int, name_identifier text)
language sql as 'select 200, ''42''';
comment on function mcp_warn.tool_login() is '
HTTP POST
login
@mcp Sign in.';
");
    }
}

[Collection("McpFeatureWarnFixture")]
public class McpFeatureWarnTests(McpFeatureWarnTestFixture test)
{
    [Fact]
    public void Mcp_annotation_on_a_non_applicable_feature_logs_a_warning()
    {
        var warning = test.StartupLogs.FirstOrDefault(l =>
            l.Message.Contains("does not apply to MCP tools") && l.Message.Contains("login"));
        warning.Should().NotBeNull("the plugin should warn when `@mcp` is placed on a login routine");
        warning!.Message.Should().Contain("mcp_warn.tool_login");
    }

    [Fact]
    public void Mcp_annotation_appears_in_the_endpoint_annotations_summary()
    {
        // The per-endpoint "annotations: [...]" Debug summary should list the mcp annotation alongside the
        // built-in ones, so it is as visible in the console as HTTP/authorize/etc.
        var summary = test.StartupLogs.FirstOrDefault(l => l.Message.Contains("annotations: ["));
        summary.Should().NotBeNull("each created endpoint logs an annotations summary");
        summary!.Message.Should().Contain("Sign in."); // tool_login's `@mcp Sign in.` annotation
    }

    [Fact]
    public void The_exposed_tool_catalog_is_logged_at_startup()
    {
        // An Information-level summary lists the exposed tools so operators see the catalog without Debug.
        // This fixture exposes exactly one tool (tool_login).
        var catalog = test.StartupLogs.FirstOrDefault(l => l.Message.Contains("tool(s) exposed"));
        catalog.Should().NotBeNull("the plugin should log a one-line MCP tool-catalog summary at startup");
        catalog!.Level.Should().Be(LogLevel.Information);
        catalog.Message.Should().Contain("tool_login");
    }
}
