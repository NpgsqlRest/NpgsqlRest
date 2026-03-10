namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientProxyOutResponseTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.proxy_out_report()
returns json
language plpgsql
as $$
begin
    return json_build_object('data', 'report');
end;
$$;
comment on function tsclient_test.proxy_out_report() is '
HTTP GET
proxy_out POST
tsclient_module=proxy_out_response
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class ProxyOutResponseTests
    {
        private const string Expected = """
const baseUrl = "";


/**
* function tsclient_test.proxy_out_report()
* returns json
* 
* @remarks
* comment on function tsclient_test.proxy_out_report is 'HTTP GET
* proxy_out POST
* tsclient_module=proxy_out_response';
* 
* @returns {Response}
* 
* @see FUNCTION tsclient_test.proxy_out_report
*/
export async function tsclientTestProxyOutReport() : Promise<Response> {
    const response = await fetch(baseUrl + "/api/tsclient-test/proxy-out-report", {
        method: "GET",
    });
    return response;
}


""";

        [Fact]
        public void Test_ProxyOutResponse_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "proxy_out_response.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }
    }
}
