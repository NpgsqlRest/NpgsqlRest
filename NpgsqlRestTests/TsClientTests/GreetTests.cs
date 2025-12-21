namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientGreetTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.greet(_name text)
returns text
language sql
as $$
select 'Hello, ' || _name || '!';
$$;
comment on function tsclient_test.greet(text) is '
tsclient_module=greet
';

create function tsclient_test.greet_status(_name text)
returns text
language sql
as $$
select 'Hello, ' || _name || '!';
$$;
comment on function tsclient_test.greet_status(text) is '
tsclient_module=greet_status
tsclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class GreetTests
    {
        private const string Expected = """
const baseUrl = "";

interface ITsclientTestGreetRequest {
    name: string | null;
}


/**
* function tsclient_test.greet(
*     _name text
* )
* returns text
* 
* @remarks
* comment on function tsclient_test.greet is 'tsclient_module=greet
* 
* @param request - Object containing request parameters.
* @returns {string}
* 
* @see FUNCTION tsclient_test.greet
*/
export async function tsclientTestGreet(
    request: ITsclientTestGreetRequest
) : Promise<string> {
    const response = await fetch(baseUrl + "/api/tsclient-test/greet", {
        method: "POST",
        body: JSON.stringify(request)
    });
    return await response.text();
}


""";

        [Fact]
        public void Test_Greet_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "greet.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }

        private const string ExpectedStatus = """
const baseUrl = "";

interface ITsclientTestGreetStatusRequest {
    name: string | null;
}


/**
* function tsclient_test.greet_status(
*     _name text
* )
* returns text
* 
* @remarks
* comment on function tsclient_test.greet_status is 'tsclient_module=greet_status
* tsclient_status_code=true';
* 
* @param request - Object containing request parameters.
* @returns {status: number, response: string, error: {status: number; title: string; detail?: string | null} | undefined}
* 
* @see FUNCTION tsclient_test.greet_status
*/
export async function tsclientTestGreetStatus(
    request: ITsclientTestGreetStatusRequest
) : Promise<{status: number, response: string, error: {status: number; title: string; detail?: string | null} | undefined}> {
    const response = await fetch(baseUrl + "/api/tsclient-test/greet-status", {
        method: "POST",
        body: JSON.stringify(request)
    });
    return {
        status: response.status,
        response: response.status === 200 ? await response.text() : undefined!,
        error: response.status !== 200 ? await response.json() as {status: number; title: string; detail?: string | null} : undefined
    };
}


""";

        [Fact]
        public void Test_GreetStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "greet_status.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
