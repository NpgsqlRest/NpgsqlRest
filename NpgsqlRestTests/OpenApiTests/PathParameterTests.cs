using System.Text.Json;
using System.Text.Json.Nodes;

namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void OpenApiPathParameterTests()
        {
            script.Append("""
create function openapi_get_product(_category_id int, _product_id int)
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
comment on function openapi_get_product(int, int) is '
HTTP GET /api/categories/{_category_id}/products/{_product_id}
';

create function openapi_get_product_with_query(_category_id int, _product_id int, include_reviews boolean default false)
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
comment on function openapi_get_product_with_query(int, int, boolean) is '
HTTP GET /api/categories/{_category_id}/products/{_product_id}/details
';

create function openapi_update_product(_category_id int, _product_id int, new_name text)
returns text
language sql
as $$
select 'Updated Product ' || _product_id || ' in Category ' || _category_id || ' to ' || new_name;
$$;
comment on function openapi_update_product(int, int, text) is '
HTTP POST /api/categories/{_category_id}/products/{_product_id}/update
';

create function openapi_single_path_param(p_id int)
returns text
language sql
as $$
select 'Product ' || p_id;
$$;
comment on function openapi_single_path_param(int) is '
HTTP GET /api/products/{p_id}
';

create function openapi_uuid_path_param(resource_id uuid)
returns text
language sql
as $$
select 'Resource: ' || resource_id::text;
$$;
comment on function openapi_uuid_path_param(uuid) is '
HTTP GET /api/resources/{resource_id}
';

create function openapi_text_path_param(username text)
returns text
language sql
as $$
select 'User: ' || username;
$$;
comment on function openapi_text_path_param(text) is '
HTTP GET /api/users/{username}
';
""");
        }
    }
}

namespace NpgsqlRestTests.OpenApiTests
{
    [Collection("TestFixture")]
    public class PathParameterTests
    {
        private JsonNode? _document;

        private JsonNode GetDocument()
        {
            if (_document != null)
                return _document;

            var filePath = Path.Combine(Setup.Program.OpenApiOutputPath, "openapi.json");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            _document = JsonNode.Parse(content);
            _document.Should().NotBeNull();
            return _document!;
        }

        /// <summary>
        /// Test that path parameters are documented with "in": "path" in OpenAPI spec.
        /// According to OpenAPI 3.0 specification, path parameters must be documented
        /// in the parameters array with "in": "path" and "required": true.
        /// </summary>
        [Fact]
        public void Test_SinglePathParameter_HasPathParameterDefinition()
        {
            var doc = GetDocument();

            // Get the path item for single path parameter endpoint
            var pathItem = doc["paths"]?["/api/products/{p_id}"];
            pathItem.Should().NotBeNull("Path /api/products/{p_id} should exist");

            var getOperation = pathItem?["get"];
            getOperation.Should().NotBeNull("GET operation should exist");

            var parameters = getOperation?["parameters"]?.AsArray();
            parameters.Should().NotBeNull("Parameters array should exist");

            // Should have a path parameter for p_id
            var pathParam = parameters?.FirstOrDefault(p =>
                p?["name"]?.GetValue<string>() == "pId" &&
                p?["in"]?.GetValue<string>() == "path");

            pathParam.Should().NotBeNull("Should have a path parameter named 'pId' (or 'p_id')");

            // Path parameters are always required in OpenAPI
            pathParam?["required"]?.GetValue<bool>().Should().BeTrue("Path parameters must be required");

            // Should have proper schema
            var schema = pathParam?["schema"];
            schema.Should().NotBeNull("Path parameter should have a schema");
            schema?["type"]?.GetValue<string>().Should().Be("integer", "p_id is an integer parameter");
        }

        /// <summary>
        /// Test that multiple path parameters are all documented correctly.
        /// </summary>
        [Fact]
        public void Test_MultiplePathParameters_AllDocumented()
        {
            var doc = GetDocument();

            var pathItem = doc["paths"]?["/api/categories/{_category_id}/products/{_product_id}"];
            pathItem.Should().NotBeNull("Path should exist");

            var getOperation = pathItem?["get"];
            getOperation.Should().NotBeNull("GET operation should exist");

            var parameters = getOperation?["parameters"]?.AsArray();
            parameters.Should().NotBeNull("Parameters array should exist");

            // Should have path parameters for both _category_id and _product_id
            var categoryParam = parameters?.FirstOrDefault(p =>
                p?["in"]?.GetValue<string>() == "path" &&
                (p?["name"]?.GetValue<string>() == "categoryId" || p?["name"]?.GetValue<string>() == "_category_id"));

            var productParam = parameters?.FirstOrDefault(p =>
                p?["in"]?.GetValue<string>() == "path" &&
                (p?["name"]?.GetValue<string>() == "productId" || p?["name"]?.GetValue<string>() == "_product_id"));

            categoryParam.Should().NotBeNull("Should have path parameter for category_id");
            productParam.Should().NotBeNull("Should have path parameter for product_id");

            // Both should be required
            categoryParam?["required"]?.GetValue<bool>().Should().BeTrue();
            productParam?["required"]?.GetValue<bool>().Should().BeTrue();

            // Both should be integers
            categoryParam?["schema"]?["type"]?.GetValue<string>().Should().Be("integer");
            productParam?["schema"]?["type"]?.GetValue<string>().Should().Be("integer");
        }

        /// <summary>
        /// Test that when an endpoint has both path and query parameters,
        /// they are documented separately with correct "in" values.
        /// </summary>
        [Fact]
        public void Test_MixedPathAndQueryParameters_ProperlyDistinguished()
        {
            var doc = GetDocument();

            var pathItem = doc["paths"]?["/api/categories/{_category_id}/products/{_product_id}/details"];
            pathItem.Should().NotBeNull("Path should exist");

            var getOperation = pathItem?["get"];
            getOperation.Should().NotBeNull("GET operation should exist");

            var parameters = getOperation?["parameters"]?.AsArray();
            parameters.Should().NotBeNull("Parameters array should exist");

            // Count path vs query parameters
            var pathParams = parameters?.Where(p => p?["in"]?.GetValue<string>() == "path").ToList();
            var queryParams = parameters?.Where(p => p?["in"]?.GetValue<string>() == "query").ToList();

            // Should have 2 path parameters (category_id and product_id)
            pathParams.Should().HaveCount(2, "Should have 2 path parameters");

            // Should have 1 query parameter (include_reviews)
            queryParams.Should().HaveCount(1, "Should have 1 query parameter");

            // The query parameter should be include_reviews
            var includeReviewsParam = queryParams?.FirstOrDefault();
            (includeReviewsParam?["name"]?.GetValue<string>() == "includeReviews" ||
             includeReviewsParam?["name"]?.GetValue<string>() == "include_reviews")
                .Should().BeTrue("Query parameter should be includeReviews");
            includeReviewsParam?["schema"]?["type"]?.GetValue<string>().Should().Be("boolean");

            // Query parameter with default value should not be required
            includeReviewsParam?["required"]?.GetValue<bool>().Should().BeFalse("Parameter with default should not be required");
        }

        /// <summary>
        /// Test that POST endpoints with path parameters have them in parameters,
        /// and body parameters in requestBody.
        /// </summary>
        [Fact]
        public void Test_PostWithPathParameters_SeparatedFromBody()
        {
            var doc = GetDocument();

            var pathItem = doc["paths"]?["/api/categories/{_category_id}/products/{_product_id}/update"];
            pathItem.Should().NotBeNull("Path should exist");

            var postOperation = pathItem?["post"];
            postOperation.Should().NotBeNull("POST operation should exist");

            // Should have path parameters
            var parameters = postOperation?["parameters"]?.AsArray();
            parameters.Should().NotBeNull("Parameters array should exist for path parameters");

            var pathParams = parameters?.Where(p => p?["in"]?.GetValue<string>() == "path").ToList();
            pathParams.Should().HaveCount(2, "Should have 2 path parameters");

            // Should have requestBody for the body parameter
            var requestBody = postOperation?["requestBody"];
            requestBody.Should().NotBeNull("Should have requestBody for POST");

            var bodyContent = requestBody?["content"]?["application/json"]?["schema"];
            bodyContent.Should().NotBeNull("Should have JSON schema in requestBody");

            // The body should only contain newName, not the path parameters
            var properties = bodyContent?["properties"];
            properties.Should().NotBeNull("Body schema should have properties");

            // newName should be in body
            (properties?["newName"] != null || properties?["new_name"] != null)
                .Should().BeTrue("Body should contain newName parameter");

            // Path parameters should NOT be in body
            properties?["categoryId"].Should().BeNull("Path parameter should not be in body");
            properties?["productId"].Should().BeNull("Path parameter should not be in body");
            properties?["_category_id"].Should().BeNull("Path parameter should not be in body");
            properties?["_product_id"].Should().BeNull("Path parameter should not be in body");
        }

        /// <summary>
        /// Test that UUID path parameters have correct schema type.
        /// </summary>
        [Fact]
        public void Test_UuidPathParameter_HasCorrectSchema()
        {
            var doc = GetDocument();

            var pathItem = doc["paths"]?["/api/resources/{resource_id}"];
            pathItem.Should().NotBeNull("Path should exist");

            var getOperation = pathItem?["get"];
            var parameters = getOperation?["parameters"]?.AsArray();

            var uuidParam = parameters?.FirstOrDefault(p => p?["in"]?.GetValue<string>() == "path");
            uuidParam.Should().NotBeNull("Should have path parameter");

            var schema = uuidParam?["schema"];
            schema?["type"]?.GetValue<string>().Should().Be("string", "UUID should be string type");
            schema?["format"]?.GetValue<string>().Should().Be("uuid", "UUID should have uuid format");
        }

        /// <summary>
        /// Test that text path parameters have correct schema type.
        /// </summary>
        [Fact]
        public void Test_TextPathParameter_HasCorrectSchema()
        {
            var doc = GetDocument();

            var pathItem = doc["paths"]?["/api/users/{username}"];
            pathItem.Should().NotBeNull("Path should exist");

            var getOperation = pathItem?["get"];
            var parameters = getOperation?["parameters"]?.AsArray();

            var textParam = parameters?.FirstOrDefault(p => p?["in"]?.GetValue<string>() == "path");
            textParam.Should().NotBeNull("Should have path parameter");

            var schema = textParam?["schema"];
            schema?["type"]?.GetValue<string>().Should().Be("string", "Text should be string type");
        }

        /// <summary>
        /// Verify the OpenAPI document is valid OpenAPI 3.0.3 format.
        /// </summary>
        [Fact]
        public void Test_DocumentStructure_IsValidOpenApi()
        {
            var doc = GetDocument();

            doc["openapi"]?.GetValue<string>().Should().Be("3.0.3", "Should be OpenAPI 3.0.3");
            doc["info"].Should().NotBeNull("Should have info section");
            doc["info"]?["title"].Should().NotBeNull("Should have title");
            doc["info"]?["version"].Should().NotBeNull("Should have version");
            doc["paths"].Should().NotBeNull("Should have paths section");
        }
    }
}
