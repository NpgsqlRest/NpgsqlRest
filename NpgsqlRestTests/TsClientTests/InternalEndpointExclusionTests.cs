namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientInternalExclusionTests()
        {
            script.Append("""
create schema if not exists tsclient_test;

-- internal-only endpoint: has no public HTTP route, so it must NOT appear in the generated
-- TypeScript client or the .http file (a function/request line for it would 404).
create function tsclient_test.internal_widget(_x int) returns int language sql as 'select _x';
comment on function tsclient_test.internal_widget(int) is '
HTTP GET
internal
tsclient_module=internal_widget';

-- visible sibling: anchors that generation ran for this schema (its artifacts are present).
create function tsclient_test.visible_widget(_x int) returns int language sql as 'select _x';
comment on function tsclient_test.visible_widget(int) is '
HTTP GET
tsclient_module=visible_widget';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class InternalEndpointExclusionTests
    {
        [Fact]
        public void Internal_endpoint_is_excluded_from_generated_ts_client()
        {
            var visible = Path.Combine(Setup.Program.TsClientOutputPath, "visible_widget.ts");
            var internalOnly = Path.Combine(Setup.Program.TsClientOutputPath, "internal_widget.ts");

            File.Exists(visible).Should().BeTrue("the visible endpoint anchors that TS generation ran");
            File.ReadAllText(visible).Should().Contain("/api/tsclient-test/visible-widget");

            File.Exists(internalOnly).Should().BeFalse("an internal-only endpoint has no public route, so no client function is generated");
        }

        [Fact]
        public void Internal_endpoint_is_excluded_from_generated_http_file()
        {
            var http = File.ReadAllText(Path.Combine(Setup.Program.HttpFilesOutputPath, "npgsqlrest.http"));
            http.Should().Contain("/api/tsclient-test/visible-widget");
            http.Should().NotContain("/api/tsclient-test/internal-widget");
        }
    }
}
