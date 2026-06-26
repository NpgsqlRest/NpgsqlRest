namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientBodyParamGetTests()
        {
            // GET endpoint with @body_parameter_name targeting a defaulted parameter (so it carries the
            // TS optional "?" suffix in the interface). Regression for three TsClient bugs:
            //  1) the body expression emitted "request.payload?" (trailing "?" — a syntax error),
            //  2) the query-exclusion key was ["payload?"] (with "?") so the param was NOT stripped,
            //  3) a fetch body was emitted for a GET (fetch forbids a body on GET).
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.bodyparam_get(_keyword text, _payload text default null)
returns text
language sql
as $$
select coalesce(_keyword, '') || coalesce(_payload, '');
$$;
comment on function tsclient_test.bodyparam_get(text, text) is '
HTTP GET
tsclient_module=bodyparam_get
body_parameter_name payload';

-- POST endpoint with an HTTP Custom Type whose body field is targeted by @body_parameter_name using
-- its EXPANDED signature name (_response_body). The generator must match that name (via ExpandedName)
-- the same way the server does, emit the body with the bare converted name (request.responseBody),
-- and exclude it from the query string. Regression for the generator only matching converted/actual
-- names. The HTTP type points at an unused port; it never fires during code generation.
create type tsclient_test.tsc_http_probe as (
    body text,
    status_code int,
    success boolean,
    error_message text
);
comment on type tsclient_test.tsc_http_probe is 'GET http://localhost:1/tsc-dummy';

create function tsclient_test.bodyparam_expanded(_response tsclient_test.tsc_http_probe default null)
returns text
language plpgsql
as $$ begin return ''; end; $$;
comment on function tsclient_test.bodyparam_expanded(tsclient_test.tsc_http_probe) is '
HTTP POST
tsclient_module=bodyparam_expanded
body_parameter_name _response_body';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class BodyParamGetTests
    {
        private const string Expected = """
const baseUrl = "";
const parseQuery = (query: Record<any, any>) => "?" + Object.keys(query ? query : {})
    .map(key => {
        const value = (query[key] != null ? query[key] : "") as string;
        if (Array.isArray(value)) {
            return value.map((s: string) => s ? `${key}=${encodeURIComponent(s)}` : `${key}=`).join("&");
        }
        return `${key}=${encodeURIComponent(value)}`;
    })
    .join("&");

interface ITsclientTestBodyparamGetRequest {
    keyword: string | null;
    payload?: string | null;
}


/**
* function tsclient_test.bodyparam_get(
*     _keyword text,
*     _payload text DEFAULT NULL::text
* )
* returns text
*
* @remarks
* comment on function tsclient_test.bodyparam_get is 'HTTP GET
* tsclient_module=bodyparam_get
* body_parameter_name payload';
*
* @param request - Object containing request parameters.
* @returns {string}
*
* @see FUNCTION tsclient_test.bodyparam_get
*/
export async function tsclientTestBodyparamGet(
    request: ITsclientTestBodyparamGetRequest
) : Promise<string> {
    const response = await fetch(baseUrl + "/api/tsclient-test/bodyparam-get" + parseQuery((({ ["payload"]: _1, ...rest }) => rest)(request)), {
        method: "GET",
    });
    return await response.text();
}


""";

        private const string ExpectedExpanded = """
const baseUrl = "";
const parseQuery = (query: Record<any, any>) => "?" + Object.keys(query ? query : {})
    .map(key => {
        const value = (query[key] != null ? query[key] : "") as string;
        if (Array.isArray(value)) {
            return value.map((s: string) => s ? `${key}=${encodeURIComponent(s)}` : `${key}=`).join("&");
        }
        return `${key}=${encodeURIComponent(value)}`;
    })
    .join("&");

interface ITsclientTestBodyparamExpandedRequest {
    responseBody?: string | null;
    responseStatusCode?: number | null;
    responseSuccess?: boolean | null;
    responseErrorMessage?: string | null;
}


/**
* function tsclient_test.bodyparam_expanded(
*     _response_body text DEFAULT NULL::tsclient_test.tsc_http_probe,
*     _response_status_code integer,
*     _response_success boolean,
*     _response_error_message text
* )
* returns text
*
* @remarks
* comment on function tsclient_test.bodyparam_expanded is 'HTTP POST
* tsclient_module=bodyparam_expanded
* body_parameter_name _response_body';
*
* @param request - Object containing request parameters.
* @returns {string}
*
* @see FUNCTION tsclient_test.bodyparam_expanded
*/
export async function tsclientTestBodyparamExpanded(
    request: ITsclientTestBodyparamExpandedRequest
) : Promise<string> {
    const response = await fetch(baseUrl + "/api/tsclient-test/bodyparam-expanded" + parseQuery((({ ["responseBody"]: _1, ...rest }) => rest)(request)), {
        method: "POST",
        body: request.responseBody
    });
    return await response.text();
}


""";

        [Fact]
        public void Test_BodyParamGet_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "bodyparam_get.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);

            // The fixed generator must NOT emit the optional "?" suffix in the runtime name, and must
            // not attach a body to a GET request.
            content.Should().NotContain("request.payload?", "the body expression must not carry the TS optional suffix");
            content.Should().NotContain("[\"payload?\"]", "the query-exclusion key must be the bare property name");
            content.Should().NotContain("body:", "a GET request must not emit a fetch body");

            // Full-content match. Normalize per-line trailing whitespace and line endings: the generator
            // emits trailing spaces on blank comment lines, which are easily mangled when transcribed.
            Normalize(content).Should().Be(Normalize(Expected));
        }

        [Fact]
        public void Test_BodyParamExpanded_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "bodyparam_expanded.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);

            // @body_parameter_name targeted the expanded signature name (_response_body); the generator
            // must resolve it to the body field and emit the body with the bare converted name.
            content.Should().Contain("body: request.responseBody", "the expanded name must resolve to the body field, emitted by its converted name");
            content.Should().Contain("[\"responseBody\"]", "the body parameter must be excluded from the query string");
            content.Should().NotContain("parseQuery(request)", "the body parameter must not be left in the unfiltered query");
            content.Should().NotContain("request.responseBody?", "no trailing optional suffix in the runtime name");

            Normalize(content).Should().Be(Normalize(ExpectedExpanded));
        }

        private static string Normalize(string s)
        {
            var lines = s.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].TrimEnd();
            }
            return string.Join("\n", lines);
        }
    }
}
