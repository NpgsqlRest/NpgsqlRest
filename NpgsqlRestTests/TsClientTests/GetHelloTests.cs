namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientGetHelloTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.get_hello()
returns text
language sql
as $$
select 'Hello, World!';
$$;
comment on function tsclient_test.get_hello() is '
tsclient_module=get_hello
';

create function tsclient_test.get_hello_status()
returns text
language sql
as $$
select 'Hello, World!';
$$;
comment on function tsclient_test.get_hello_status() is '
tsclient_module=get_hello_status
tsclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class GetHelloTests(TestFixture test)
    {
        private const string Expected = """
const baseUrl = "";


/**
* function tsclient_test.get_hello()
* returns text
* 
* @remarks
* comment on function tsclient_test.get_hello is 'tsclient_module=get_hello
* 
* @returns {string}
* 
* @see FUNCTION tsclient_test.get_hello
*/
export async function tsclientTestGetHello() : Promise<string> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-hello", {
        method: "GET",
    });
    return await response.text();
}


""";

        [Fact]
        public void Test_GetHello_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "get_hello.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }

        private const string ExpectedStatus = """
const baseUrl = "";


/**
* function tsclient_test.get_hello_status()
* returns text
* 
* @remarks
* comment on function tsclient_test.get_hello_status is 'tsclient_module=get_hello_status
* tsclient_status_code=true';
* 
* @returns {status: number, response: string, error: {status: number; title: string; detail?: string | null} | undefined}
* 
* @see FUNCTION tsclient_test.get_hello_status
*/
export async function tsclientTestGetHelloStatus() : Promise<{status: number, response: string, error: {status: number; title: string; detail?: string | null} | undefined}> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-hello-status", {
        method: "GET",
    });
    return {
        status: response.status,
        response: response.status === 200 ? await response.text() : undefined!,
        error: response.status !== 200 ? await response.json() as {status: number; title: string; detail?: string | null} : undefined
    };
}


""";

        [Fact]
        public void Test_GetHelloStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "get_hello_status.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
