namespace NpgsqlRestTests.TsClientTests
{
    // Exercises TsClientOptions.OmitAutomaticParameters = true (the Setup.Program "TsClientOmit" config).
    // Server-filled parameters that cannot be set by the client are dropped from the generated request.
    [Collection("TestFixture")]
    public class OmitAutomaticParamsTests
    {
        // All parameters are HTTP Custom Type fields (automatic + optional) → the whole request collapses:
        // no request interface, no argument, no body, no query.
        private const string ExpectedAllOmitted = """
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
* @returns {string}
*
* @see FUNCTION tsclient_test.bodyparam_expanded
*/
export async function tsclientTestBodyparamExpanded() : Promise<string> {
    const response = await fetch(baseUrl + "/api/tsclient-test/bodyparam-expanded", {
        method: "POST",
    });
    return await response.text();
}


""";

        [Fact]
        public void Test_AllAutomatic_OmittedEntirely()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOmitOutputPath, "bodyparam_expanded.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);

            content.Should().NotContain("responseBody", "automatic HTTP-type fields must be omitted");
            content.Should().NotContain("interface ", "no request interface when every parameter is omitted");
            content.Should().NotContain("body:", "no body for an omitted body parameter");
            content.Should().NotContain("+ parseQuery", "no query call when every parameter is omitted");
            content.Should().Contain("tsclientTestBodyparamExpanded()", "the function takes no request argument");

            Normalize(content).Should().Be(Normalize(ExpectedAllOmitted));
        }

        // Mixed: a normal parameter (keyword) survives; the HTTP-type fields are omitted.
        private const string ExpectedMixed = """
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

interface ITsclientTestBodyparamMixedRequest {
    keyword: string | null;
}


/**
* function tsclient_test.bodyparam_mixed(
*     _keyword text,
*     _response_body text DEFAULT NULL::tsclient_test.tsc_http_probe,
*     _response_status_code integer,
*     _response_success boolean,
*     _response_error_message text
* )
* returns text
*
* @remarks
* comment on function tsclient_test.bodyparam_mixed is 'HTTP GET
* tsclient_module=bodyparam_mixed';
*
* @param request - Object containing request parameters.
* @returns {string}
*
* @see FUNCTION tsclient_test.bodyparam_mixed
*/
export async function tsclientTestBodyparamMixed(
    request: ITsclientTestBodyparamMixedRequest
) : Promise<string> {
    const response = await fetch(baseUrl + "/api/tsclient-test/bodyparam-mixed" + parseQuery(request), {
        method: "GET",
    });
    return await response.text();
}


""";

        [Fact]
        public void Test_MixedParams_KeepsNonAutomatic()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOmitOutputPath, "bodyparam_mixed.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);

            content.Should().Contain("keyword: string | null;", "non-automatic parameters are kept");
            content.Should().NotContain("responseBody", "automatic HTTP-type fields must be omitted");
            content.Should().NotContain("responseStatusCode", "automatic HTTP-type fields must be omitted");
            content.Should().Contain("parseQuery(request)", "the surviving parameter is still sent on the query");

            Normalize(content).Should().Be(Normalize(ExpectedMixed));
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
