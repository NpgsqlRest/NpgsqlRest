namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientAddNumbersTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.add_numbers(_a int, _b int)
returns int
language sql
as $$
select _a + _b;
$$;
comment on function tsclient_test.add_numbers(int, int) is '
tsclient_module=add_numbers
';

create function tsclient_test.add_numbers_status(_a int, _b int)
returns int
language sql
as $$
select _a + _b;
$$;
comment on function tsclient_test.add_numbers_status(int, int) is '
tsclient_module=add_numbers_status
tsclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class AddNumbersTests
    {
        private const string Expected = """
const baseUrl = "";

interface ITsclientTestAddNumbersRequest {
    a: number | null;
    b: number | null;
}


/**
* function tsclient_test.add_numbers(
*     _a integer,
*     _b integer
* )
* returns integer
* 
* @remarks
* comment on function tsclient_test.add_numbers is 'tsclient_module=add_numbers
* 
* @param request - Object containing request parameters.
* @returns {number}
* 
* @see FUNCTION tsclient_test.add_numbers
*/
export async function tsclientTestAddNumbers(
    request: ITsclientTestAddNumbersRequest
) : Promise<number> {
    const response = await fetch(baseUrl + "/api/tsclient-test/add-numbers", {
        method: "POST",
        body: JSON.stringify(request)
    });
    return Number(await response.text());
}


""";

        [Fact]
        public void Test_AddNumbers_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "add_numbers.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }

        private const string ExpectedStatus = """
const baseUrl = "";

interface ITsclientTestAddNumbersStatusRequest {
    a: number | null;
    b: number | null;
}


/**
* function tsclient_test.add_numbers_status(
*     _a integer,
*     _b integer
* )
* returns integer
* 
* @remarks
* comment on function tsclient_test.add_numbers_status is 'tsclient_module=add_numbers_status
* tsclient_status_code=true';
* 
* @param request - Object containing request parameters.
* @returns {status: number, response: number, error: {status: number; title: string; detail?: string | null} | undefined}
* 
* @see FUNCTION tsclient_test.add_numbers_status
*/
export async function tsclientTestAddNumbersStatus(
    request: ITsclientTestAddNumbersStatusRequest
) : Promise<{status: number, response: number, error: {status: number; title: string; detail?: string | null} | undefined}> {
    const response = await fetch(baseUrl + "/api/tsclient-test/add-numbers-status", {
        method: "POST",
        body: JSON.stringify(request)
    });
    return {
        status: response.status,
        response: response.status === 200 ? Number(await response.text()) : undefined!,
        error: response.status !== 200 ? await response.json() as {status: number; title: string; detail?: string | null} : undefined
    };
}


""";

        [Fact]
        public void Test_AddNumbersStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "add_numbers_status.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
