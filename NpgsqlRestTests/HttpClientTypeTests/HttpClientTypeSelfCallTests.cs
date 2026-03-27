namespace NpgsqlRestTests;

public static partial class Database
{
    public static void HttpClientTypeSelfCallTests()
    {
        script.Append(@"
        -- HTTP type with relative path (self-call to same server)
        create type http_self_call as (
            body text,
            status_code int,
            success bool
        );
        comment on type http_self_call is 'POST /api/hello-world';

        -- HTTP type that targets an endpoint with path parameters
        create type http_self_path_param as (
            body text,
            status_code int
        );
        comment on type http_self_path_param is 'GET /api/categories/5/products/10';

        create function get_http_self_path_param(
            req http_self_path_param
        )
        returns text
        language plpgsql
        as
        $$
        begin
            return (req).body;
        end;
        $$;

        -- HTTP type targeting a non-existent endpoint (404 case)
        create type http_self_not_found as (
            body text,
            status_code int
        );
        comment on type http_self_not_found is 'GET /api/does-not-exist';

        create function get_http_self_not_found(
            req http_self_not_found
        )
        returns int
        language plpgsql
        as
        $$
        begin
            return (req).status_code;
        end;
        $$;

        create function get_http_self_call(
            req http_self_call
        )
        returns text
        language plpgsql
        as
        $$
        begin
            return (req).body;
        end;
        $$;

        create function get_http_self_call_status(
            req http_self_call
        )
        returns int
        language plpgsql
        as
        $$
        begin
            return (req).status_code;
        end;
        $$;

        create function get_http_self_call_success(
            req http_self_call
        )
        returns bool
        language plpgsql
        as
        $$
        begin
            return (req).success;
        end;
        $$;
");
    }
}

[Collection("TestFixture")]
public class HttpClientTypeSelfCallTests(TestFixture test)
{
    [Fact]
    public async Task SelfCall_RelativePath_ReturnsBody()
    {
        // /api/hello-world returns 'Hello World' — the HTTP type calls it internally
        using var response = await test.Client.GetAsync("/api/get-http-self-call");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("Hello World");
    }

    [Fact]
    public async Task SelfCall_RelativePath_ReturnsStatusCode200()
    {
        using var response = await test.Client.GetAsync("/api/get-http-self-call-status");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("200");
    }

    [Fact]
    public async Task SelfCall_RelativePath_ReturnsSuccessTrue()
    {
        using var response = await test.Client.GetAsync("/api/get-http-self-call-success");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("t");
    }

    [Fact]
    public async Task SelfCall_PathParameter_ReturnsBody()
    {
        // Self-call to /api/categories/5/products/10 — endpoint has {_category_id} and {_product_id} path params
        using var response = await test.Client.GetAsync("/api/get-http-self-path-param");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Contain("Product Name");
    }

    [Fact]
    public async Task SelfCall_NonExistentEndpoint_Returns404StatusCode()
    {
        using var response = await test.Client.GetAsync("/api/get-http-self-not-found");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("404");
    }
}
