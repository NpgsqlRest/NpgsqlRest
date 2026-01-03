namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientDoNothingTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.do_nothing()
returns void
language sql
as $$
select null;
$$;
comment on function tsclient_test.do_nothing() is '
tsclient_module=do_nothing
';

create function tsclient_test.do_nothing_status()
returns void
language sql
as $$
select null;
$$;
comment on function tsclient_test.do_nothing_status() is '
tsclient_module=do_nothing_status
tsclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class DoNothingTests
    {
        private const string Expected = """
const baseUrl = "";


/**
* function tsclient_test.do_nothing()
* returns void
* 
* @remarks
* comment on function tsclient_test.do_nothing is 'tsclient_module=do_nothing
* 
* @returns {void}
* 
* @see FUNCTION tsclient_test.do_nothing
*/
export async function tsclientTestDoNothing() : Promise<void> {
    await fetch(baseUrl + "/api/tsclient-test/do-nothing", {
        method: "POST",
    });
}


""";

        [Fact]
        public void Test_DoNothing_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "do_nothing.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }

        private const string ExpectedStatus = """
const baseUrl = "";


/**
* function tsclient_test.do_nothing_status()
* returns void
* 
* @remarks
* comment on function tsclient_test.do_nothing_status is 'tsclient_module=do_nothing_status
* tsclient_status_code=true';
* 
* @returns {status: number, error: {status: number; title: string; detail?: string | null} | undefined}
* 
* @see FUNCTION tsclient_test.do_nothing_status
*/
export async function tsclientTestDoNothingStatus() : Promise<{status: number, error: {status: number; title: string; detail?: string | null} | undefined}> {
    const response = await fetch(baseUrl + "/api/tsclient-test/do-nothing-status", {
        method: "POST",
    });
    return {
        status: response.status,
        error: !response.ok && response.headers.get("content-length") !== "0" ? await response.json() as {status: number; title: string; detail?: string | null} : undefined
    };
}


""";

        [Fact]
        public void Test_DoNothingStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "do_nothing_status.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
