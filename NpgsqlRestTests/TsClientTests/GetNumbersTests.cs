namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientGetNumbersTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.get_numbers()
returns int[]
language sql
as $$
select array[1, 2, 3, 4, 5];
$$;
comment on function tsclient_test.get_numbers() is '
tsclient_module=get_numbers
';

create function tsclient_test.get_numbers_status()
returns int[]
language sql
as $$
select array[1, 2, 3, 4, 5];
$$;
comment on function tsclient_test.get_numbers_status() is '
tsclient_module=get_numbers_status
tsclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class GetNumbersTests(TestFixture test)
    {
        private const string Expected = """
const baseUrl = "";


/**
* function tsclient_test.get_numbers()
* returns integer[]
* 
* @remarks
* comment on function tsclient_test.get_numbers is 'tsclient_module=get_numbers
* 
* @returns {number[]}
* 
* @see FUNCTION tsclient_test.get_numbers
*/
export async function tsclientTestGetNumbers() : Promise<number[]> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-numbers", {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json();
}


""";

        [Fact]
        public void Test_GetNumbers_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "get_numbers.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }

        private const string ExpectedStatus = """
const baseUrl = "";


/**
* function tsclient_test.get_numbers_status()
* returns integer[]
* 
* @remarks
* comment on function tsclient_test.get_numbers_status is 'tsclient_module=get_numbers_status
* tsclient_status_code=true';
* 
* @returns {status: number, response: number[], error: {status: number; title: string; detail?: string | null} | undefined}
* 
* @see FUNCTION tsclient_test.get_numbers_status
*/
export async function tsclientTestGetNumbersStatus() : Promise<{status: number, response: number[], error: {status: number; title: string; detail?: string | null} | undefined}> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-numbers-status", {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return {
        status: response.status,
        response: response.status === 200 ? await response.json() : undefined!,
        error: response.status !== 200 ? await response.json() as {status: number; title: string; detail?: string | null} : undefined
    };
}


""";

        [Fact]
        public void Test_GetNumbersStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "get_numbers_status.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
