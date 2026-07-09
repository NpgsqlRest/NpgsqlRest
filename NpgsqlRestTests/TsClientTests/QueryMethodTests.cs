namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientQueryMethodTests()
        {
            script.Append("""
create schema if not exists tsclient_test;

-- The HTTP QUERY method (safe + idempotent, carries the query in the request body):
-- parameters default to the JSON body, generators emit method QUERY with a body,
-- ReactQuery hooks treat it as a query (useQuery), and OpenAPI 3.0/3.1 skips it
-- (no `query` path item key until OpenAPI 3.2).
create function tsclient_test.query_search(_q text, _top int default 3)
returns text
language sql
as $$
select _q || '/' || _top::text;
$$;
comment on function tsclient_test.query_search(text, int) is '
HTTP QUERY
tsclient_module=query_search
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class QueryMethodTests(Setup.TestFixture test)
    {
        [Fact]
        public async Task QueryMethod_Endpoint_BindsParametersFromJsonBody()
        {
            using var request = new HttpRequestMessage(
                new HttpMethod("QUERY"),
                "/api/tsclient-test/query-search")
            {
                Content = new StringContent("""{"q":"abc","top":7}""", Encoding.UTF8, "application/json"),
            };
            using var response = await test.Client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be("abc/7");
        }

        [Fact]
        public async Task QueryMethod_Endpoint_DefaultedParameterCanBeOmitted()
        {
            using var request = new HttpRequestMessage(
                new HttpMethod("QUERY"),
                "/api/tsclient-test/query-search")
            {
                Content = new StringContent("""{"q":"abc"}""", Encoding.UTF8, "application/json"),
            };
            using var response = await test.Client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be("abc/3");
        }

        private const string Expected = """
const baseUrl = "";

interface ITsclientTestQuerySearchRequest {
    q: string | null;
    top?: number | null;
}


/**
* function tsclient_test.query_search(
*     _q text,
*     _top integer DEFAULT 3
* )
* returns text
* 
* @remarks
* comment on function tsclient_test.query_search is 'HTTP QUERY
* tsclient_module=query_search';
* 
* @param request - Object containing request parameters.
* @returns {string}
* 
* @see FUNCTION tsclient_test.query_search
*/
export async function tsclientTestQuerySearch(
    request: ITsclientTestQuerySearchRequest
) : Promise<string> {
    const response = await fetch(baseUrl + "/api/tsclient-test/query-search", {
        method: "QUERY",
        body: JSON.stringify(request)
    });
    return await response.text();
}


""";

        [Fact]
        public void QueryMethod_TsClient_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "query_search.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
            content.Should().Contain("method: \"QUERY\"");
            content.Should().Contain("body: JSON.stringify(request)");
        }

        [Fact]
        public void QueryMethod_ReactQueryHooks_EmitUseQuery()
        {
            var filePath = Path.Combine(Setup.Program.TsClientHooksOutputPath, "hooks", "query_searchHooks.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Contain("export function useTsclientTestQuerySearch(", "QUERY is a safe method and must map to useQuery");
            content.Should().Contain("return useQuery({");
            content.Should().NotContain("useTsclientTestQuerySearchMutation");
            content.Should().NotContain("return useMutation({");
        }

        [Fact]
        public void QueryMethod_OpenApi_SkipsTheEndpoint()
        {
            // OpenAPI 3.0/3.1 path items have no `query` operation key (supported from OpenAPI 3.2),
            // so QUERY endpoints are skipped with a logged warning instead of emitting an invalid key.
            var filePath = Path.Combine(Setup.Program.OpenApiOutputPath, "openapi.json");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var doc = JsonNode.Parse(File.ReadAllText(filePath))!;
            var paths = doc["paths"]!.AsObject();
            paths.ContainsKey("/api/tsclient-test/query-search").Should().BeFalse(
                "QUERY endpoints cannot be represented in OpenAPI 3.0/3.1");
        }
    }
}
