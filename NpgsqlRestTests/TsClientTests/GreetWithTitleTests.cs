namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientGreetWithTitleTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.greet_with_title(_name text, _title text default 'Mr.')
returns text
language sql
as $$
select 'Hello, ' || _title || ' ' || _name || '!';
$$;
comment on function tsclient_test.greet_with_title(text, text) is '
tsclient_module=greet_with_title
';

create function tsclient_test.greet_with_title_status(_name text, _title text default 'Mr.')
returns text
language sql
as $$
select 'Hello, ' || _title || ' ' || _name || '!';
$$;
comment on function tsclient_test.greet_with_title_status(text, text) is '
tsclient_module=greet_with_title_status
tsclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class GreetWithTitleTests
    {
        private const string Expected = """
const baseUrl = "";

interface ITsclientTestGreetWithTitleRequest {
    name: string | null;
    title?: string | null;
}


/**
* function tsclient_test.greet_with_title(
*     _name text,
*     _title text DEFAULT 'Mr.'::text
* )
* returns text
* 
* @remarks
* comment on function tsclient_test.greet_with_title is 'tsclient_module=greet_with_title
* 
* @param request - Object containing request parameters.
* @returns {string}
* 
* @see FUNCTION tsclient_test.greet_with_title
*/
export async function tsclientTestGreetWithTitle(
    request: ITsclientTestGreetWithTitleRequest
) : Promise<string> {
    const response = await fetch(baseUrl + "/api/tsclient-test/greet-with-title", {
        method: "POST",
        body: JSON.stringify(request)
    });
    return await response.text();
}


""";

        [Fact]
        public void Test_GreetWithTitle_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "greet_with_title.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }

        private const string ExpectedStatus = """
const baseUrl = "";

interface ITsclientTestGreetWithTitleStatusRequest {
    name: string | null;
    title?: string | null;
}


/**
* function tsclient_test.greet_with_title_status(
*     _name text,
*     _title text DEFAULT 'Mr.'::text
* )
* returns text
* 
* @remarks
* comment on function tsclient_test.greet_with_title_status is 'tsclient_module=greet_with_title_status
* tsclient_status_code=true';
* 
* @param request - Object containing request parameters.
* @returns {status: number, response: string, error: {status: number; title: string; detail?: string | null} | undefined}
* 
* @see FUNCTION tsclient_test.greet_with_title_status
*/
export async function tsclientTestGreetWithTitleStatus(
    request: ITsclientTestGreetWithTitleStatusRequest
) : Promise<{status: number, response: string, error: {status: number; title: string; detail?: string | null} | undefined}> {
    const response = await fetch(baseUrl + "/api/tsclient-test/greet-with-title-status", {
        method: "POST",
        body: JSON.stringify(request)
    });
    return {
        status: response.status,
        response: response.ok ? await response.text() : undefined!,
        error: !response.ok ? await response.json() as {status: number; title: string; detail?: string | null} : undefined
    };
}


""";

        [Fact]
        public void Test_GreetWithTitleStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "greet_with_title_status.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
