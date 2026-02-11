namespace NpgsqlRestTests;

public static partial class Database
{
    public static void PathParameterBasicTests()
    {
        script.Append(@"
create function get_product(p_id int) returns text language sql as 'select ''Product '' || $1';
comment on function get_product(int) is '
HTTP GET /products/{p_id}
';

create function get_product_review(p_id int, review_id int) returns text language sql as 'select ''Product '' || $1 || '' Review '' || $2';
comment on function get_product_review(int, int) is '
HTTP GET /products/{p_id}/reviews/{review_id}
';

create function get_user_by_name(username text) returns text language sql as 'select ''User: '' || $1';
comment on function get_user_by_name(text) is '
HTTP GET /users/{username}
';

create function update_product(p_id int, new_name text) returns text language sql as 'select ''Updated Product '' || $1 || '' to '' || $2';
comment on function update_product(int, text) is '
HTTP POST /products/{p_id}/update
';

create function delete_product(p_id int) returns text language sql as 'select ''Deleted Product '' || $1';
comment on function delete_product(int) is '
HTTP DELETE /products/{p_id}
';

create function get_resource_by_uuid(resource_id uuid) returns text language sql as 'select ''Resource: '' || $1::text';
comment on function get_resource_by_uuid(uuid) is '
HTTP GET /resources/{resource_id}
';

create function get_order(order_id int) returns text language sql as 'select ''Order '' || $1';
comment on function get_order(int) is '
HTTP GET
PATH /orders/{order_id}
';

create function get_large_entity(entity_id bigint) returns text language sql as 'select ''Entity '' || $1::text';
comment on function get_large_entity(bigint) is '
HTTP GET /entities/{entity_id}
';

create function get_product_json(p_id int) returns json language sql as 'select json_build_object(''id'', $1, ''name'', ''Product '' || $1)';
comment on function get_product_json(int) is '
HTTP GET /api/products/{p_id}/json
';

create function get_product_record(p_id int) returns table(id int, name text) language sql as 'select $1 as id, ''Product '' || $1 as name';
comment on function get_product_record(int) is '
HTTP GET /api/products/{p_id}/record
';
");
    }
}

[Collection("TestFixture")]
public class PathParameterBasicTests(TestFixture test)
{
    [Fact]
    public async Task Test_get_product()
    {
        using var response = await test.Client.GetAsync("/products/123");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("Product 123");
    }

    [Fact]
    public async Task Test_get_product_DifferentValues()
    {
        using var response1 = await test.Client.GetAsync("/products/1");
        (await response1.Content.ReadAsStringAsync()).Should().Be("Product 1");

        using var response2 = await test.Client.GetAsync("/products/999");
        (await response2.Content.ReadAsStringAsync()).Should().Be("Product 999");

        using var response3 = await test.Client.GetAsync("/products/0");
        (await response3.Content.ReadAsStringAsync()).Should().Be("Product 0");
    }

    [Fact]
    public async Task Test_get_product_InvalidValue_Returns404()
    {
        using var response = await test.Client.GetAsync("/products/not_a_number");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_get_product_NegativeValue()
    {
        using var response = await test.Client.GetAsync("/products/-5");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("Product -5");
    }

    [Fact]
    public async Task Test_get_product_review()
    {
        using var response = await test.Client.GetAsync("/products/5/reviews/10");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("Product 5 Review 10");
    }

    [Fact]
    public async Task Test_get_user_by_name()
    {
        using var response = await test.Client.GetAsync("/users/john_doe");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("User: john_doe");
    }

    [Fact]
    public async Task Test_get_user_by_name_NullLiteral()
    {
        // The string "null" should be passed as-is (not SQL NULL) with default null handling
        using var response = await test.Client.GetAsync("/users/null");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("User: null");
    }

    [Fact]
    public async Task Test_get_user_by_name_UrlEncoded()
    {
        // URL-encoded characters should be decoded
        using var response = await test.Client.GetAsync("/users/john%20doe");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("User: john doe");
    }

    [Fact]
    public async Task Test_update_product()
    {
        var content = new StringContent("{\"newName\": \"New Product Name\"}", System.Text.Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/products/7/update", content);
        var result = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Should().Be("Updated Product 7 to New Product Name");
    }

    [Fact]
    public async Task Test_delete_product()
    {
        using var response = await test.Client.DeleteAsync("/products/99");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("Deleted Product 99");
    }

    [Fact]
    public async Task Test_get_resource_by_uuid()
    {
        var uuid = "550e8400-e29b-41d4-a716-446655440000";
        using var response = await test.Client.GetAsync($"/resources/{uuid}");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be($"Resource: {uuid}");
    }

    [Fact]
    public async Task Test_get_order()
    {
        using var response = await test.Client.GetAsync("/orders/12345");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("Order 12345");
    }

    [Fact]
    public async Task Test_get_large_entity()
    {
        using var response = await test.Client.GetAsync("/entities/9223372036854775807");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("Entity 9223372036854775807");
    }

    [Fact]
    public async Task Test_get_product_json()
    {
        using var response = await test.Client.GetAsync("/api/products/42/json");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        content.Should().Contain("\"id\"");
        content.Should().Contain("42");
        content.Should().Contain("\"name\"");
        content.Should().Contain("Product 42");
    }

    [Fact]
    public async Task Test_get_product_record()
    {
        using var response = await test.Client.GetAsync("/api/products/55/record");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        content.Should().Contain("\"id\":55");
        content.Should().Contain("\"name\":\"Product 55\"");
    }
}
