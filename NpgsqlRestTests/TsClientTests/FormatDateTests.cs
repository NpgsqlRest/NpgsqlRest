namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientFormatDateTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.format_date(_dt timestamp)
returns text
language sql
as $$
select to_char(_dt, 'YYYY-MM-DD');
$$;
comment on function tsclient_test.format_date(timestamp) is '
tsclient_module=format_date
';

create function tsclient_test.format_date_status(_dt timestamp)
returns text
language sql
as $$
select to_char(_dt, 'YYYY-MM-DD');
$$;
comment on function tsclient_test.format_date_status(timestamp) is '
tsclient_module=format_date_status
tsclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class FormatDateTests
    {
        private const string Expected = """
const baseUrl = "";

interface ITsclientTestFormatDateRequest {
    dt: string | null;
}


/**
* function tsclient_test.format_date(
*     _dt timestamp without time zone
* )
* returns text
* 
* @remarks
* comment on function tsclient_test.format_date is 'tsclient_module=format_date
* 
* @param request - Object containing request parameters.
* @returns {string}
* 
* @see FUNCTION tsclient_test.format_date
*/
export async function tsclientTestFormatDate(
    request: ITsclientTestFormatDateRequest
) : Promise<string> {
    const response = await fetch(baseUrl + "/api/tsclient-test/format-date", {
        method: "POST",
        body: JSON.stringify(request)
    });
    return await response.text();
}


""";

        [Fact]
        public void Test_FormatDate_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "format_date.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }

        private const string ExpectedStatus = """
const baseUrl = "";

interface ITsclientTestFormatDateStatusRequest {
    dt: string | null;
}


/**
* function tsclient_test.format_date_status(
*     _dt timestamp without time zone
* )
* returns text
* 
* @remarks
* comment on function tsclient_test.format_date_status is 'tsclient_module=format_date_status
* tsclient_status_code=true';
* 
* @param request - Object containing request parameters.
* @returns {status: number, response: string, error: {status: number; title: string; detail?: string | null} | undefined}
* 
* @see FUNCTION tsclient_test.format_date_status
*/
export async function tsclientTestFormatDateStatus(
    request: ITsclientTestFormatDateStatusRequest
) : Promise<{status: number, response: string, error: {status: number; title: string; detail?: string | null} | undefined}> {
    const response = await fetch(baseUrl + "/api/tsclient-test/format-date-status", {
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
        public void Test_FormatDateStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "format_date_status.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
