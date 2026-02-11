namespace NpgsqlRestTests;

public static partial class Database
{
    public static void PathParameterDefaultTests()
    {
        script.Append(@"
-- Path param with mixed query string default
create function get_product_details(p_id int, include_reviews boolean default false) returns text language sql as 'select ''Product '' || $1 || '' Include Reviews: '' || $2';
comment on function get_product_details(int, boolean) is '
HTTP GET /products/{p_id}/details
';

-- Multiple path params with query string default
create function search_category_products(cat_id int, subcat_id int, query text default null) returns text
language sql as 'select ''Category '' || $1 || '' SubCategory '' || $2 || '' Query: '' || coalesce($3, ''none'')';
comment on function search_category_products(int, int, text) is '
HTTP GET /categories/{cat_id}/subcategories/{subcat_id}/products
';

-- Path param with default value (default is useless without ? syntax)
create function get_item_with_default(p_id int default 42) returns text language sql as 'select ''Item '' || $1';
comment on function get_item_with_default(int) is '
HTTP GET /items-default/{p_id}
';

-- Path param + multiple default query params
create function get_filtered_item(p_id int, status text default 'active', page int default 1) returns text
language sql as 'select ''Item '' || $1 || '' Status: '' || $2 || '' Page: '' || $3';
comment on function get_filtered_item(int, text, int) is '
HTTP GET /items-filtered/{p_id}
';

-- Optional path parameter with {p_id?} syntax
create function get_optional_item(p_id int default 42) returns text language sql as 'select ''OptItem '' || $1';
comment on function get_optional_item(int) is '
HTTP GET /optional-items/{p_id?}
';

-- Null handling: text path param with null_literal handling
create function get_user_null_literal(username text) returns text language sql as 'select coalesce($1, ''<NULL>'')';
comment on function get_user_null_literal(text) is '
HTTP GET /users-null-literal/{username}
query_string_null_handling null_literal
';

-- Null handling: int path param with null_literal handling
create function get_item_null_literal(p_id int default null) returns text language sql as 'select coalesce($1::text, ''<NULL>'')';
comment on function get_item_null_literal(int) is '
HTTP GET /items-null-literal/{p_id}
query_string_null_handling null_literal
';

-- Null handling: text path param with empty_string handling
create function get_user_empty_string(username text) returns text language sql as 'select coalesce($1, ''<NULL>'')';
comment on function get_user_empty_string(text) is '
HTTP GET /users-empty-string/{username}
query_string_null_handling empty_string
';
");
    }
}

[Collection("TestFixture")]
public class PathParameterDefaultTests(TestFixture test)
{
    [Fact]
    public async Task Test_get_product_details_DefaultUsed()
    {
        using var response = await test.Client.GetAsync("/products/42/details");
        (await response.Content.ReadAsStringAsync()).Should().Be("Product 42 Include Reviews: false");
    }

    [Fact]
    public async Task Test_get_product_details_QueryOverride()
    {
        using var response = await test.Client.GetAsync("/products/42/details?includeReviews=true");
        (await response.Content.ReadAsStringAsync()).Should().Be("Product 42 Include Reviews: true");
    }

    [Fact]
    public async Task Test_search_category_products_NoQuery()
    {
        using var response = await test.Client.GetAsync("/categories/1/subcategories/2/products");
        (await response.Content.ReadAsStringAsync()).Should().Be("Category 1 SubCategory 2 Query: none");
    }

    [Fact]
    public async Task Test_search_category_products_WithQuery()
    {
        using var response = await test.Client.GetAsync("/categories/1/subcategories/2/products?query=laptop");
        (await response.Content.ReadAsStringAsync()).Should().Be("Category 1 SubCategory 2 Query: laptop");
    }

    [Fact]
    public async Task Test_get_item_with_default()
    {
        // Default on path param is useless without ? syntax - the path always provides a value
        using var response = await test.Client.GetAsync("/items-default/5");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("Item 5");
    }

    [Fact]
    public async Task Test_get_filtered_item_AllDefaults()
    {
        // Path param provided, both query string params use defaults
        using var response = await test.Client.GetAsync("/items-filtered/5");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("Item 5 Status: active Page: 1");
    }

    [Fact]
    public async Task Test_get_filtered_item_PartialOverride()
    {
        using var response = await test.Client.GetAsync("/items-filtered/5?status=archived");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("Item 5 Status: archived Page: 1");
    }

    [Fact]
    public async Task Test_get_filtered_item_AllOverridden()
    {
        using var response = await test.Client.GetAsync("/items-filtered/5?status=archived&page=3");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("Item 5 Status: archived Page: 3");
    }

    [Fact]
    public async Task Test_get_optional_item_WithValue()
    {
        // Optional path param with value provided
        using var response = await test.Client.GetAsync("/optional-items/7");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("OptItem 7");
    }

    [Fact]
    public async Task Test_get_optional_item_WithoutValue()
    {
        // Optional path param omitted - should use the PostgreSQL default (42)
        using var response = await test.Client.GetAsync("/optional-items/");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("OptItem 42");
    }

    [Fact]
    public async Task Test_get_user_null_literal_WithValue()
    {
        // Normal value should work as usual
        using var response = await test.Client.GetAsync("/users-null-literal/john");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("john");
    }

    [Fact]
    public async Task Test_get_user_null_literal_NullString()
    {
        // With null_literal handling, passing "null" converts to SQL NULL
        using var response = await test.Client.GetAsync("/users-null-literal/null");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("<NULL>");
    }

    [Fact]
    public async Task Test_get_item_null_literal_WithValue()
    {
        // Normal int value should work
        using var response = await test.Client.GetAsync("/items-null-literal/5");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("5");
    }

    [Fact]
    public async Task Test_get_item_null_literal_NullString()
    {
        // With null_literal handling, passing "null" converts to SQL NULL for int param too
        using var response = await test.Client.GetAsync("/items-null-literal/null");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("<NULL>");
    }

    [Fact]
    public async Task Test_get_user_empty_string_WithValue()
    {
        // Normal value should work as usual
        using var response = await test.Client.GetAsync("/users-empty-string/john");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("john");
    }

    [Fact]
    public async Task Test_get_user_empty_string_NullLiteralNotConverted()
    {
        // With empty_string handling, "null" is NOT converted - it stays as the literal string
        using var response = await test.Client.GetAsync("/users-empty-string/null");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("null");
    }
}
