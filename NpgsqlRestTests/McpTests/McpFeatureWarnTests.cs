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
}
