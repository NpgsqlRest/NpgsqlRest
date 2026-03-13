namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientProxyPassthroughResponseTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.proxy_passthrough()
returns void
language sql
as $$
select null;
$$;
comment on function tsclient_test.proxy_passthrough() is '
HTTP GET
proxy
tsclient_module=proxy_passthrough_response
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class ProxyPassthroughResponseTests
    {
        private const string Expected = "const baseUrl = \"\";\n\n\n/**\n* function tsclient_test.proxy_passthrough()\n* returns void\n* \n* @remarks\n* comment on function tsclient_test.proxy_passthrough is 'HTTP GET\n* proxy\n* tsclient_module=proxy_passthrough_response';\n* \n* @returns {Response}\n* \n* @see FUNCTION tsclient_test.proxy_passthrough\n*/\nexport async function tsclientTestProxyPassthrough() : Promise<Response> {\n    const response = await fetch(baseUrl + \"/api/tsclient-test/proxy-passthrough\", {\n        method: \"GET\",\n    });\n    return response;\n}\n\n";

        [Fact]
        public void Test_ProxyPassthroughResponse_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "proxy_passthrough_response.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }
    }
}
