namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientIsActiveTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.is_active()
returns boolean
language sql
as $$
select true;
$$;
comment on function tsclient_test.is_active() is '
tsclient_module=is_active
';

create function tsclient_test.is_active_status()
returns boolean
language sql
as $$
select true;
$$;
comment on function tsclient_test.is_active_status() is '
tsclient_module=is_active_status
tsclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class IsActiveTests
    {
        private const string Expected = """
const baseUrl = "";


/**
* function tsclient_test.is_active()
* returns boolean
* 
* @remarks
* comment on function tsclient_test.is_active is 'tsclient_module=is_active
* 
* @returns {boolean}
* 
* @see FUNCTION tsclient_test.is_active
*/
export async function tsclientTestIsActive() : Promise<boolean> {
    const response = await fetch(baseUrl + "/api/tsclient-test/is-active", {
        method: "POST",
    });
    return await response.text() == "t";
}


""";

        [Fact]
        public void Test_IsActive_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "is_active.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }

        private const string ExpectedStatus = """
const baseUrl = "";


/**
* function tsclient_test.is_active_status()
* returns boolean
* 
* @remarks
* comment on function tsclient_test.is_active_status is 'tsclient_module=is_active_status
* tsclient_status_code=true';
* 
* @returns {status: number, response: boolean, error: {status: number; title: string; detail?: string | null} | undefined}
* 
* @see FUNCTION tsclient_test.is_active_status
*/
export async function tsclientTestIsActiveStatus() : Promise<{status: number, response: boolean, error: {status: number; title: string; detail?: string | null} | undefined}> {
    const response = await fetch(baseUrl + "/api/tsclient-test/is-active-status", {
        method: "POST",
    });
    return {
        status: response.status,
        response: response.ok ? await response.text() == "t" : undefined!,
        error: !response.ok && response.headers.get("content-length") !== "0" ? await response.json() as {status: number; title: string; detail?: string | null} : undefined
    };
}


""";

        [Fact]
        public void Test_IsActiveStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "is_active_status.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
