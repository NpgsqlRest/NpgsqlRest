namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientExportTypesTests()
        {
            script.Append("""
create function tsclient_test.search_products(_query text default null, _max_price numeric default null)
returns table (id int, name text, price numeric)
language sql
as $$
select 1, 'x'::text, 1.0::numeric;
$$;
comment on function tsclient_test.search_products(text, numeric) is '
HTTP GET
tsclient_module=search_products
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class ExportTypesTests
    {
        // ExportTypes = true with CreateSeparateTypeFile = true: interfaces live in an importable
        // module {name}Types.ts (not an ambient .d.ts), emitted as `export interface`, and the
        // client file imports them by name.
        private const string ExpectedClient = """
import type { ITsclientTestSearchProductsRequest, ITsclientTestSearchProductsResponse } from "./search_productsTypes";
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
* 
* @param request - Object containing request parameters.
* @returns {ITsclientTestSearchProductsResponse[]}
* 
* @see FUNCTION tsclient_test.search_products
*/
export async function tsclientTestSearchProducts(
    request: ITsclientTestSearchProductsRequest
) : Promise<ITsclientTestSearchProductsResponse[]> {
    const response = await fetch(baseUrl + "/api/tsclient-test/search-products" + parseQuery(request), {
        method: "GET",
        headers: {
            "Content-Type": "application/json"
        },
    });
    return await response.json() as ITsclientTestSearchProductsResponse[];
}

""";

        private const string ExpectedTypes = """
export interface ITsclientTestSearchProductsRequest {
    query?: string | null;
    maxPrice?: number | null;
}

export interface ITsclientTestSearchProductsResponse {
    id: number | null;
    name: string | null;
    price: number | null;
}


""";

        [Fact]
        public void Test_ExportTypes_ClientFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientExportOutputPath, "search_products.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");
            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedClient);
        }

        [Fact]
        public void Test_ExportTypes_TypeFile_IsImportableModule()
        {
            // Importable module: {name}Types.ts, not the ambient {name}Types.d.ts.
            var tsPath = Path.Combine(Setup.Program.TsClientExportOutputPath, "search_productsTypes.ts");
            var dtsPath = Path.Combine(Setup.Program.TsClientExportOutputPath, "search_productsTypes.d.ts");
            File.Exists(tsPath).Should().BeTrue($"Expected importable type module at {tsPath}");
            File.Exists(dtsPath).Should().BeFalse($"Ambient declaration file should not be produced at {dtsPath}");
            var content = File.ReadAllText(tsPath);
            content.Should().Be(ExpectedTypes);
        }
    }
}
