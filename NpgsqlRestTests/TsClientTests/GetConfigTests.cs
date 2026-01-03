namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientGetConfigTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.get_config()
returns json
language sql
as $$
select '{"key": "value", "count": 42}'::json;
$$;
comment on function tsclient_test.get_config() is '
tsclient_module=get_config
';

create function tsclient_test.get_config_status()
returns json
language sql
as $$
select '{"key": "value", "count": 42}'::json;
$$;
comment on function tsclient_test.get_config_status() is '
tsclient_module=get_config_status
tsclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class GetConfigTests
    {
        private const string Expected = """
const baseUrl = "";


/**
* function tsclient_test.get_config()
* returns json
* 
* @remarks
* comment on function tsclient_test.get_config is 'tsclient_module=get_config
* 
* @returns {any}
* 
* @see FUNCTION tsclient_test.get_config
*/
export async function tsclientTestGetConfig() : Promise<any> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-config", {
        method: "GET",
    });
    return await response.json();
}


""";

        [Fact]
        public void Test_GetConfig_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "get_config.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }

        private const string ExpectedStatus = """
const baseUrl = "";


/**
* function tsclient_test.get_config_status()
* returns json
* 
* @remarks
* comment on function tsclient_test.get_config_status is 'tsclient_module=get_config_status
* tsclient_status_code=true';
* 
* @returns {status: number, response: any, error: {status: number; title: string; detail?: string | null} | undefined}
* 
* @see FUNCTION tsclient_test.get_config_status
*/
export async function tsclientTestGetConfigStatus() : Promise<{status: number, response: any, error: {status: number; title: string; detail?: string | null} | undefined}> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-config-status", {
        method: "GET",
    });
    return {
        status: response.status,
        response: response.ok ? await response.json() : undefined!,
        error: !response.ok && response.headers.get("content-length") !== "0" ? await response.json() as {status: number; title: string; detail?: string | null} : undefined
    };
}


""";

        [Fact]
        public void Test_GetConfigStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "get_config_status.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
