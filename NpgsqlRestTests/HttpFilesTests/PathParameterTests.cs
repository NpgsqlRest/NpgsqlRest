namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void HttpFilesPathParameterTests()
        {
            script.Append("""
create function httpfiles_get_product(_category_id int, _product_id int)
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
comment on function httpfiles_get_product(int, int) is '
HTTP GET /api/categories/{_category_id}/products/{_product_id}
';

create function httpfiles_get_product_with_query(_category_id int, _product_id int, include_reviews boolean default false)
returns table (
    id int,
    category_id int,
    name text,
    price numeric,
    include_reviews boolean
)
language sql
as $$
select * from (
    values
    (_product_id, _category_id, 'Product Name', 99.99, include_reviews)
) as t(id, category_id, name, price, include_reviews);
$$;
comment on function httpfiles_get_product_with_query(int, int, boolean) is '
HTTP GET /api/categories/{_category_id}/products/{_product_id}/details
';

create function httpfiles_update_product(_category_id int, _product_id int, new_name text)
returns text
language sql
as $$
select 'Updated Product ' || _product_id || ' in Category ' || _category_id || ' to ' || new_name;
$$;
comment on function httpfiles_update_product(int, int, text) is '
HTTP POST /api/categories/{_category_id}/products/{_product_id}/update
';

create function httpfiles_single_path_param(p_id int)
returns text
language sql
as $$
select 'Product ' || p_id;
$$;
comment on function httpfiles_single_path_param(int) is '
HTTP GET /api/products/{p_id}
';
""");
        }
    }
}

namespace NpgsqlRestTests.HttpFilesTests
{
    [Collection("TestFixture")]
    public class PathParameterTests
    {
        /// <summary>
        /// Test that path parameters are properly handled in HTTP files.
        /// Path parameters should:
        /// 1. Keep the {param} placeholder in the URL path
        /// 2. NOT be included in the query string (they're already in the path)
        /// </summary>
        [Fact]
        public void Test_SinglePathParameter_NotInQueryString()
        {
            var filePath = Path.Combine(Setup.Program.HttpFilesOutputPath, "npgsqlrest.http");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);

            // Should contain the path with placeholder
            content.Should().Contain("/api/products/{p_id}");

            // The path parameter should NOT appear as a query string parameter
            // Current behavior (broken): adds pId=1 to query string
            // Expected behavior: no query string for path-only parameters
            var singlePathParamSection = ExtractSection(content, "/api/products/{p_id}");
            singlePathParamSection.Should().NotBeNull("Should find the single path param endpoint");

            // This test documents the EXPECTED behavior:
            // Path parameter should not be duplicated in query string
            singlePathParamSection.Should().NotContain("?pId=", "Path parameter should not appear in query string");
            singlePathParamSection.Should().NotContain("?p_id=", "Path parameter should not appear in query string");
        }

        /// <summary>
        /// Test that path parameters with multiple segments work correctly.
        /// </summary>
        [Fact]
        public void Test_MultiplePathParameters_NotInQueryString()
        {
            var filePath = Path.Combine(Setup.Program.HttpFilesOutputPath, "npgsqlrest.http");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);

            // Should contain the path with placeholders
            content.Should().Contain("/api/categories/{_category_id}/products/{_product_id}");

            // Find the GET request for this endpoint (not the one with /details or /update)
            var multiPathParamSection = ExtractSection(content, "GET {{host}}/api/categories/{_category_id}/products/{_product_id}\n");
            multiPathParamSection.Should().NotBeNull("Should find the multi path param endpoint");

            // Path parameters should NOT be in query string
            multiPathParamSection.Should().NotContain("?", "Path-only endpoint should have no query string");
            multiPathParamSection.Should().NotContain("categoryId=", "Path parameter should not appear in query string");
            multiPathParamSection.Should().NotContain("productId=", "Path parameter should not appear in query string");
        }

        /// <summary>
        /// Test that when an endpoint has both path parameters AND query parameters,
        /// only the query parameters appear in the query string.
        /// </summary>
        [Fact]
        public void Test_PathParametersWithQueryString_OnlyQueryParamsInQueryString()
        {
            var filePath = Path.Combine(Setup.Program.HttpFilesOutputPath, "npgsqlrest.http");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);

            // Find the endpoint with both path and query params
            var mixedParamSection = ExtractSection(content, "/api/categories/{_category_id}/products/{_product_id}/details");
            mixedParamSection.Should().NotBeNull("Should find the mixed param endpoint");

            // Should have the path parameters in the URL
            mixedParamSection.Should().Contain("{_category_id}");
            mixedParamSection.Should().Contain("{_product_id}");

            // Should have ONLY includeReviews in query string (not the path params)
            mixedParamSection.Should().Contain("includeReviews=", "Query parameter should be in query string");

            // Path parameters should NOT be in query string
            mixedParamSection.Should().NotContain("categoryId=", "Path parameter should not appear in query string");
            mixedParamSection.Should().NotContain("productId=", "Path parameter should not appear in query string");
        }

        /// <summary>
        /// Test that POST requests with path parameters work correctly.
        /// Path parameters should be in the URL, body parameters in the JSON body.
        /// </summary>
        [Fact]
        public void Test_PostWithPathParameters_BodyNotDuplicated()
        {
            var filePath = Path.Combine(Setup.Program.HttpFilesOutputPath, "npgsqlrest.http");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);

            // Find the POST endpoint
            var postSection = ExtractSection(content, "POST {{host}}/api/categories/{_category_id}/products/{_product_id}/update");
            postSection.Should().NotBeNull("Should find the POST endpoint");

            // Should have path parameters in URL
            postSection.Should().Contain("{_category_id}");
            postSection.Should().Contain("{_product_id}");

            // Should have JSON body with only the non-path parameter
            postSection.Should().Contain("content-type: application/json");
            postSection.Should().Contain("\"newName\":");

            // Path parameters should NOT be in the JSON body
            postSection.Should().NotContain("\"categoryId\":", "Path parameter should not appear in JSON body");
            postSection.Should().NotContain("\"productId\":", "Path parameter should not appear in JSON body");
        }

        /// <summary>
        /// Helper to extract a section of the HTTP file content starting from a marker.
        /// Returns the content from the marker until the next ### separator.
        /// </summary>
        private static string? ExtractSection(string content, string marker)
        {
            var startIndex = content.IndexOf(marker, StringComparison.Ordinal);
            if (startIndex < 0)
                return null;

            // Find the start of this request block (look backwards for ### or start of file)
            var blockStart = content.LastIndexOf("###", startIndex, StringComparison.Ordinal);
            if (blockStart < 0)
            {
                // Look for @host= which indicates start of file
                blockStart = content.LastIndexOf("@host=", startIndex, StringComparison.Ordinal);
                if (blockStart < 0)
                    blockStart = 0;
                else
                {
                    // Skip past the @host line
                    var nextNewline = content.IndexOf('\n', blockStart);
                    if (nextNewline >= 0)
                        blockStart = nextNewline + 1;
                }
            }
            else
            {
                blockStart += 3; // Skip past ###
            }

            // Find end of this section (next ### or end of file)
            var endIndex = content.IndexOf("###", startIndex, StringComparison.Ordinal);
            if (endIndex < 0)
                endIndex = content.Length;

            return content.Substring(blockStart, endIndex - blockStart).Trim();
        }
    }
}
