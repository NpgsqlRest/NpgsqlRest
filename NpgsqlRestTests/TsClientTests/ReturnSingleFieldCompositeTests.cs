namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientReturnSingleFieldCompositeTests()
        {
            script.Append("""
create schema if not exists tsclient_test;

-- Single field composite types
create type tsclient_test.single_status as (
    status boolean
);

create type tsclient_test.single_count as (
    count integer
);

-- Functions returning single-field composite types
create function tsclient_test.get_single_status()
returns tsclient_test.single_status
language sql as
$$
select row(true)::tsclient_test.single_status;
$$;
comment on function tsclient_test.get_single_status() is '
tsclient_module=single_field_composite
';

create function tsclient_test.get_single_count()
returns tsclient_test.single_count
language sql as
$$
select row(42)::tsclient_test.single_count;
$$;
comment on function tsclient_test.get_single_count() is '
tsclient_module=single_field_composite
';

-- Multi-field composite type for comparison
create type tsclient_test.user_status as (
    is_active boolean,
    user_count integer
);

create function tsclient_test.get_user_status()
returns tsclient_test.user_status
language sql as
$$
select row(true, 100)::tsclient_test.user_status;
$$;
comment on function tsclient_test.get_user_status() is '
tsclient_module=single_field_composite
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class ReturnSingleFieldCompositeTests
    {
        // Expected TypeScript output for single-field composite type returns
        private const string Expected = """
const baseUrl = "";

interface ITsclientTestGetSingleCountResponse {
    count: number | null;
}

interface ITsclientTestGetSingleStatusResponse {
    status: boolean | null;
}

interface ITsclientTestGetUserStatusResponse {
    isActive: boolean | null;
    userCount: number | null;
}


/**
* function tsclient_test.get_single_count()
* returns record
*
* @remarks
* comment on function tsclient_test.get_single_count is 'tsclient_module=single_field_composite
*
* @returns {ITsclientTestGetSingleCountResponse}
*
* @see FUNCTION tsclient_test.get_single_count
*/
export async function tsclientTestGetSingleCount() : Promise<ITsclientTestGetSingleCountResponse> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-single-count", {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json() as ITsclientTestGetSingleCountResponse;
}

/**
* function tsclient_test.get_single_status()
* returns record
*
* @remarks
* comment on function tsclient_test.get_single_status is 'tsclient_module=single_field_composite
*
* @returns {ITsclientTestGetSingleStatusResponse}
*
* @see FUNCTION tsclient_test.get_single_status
*/
export async function tsclientTestGetSingleStatus() : Promise<ITsclientTestGetSingleStatusResponse> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-single-status", {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json() as ITsclientTestGetSingleStatusResponse;
}

/**
* function tsclient_test.get_user_status()
* returns record
*
* @remarks
* comment on function tsclient_test.get_user_status is 'tsclient_module=single_field_composite
*
* @returns {ITsclientTestGetUserStatusResponse}
*
* @see FUNCTION tsclient_test.get_user_status
*/
export async function tsclientTestGetUserStatus() : Promise<ITsclientTestGetUserStatusResponse> {
    const response = await fetch(baseUrl + "/api/tsclient-test/get-user-status", {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json() as ITsclientTestGetUserStatusResponse;
}
""";

        [Fact]
        public void Test_SingleFieldCompositeTypes_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "single_field_composite.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            // Normalize trailing whitespace on lines
            var normalizedContent = NormalizeTrailingWhitespace(content);
            var normalizedExpected = NormalizeTrailingWhitespace(Expected);
            Assert.True(normalizedContent == normalizedExpected, $"ACTUAL:\n{content}\n\nEXPECTED:\n{Expected}");
        }

        private static string NormalizeTrailingWhitespace(string input)
        {
            var lines = input.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].TrimEnd();
            }
            // Also trim trailing empty lines
            return string.Join('\n', lines).TrimEnd('\n');
        }
    }
}
