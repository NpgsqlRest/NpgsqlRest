namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientSumArrayTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.sum_array(_numbers int[])
returns int
language sql
as $$
select coalesce(sum(n), 0)::int from unnest(_numbers) as n;
$$;
comment on function tsclient_test.sum_array(int[]) is '
tsclient_module=sum_array
';

create function tsclient_test.sum_array_status(_numbers int[])
returns int
language sql
as $$
select coalesce(sum(n), 0)::int from unnest(_numbers) as n;
$$;
comment on function tsclient_test.sum_array_status(int[]) is '
tsclient_module=sum_array_status
tsclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class SumArrayTests(TestFixture test)
    {
        private const string Expected = """
const baseUrl = "";

interface ITsclientTestSumArrayRequest {
    numbers: number[] | null;
}


/**
* function tsclient_test.sum_array(
*     _numbers integer[]
* )
* returns integer
* 
* @remarks
* comment on function tsclient_test.sum_array is 'tsclient_module=sum_array
* 
* @param request - Object containing request parameters.
* @returns {number}
* 
* @see FUNCTION tsclient_test.sum_array
*/
export async function tsclientTestSumArray(
    request: ITsclientTestSumArrayRequest
) : Promise<number> {
    const response = await fetch(baseUrl + "/api/tsclient-test/sum-array", {
        method: "POST",
        body: JSON.stringify(request)
    });
    return Number(await response.text());
}


""";

        [Fact]
        public void Test_SumArray_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "sum_array.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }

        private const string ExpectedStatus = """
const baseUrl = "";

interface ITsclientTestSumArrayStatusRequest {
    numbers: number[] | null;
}


/**
* function tsclient_test.sum_array_status(
*     _numbers integer[]
* )
* returns integer
* 
* @remarks
* comment on function tsclient_test.sum_array_status is 'tsclient_module=sum_array_status
* tsclient_status_code=true';
* 
* @param request - Object containing request parameters.
* @returns {status: number, response: number, error: {status: number; title: string; detail?: string | null} | undefined}
* 
* @see FUNCTION tsclient_test.sum_array_status
*/
export async function tsclientTestSumArrayStatus(
    request: ITsclientTestSumArrayStatusRequest
) : Promise<{status: number, response: number, error: {status: number; title: string; detail?: string | null} | undefined}> {
    const response = await fetch(baseUrl + "/api/tsclient-test/sum-array-status", {
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
        public void Test_SumArrayStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "sum_array_status.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
