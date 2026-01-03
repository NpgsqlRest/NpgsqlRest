namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientGetAnswerTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.get_answer()
returns int
language sql
as $$
select 42;
$$;
comment on function tsclient_test.get_answer() is '
tsclient_module=get_answer
';

create function tsclient_test.get_answer_status()
returns int
language sql
as $$
select 42;
$$;
comment on function tsclient_test.get_answer_status() is '
tsclient_module=get_answer_status
tsclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class GetAnswerTests
    {
        private const string Expected = """
const baseUrl = "";


/**
* function tsclient_test.get_answer()
* returns integer
* 
* @remarks
* comment on function tsclient_test.get_answer is 'tsclient_module=get_answer
* 
* @returns {number}
* 
* @see FUNCTION tsclient_test.get_answer
*/
export async function tsclientTestGetAnswer() : Promise<number> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-answer", {
        method: "GET",
    });
    return Number(await response.text());
}


""";

        [Fact]
        public void Test_GetAnswer_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "get_answer.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }

        private const string ExpectedStatus = """
const baseUrl = "";


/**
* function tsclient_test.get_answer_status()
* returns integer
* 
* @remarks
* comment on function tsclient_test.get_answer_status is 'tsclient_module=get_answer_status
* tsclient_status_code=true';
* 
* @returns {status: number, response: number, error: {status: number; title: string; detail?: string | null} | undefined}
* 
* @see FUNCTION tsclient_test.get_answer_status
*/
export async function tsclientTestGetAnswerStatus() : Promise<{status: number, response: number, error: {status: number; title: string; detail?: string | null} | undefined}> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-answer-status", {
        method: "GET",
    });
    return {
        status: response.status,
        response: response.ok ? Number(await response.text()) : undefined!,
        error: !response.ok && response.headers.get("content-length") !== "0" ? await response.json() as {status: number; title: string; detail?: string | null} : undefined
    };
}


""";

        [Fact]
        public void Test_GetAnswerStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "get_answer_status.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
