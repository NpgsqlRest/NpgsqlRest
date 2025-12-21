namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientGetProductTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.get_product(_category_id int, _product_id int)
returns table (
    id int,
    category_id int,
    name text,
    price numeric
)
language sql
as $$
select * from (
    values
    (_product_id, _category_id, 'Product Name', 99.99)
) as t(id, category_id, name, price);
$$;
comment on function tsclient_test.get_product(int, int) is '
tsclient_module=get_product
HTTP GET
path /api/categories/{_category_id}/products/{_product_id}
';

create function tsclient_test.get_product_status(_category_id int, _product_id int)
returns table (
    id int,
    category_id int,
    name text,
    price numeric
)
language sql
as $$
select * from (
    values
    (_product_id, _category_id, 'Product Name', 99.99)
) as t(id, category_id, name, price);
$$;
comment on function tsclient_test.get_product_status(int, int) is '
tsclient_module=get_product_status
HTTP GET
path /api/categories/{_category_id}/products/{_product_id}/status
tsclient_status_code=true
';

create function tsclient_test.get_product_parse_url(_category_id int, _product_id int)
returns table (
    id int,
    category_id int,
    name text,
    price numeric
)
language sql
as $$
select * from (
    values
    (_product_id, _category_id, 'Product Name', 99.99)
) as t(id, category_id, name, price);
$$;
comment on function tsclient_test.get_product_parse_url(int, int) is '
tsclient_module=get_product_parse_url
HTTP GET
path /api/categories/{_category_id}/products/{_product_id}/parse-url
tsclient_parse_url=true
';

create function tsclient_test.get_product_parse_request(_category_id int, _product_id int)
returns table (
    id int,
    category_id int,
    name text,
    price numeric
)
language sql
as $$
select * from (
    values
    (_product_id, _category_id, 'Product Name', 99.99)
) as t(id, category_id, name, price);
$$;
comment on function tsclient_test.get_product_parse_request(int, int) is '
tsclient_module=get_product_parse_request
HTTP GET
path /api/categories/{_category_id}/products/{_product_id}/parse-request
tsclient_parse_request=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class GetProductTests(TestFixture test)
    {
        private const string Expected = """
const baseUrl = "";

interface ICategoriesCategoryIdProductsProductIdRequest {
    categoryId: number | null;
    productId: number | null;
}

interface ICategoriesCategoryIdProductsProductIdResponse {
    id: number | null;
    categoryId: number | null;
    name: string | null;
    price: number | null;
}


/**
* function tsclient_test.get_product(
*     _category_id integer,
*     _product_id integer
* )
* returns table(
*     id integer,
*     category_id integer,
*     name text,
*     price numeric
* )
* 
* @remarks
* comment on function tsclient_test.get_product is 'tsclient_module=get_product
* HTTP GET
* path /api/categories/{_category_id}/products/{_product_id}';
* 
* @param request - Object containing request parameters.
* @returns {ICategoriesCategoryIdProductsProductIdResponse[]}
* 
* @see FUNCTION tsclient_test.get_product
*/
export async function categoriesCategoryIdProductsProductId(
    request: ICategoriesCategoryIdProductsProductIdRequest
) : Promise<ICategoriesCategoryIdProductsProductIdResponse[]> {
    const response = await fetch(`${baseUrl}/api/categories/${request._category_id}/products/${request._product_id}`, {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json() as ICategoriesCategoryIdProductsProductIdResponse[];
}


""";

        [Fact]
        public void Test_GetProduct_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "get_product.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }

        private const string ExpectedStatus = """
const baseUrl = "";

interface ICategoriesCategoryIdProductsProductIdStatusRequest {
    categoryId: number | null;
    productId: number | null;
}

interface ICategoriesCategoryIdProductsProductIdStatusResponse {
    id: number | null;
    categoryId: number | null;
    name: string | null;
    price: number | null;
}


/**
* function tsclient_test.get_product_status(
*     _category_id integer,
*     _product_id integer
* )
* returns table(
*     id integer,
*     category_id integer,
*     name text,
*     price numeric
* )
* 
* @remarks
* comment on function tsclient_test.get_product_status is 'tsclient_module=get_product_status
* HTTP GET
* path /api/categories/{_category_id}/products/{_product_id}/status
* tsclient_status_code=true';
* 
* @param request - Object containing request parameters.
* @returns {status: number, response: ICategoriesCategoryIdProductsProductIdStatusResponse[], error: {status: number; title: string; detail?: string | null} | undefined}
* 
* @see FUNCTION tsclient_test.get_product_status
*/
export async function categoriesCategoryIdProductsProductIdStatus(
    request: ICategoriesCategoryIdProductsProductIdStatusRequest
) : Promise<{status: number, response: ICategoriesCategoryIdProductsProductIdStatusResponse[], error: {status: number; title: string; detail?: string | null} | undefined}> {
    const response = await fetch(`${baseUrl}/api/categories/${request._category_id}/products/${request._product_id}/status`, {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return {
        status: response.status,
        response: response.status === 200 ? await response.json() as ICategoriesCategoryIdProductsProductIdStatusResponse[] : undefined!,
        error: response.status !== 200 ? await response.json() as {status: number; title: string; detail?: string | null} : undefined
    };
}


""";

        [Fact]
        public void Test_GetProductStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "get_product_status.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }

        private const string ExpectedParseUrl = """
const baseUrl = "";

interface ICategoriesCategoryIdProductsProductIdParseUrlRequest {
    categoryId: number | null;
    productId: number | null;
}

interface ICategoriesCategoryIdProductsProductIdParseUrlResponse {
    id: number | null;
    categoryId: number | null;
    name: string | null;
    price: number | null;
}


/**
* function tsclient_test.get_product_parse_url(
*     _category_id integer,
*     _product_id integer
* )
* returns table(
*     id integer,
*     category_id integer,
*     name text,
*     price numeric
* )
* 
* @remarks
* comment on function tsclient_test.get_product_parse_url is 'tsclient_module=get_product_parse_url
* HTTP GET
* path /api/categories/{_category_id}/products/{_product_id}/parse-url
* tsclient_parse_url=true';
* 
* @param request - Object containing request parameters.
* @param parseUrl - Optional function to parse constructed URL before making the request.
* @returns {ICategoriesCategoryIdProductsProductIdParseUrlResponse[]}
* 
* @see FUNCTION tsclient_test.get_product_parse_url
*/
export async function categoriesCategoryIdProductsProductIdParseUrl(
    request: ICategoriesCategoryIdProductsProductIdParseUrlRequest,
    parseUrl: (url: string) => string = url => url
) : Promise<ICategoriesCategoryIdProductsProductIdParseUrlResponse[]> {
    const response = await fetch(parseUrl(`${baseUrl}/api/categories/${request._category_id}/products/${request._product_id}/parse-url`), {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json() as ICategoriesCategoryIdProductsProductIdParseUrlResponse[];
}


""";

        [Fact]
        public void Test_GetProductParseUrl_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "get_product_parse_url.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedParseUrl);
        }

        private const string ExpectedParseRequest = """
const baseUrl = "";

interface ICategoriesCategoryIdProductsProductIdParseRequestRequest {
    categoryId: number | null;
    productId: number | null;
}

interface ICategoriesCategoryIdProductsProductIdParseRequestResponse {
    id: number | null;
    categoryId: number | null;
    name: string | null;
    price: number | null;
}


/**
* function tsclient_test.get_product_parse_request(
*     _category_id integer,
*     _product_id integer
* )
* returns table(
*     id integer,
*     category_id integer,
*     name text,
*     price numeric
* )
* 
* @remarks
* comment on function tsclient_test.get_product_parse_request is 'tsclient_module=get_product_parse_request
* HTTP GET
* path /api/categories/{_category_id}/products/{_product_id}/parse-request
* tsclient_parse_request=true';
* 
* @param request - Object containing request parameters.
* @param parseRequest - Optional function to parse constructed request before making the request.
* @returns {ICategoriesCategoryIdProductsProductIdParseRequestResponse[]}
* 
* @see FUNCTION tsclient_test.get_product_parse_request
*/
export async function categoriesCategoryIdProductsProductIdParseRequest(
    request: ICategoriesCategoryIdProductsProductIdParseRequestRequest,
    parseRequest: (request: RequestInit) => RequestInit = request => request
) : Promise<ICategoriesCategoryIdProductsProductIdParseRequestResponse[]> {
    const response = await fetch(`${baseUrl}/api/categories/${request._category_id}/products/${request._product_id}/parse-request`, parseRequest({
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    }));
    return await response.json() as ICategoriesCategoryIdProductsProductIdParseRequestResponse[];
}


""";

        [Fact]
        public void Test_GetProductParseRequest_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "get_product_parse_request.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedParseRequest);
        }
    }
}
