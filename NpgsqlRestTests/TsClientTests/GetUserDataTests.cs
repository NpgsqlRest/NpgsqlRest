namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientGetUserDataTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.get_user_data()
returns table (
    id int,
    name text,
    email text,
    is_active bool
)
language sql
as $$
select * from (
    values
    (1, 'Alice', 'alice@example.com', true),
    (2, 'Bob', 'bob@example.com', false)
) as t(id, name, email, is_active);
$$;
comment on function tsclient_test.get_user_data() is '
tsclient_module=get_user_data
';

create function tsclient_test.get_user_data_status()
returns table (
    id int,
    name text,
    email text,
    is_active bool
)
language sql
as $$
select * from (
    values
    (1, 'Alice', 'alice@example.com', true),
    (2, 'Bob', 'bob@example.com', false)
) as t(id, name, email, is_active);
$$;
comment on function tsclient_test.get_user_data_status() is '
tsclient_module=get_user_data_status
tsclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class GetUserDataTests
    {
        private const string Expected = """
const baseUrl = "";

interface ITsclientTestGetUserDataResponse {
    id: number | null;
    name: string | null;
    email: string | null;
    isActive: boolean | null;
}


/**
* function tsclient_test.get_user_data()
* returns table(
*     id integer,
*     name text,
*     email text,
*     is_active boolean
* )
* 
* @remarks
* comment on function tsclient_test.get_user_data is 'tsclient_module=get_user_data
* 
* @returns {ITsclientTestGetUserDataResponse[]}
* 
* @see FUNCTION tsclient_test.get_user_data
*/
export async function tsclientTestGetUserData() : Promise<ITsclientTestGetUserDataResponse[]> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-user-data", {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json() as ITsclientTestGetUserDataResponse[];
}


""";

        [Fact]
        public void Test_GetUserData_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "get_user_data.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }

        private const string ExpectedStatus = """
const baseUrl = "";

interface ITsclientTestGetUserDataStatusResponse {
    id: number | null;
    name: string | null;
    email: string | null;
    isActive: boolean | null;
}


/**
* function tsclient_test.get_user_data_status()
* returns table(
*     id integer,
*     name text,
*     email text,
*     is_active boolean
* )
* 
* @remarks
* comment on function tsclient_test.get_user_data_status is 'tsclient_module=get_user_data_status
* tsclient_status_code=true';
* 
* @returns {status: number, response: ITsclientTestGetUserDataStatusResponse[], error: {status: number; title: string; detail?: string | null} | undefined}
* 
* @see FUNCTION tsclient_test.get_user_data_status
*/
export async function tsclientTestGetUserDataStatus() : Promise<{status: number, response: ITsclientTestGetUserDataStatusResponse[], error: {status: number; title: string; detail?: string | null} | undefined}> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-user-data-status", {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return {
        status: response.status,
        response: response.ok ? await response.json() as ITsclientTestGetUserDataStatusResponse[] : undefined!,
        error: !response.ok ? await response.json() as {status: number; title: string; detail?: string | null} : undefined
    };
}


""";

        [Fact]
        public void Test_GetUserDataStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "get_user_data_status.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
