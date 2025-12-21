namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientGetNamesTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.get_names()
returns setof text
language sql
as $$
select unnest(array['Alice', 'Bob', 'Charlie']);
$$;
comment on function tsclient_test.get_names() is '
tsclient_module=get_names
';

create function tsclient_test.get_names_status()
returns setof text
language sql
as $$
select unnest(array['Alice', 'Bob', 'Charlie']);
$$;
comment on function tsclient_test.get_names_status() is '
tsclient_module=get_names_status
tsclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class GetNamesTests(TestFixture test)
    {
        private const string Expected = """
const baseUrl = "";


/**
* function tsclient_test.get_names()
* returns setof text
* 
* @remarks
* comment on function tsclient_test.get_names is 'tsclient_module=get_names
* 
* @returns {string[]}
* 
* @see FUNCTION tsclient_test.get_names
*/
export async function tsclientTestGetNames() : Promise<string[]> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-names", {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json() as string[];
}


""";

        [Fact]
        public void Test_GetNames_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "get_names.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }

        private const string ExpectedStatus = """
const baseUrl = "";


/**
* function tsclient_test.get_names_status()
* returns setof text
* 
* @remarks
* comment on function tsclient_test.get_names_status is 'tsclient_module=get_names_status
* tsclient_status_code=true';
* 
* @returns {status: number, response: string[], error: {status: number; title: string; detail?: string | null} | undefined}
* 
* @see FUNCTION tsclient_test.get_names_status
*/
export async function tsclientTestGetNamesStatus() : Promise<{status: number, response: string[], error: {status: number; title: string; detail?: string | null} | undefined}> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-names-status", {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return {
        status: response.status,
        response: response.status === 200 ? await response.json() as string[] : undefined!,
        error: response.status !== 200 ? await response.json() as {status: number; title: string; detail?: string | null} : undefined
    };
}


""";

        [Fact]
        public void Test_GetNamesStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "get_names_status.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
