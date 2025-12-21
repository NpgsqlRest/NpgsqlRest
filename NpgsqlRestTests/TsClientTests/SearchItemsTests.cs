namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientSearchItemsTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.search_items(_query text, _page int, _limit int default 10)
returns table (
    id int,
    name text,
    price numeric
)
language sql
as $$
select * from (
    values
    (1, 'Item A', 10.99),
    (2, 'Item B', 20.50)
) as t(id, name, price);
$$;
comment on function tsclient_test.search_items(text, int, int) is '
tsclient_module=search_items
HTTP GET
request_param_type query_string
';

create function tsclient_test.search_items_status(_query text, _page int, _limit int default 10)
returns table (
    id int,
    name text,
    price numeric
)
language sql
as $$
select * from (
    values
    (1, 'Item A', 10.99),
    (2, 'Item B', 20.50)
) as t(id, name, price);
$$;
comment on function tsclient_test.search_items_status(text, int, int) is '
tsclient_module=search_items_status
HTTP GET
request_param_type query_string
tsclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class SearchItemsTests(TestFixture test)
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

interface ITsclientTestSearchItemsRequest {
    query: string | null;
    page: number | null;
    limit?: number | null;
}

interface ITsclientTestSearchItemsResponse {
    id: number | null;
    name: string | null;
    price: number | null;
}


/**
* function tsclient_test.search_items(
*     _query text,
*     _page integer,
*     _limit integer DEFAULT 10
* )
* returns table(
*     id integer,
*     name text,
*     price numeric
* )
* 
* @remarks
* comment on function tsclient_test.search_items is 'tsclient_module=search_items
* HTTP GET
* request_param_type query_string';
* 
* @param request - Object containing request parameters.
* @returns {ITsclientTestSearchItemsResponse[]}
* 
* @see FUNCTION tsclient_test.search_items
*/
export async function tsclientTestSearchItems(
    request: ITsclientTestSearchItemsRequest
) : Promise<ITsclientTestSearchItemsResponse[]> {
    const response = await fetch(baseUrl + "/api/tsclient-test/search-items" + parseQuery(request), {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json() as ITsclientTestSearchItemsResponse[];
}


""";

        [Fact]
        public void Test_SearchItems_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "search_items.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }

        private const string ExpectedStatus = """
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

interface ITsclientTestSearchItemsStatusRequest {
    query: string | null;
    page: number | null;
    limit?: number | null;
}

interface ITsclientTestSearchItemsStatusResponse {
    id: number | null;
    name: string | null;
    price: number | null;
}


/**
* function tsclient_test.search_items_status(
*     _query text,
*     _page integer,
*     _limit integer DEFAULT 10
* )
* returns table(
*     id integer,
*     name text,
*     price numeric
* )
* 
* @remarks
* comment on function tsclient_test.search_items_status is 'tsclient_module=search_items_status
* HTTP GET
* request_param_type query_string
* tsclient_status_code=true';
* 
* @param request - Object containing request parameters.
* @returns {status: number, response: ITsclientTestSearchItemsStatusResponse[], error: {status: number; title: string; detail?: string | null} | undefined}
* 
* @see FUNCTION tsclient_test.search_items_status
*/
export async function tsclientTestSearchItemsStatus(
    request: ITsclientTestSearchItemsStatusRequest
) : Promise<{status: number, response: ITsclientTestSearchItemsStatusResponse[], error: {status: number; title: string; detail?: string | null} | undefined}> {
    const response = await fetch(baseUrl + "/api/tsclient-test/search-items-status" + parseQuery(request), {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return {
        status: response.status,
        response: response.status === 200 ? await response.json() as ITsclientTestSearchItemsStatusResponse[] : undefined!,
        error: response.status !== 200 ? await response.json() as {status: number; title: string; detail?: string | null} : undefined
    };
}


""";

        [Fact]
        public void Test_SearchItemsStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "search_items_status.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
