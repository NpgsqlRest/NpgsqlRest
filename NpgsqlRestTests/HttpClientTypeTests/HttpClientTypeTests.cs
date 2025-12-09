using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Settings;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void HttpClientTypeTests()
    {
        script.Append($@"
        -- Test 1: Basic body response
        create type http_api_test1 as (
            body text
        );
        comment on type http_api_test1 is 'GET http://localhost:{WireMockFixture.Port}/api/test1';

        create function get_http_api_test1(
            req http_api_test1
        )
        returns text
        language plpgsql
        as
        $$
        begin
            return (req).body;
        end;
        $$;

        -- Test 2: Status code response (integer)
        create type http_api_status_code as (
            status_code int
        );
        comment on type http_api_status_code is 'GET http://localhost:{WireMockFixture.Port}/api/status-code';

        create function get_http_api_status_code(
            req http_api_status_code
        )
        returns int
        language plpgsql
        as
        $$
        begin
            return (req).status_code;
        end;
        $$;

        -- Test 3: Status code as text
        create type http_api_status_code_text as (
            status_code text
        );
        comment on type http_api_status_code_text is 'GET http://localhost:{WireMockFixture.Port}/api/status-code-text';

        create function get_http_api_status_code_text(
            req http_api_status_code_text
        )
        returns text
        language plpgsql
        as
        $$
        begin
            return (req).status_code;
        end;
        $$;

        -- Test 4: Content type response
        create type http_api_content_type as (
            content_type text
        );
        comment on type http_api_content_type is 'GET http://localhost:{WireMockFixture.Port}/api/content-type';

        create function get_http_api_content_type(
            req http_api_content_type
        )
        returns text
        language plpgsql
        as
        $$
        begin
            return (req).content_type;
        end;
        $$;

        -- Test 5: Success boolean response
        create type http_api_success as (
            success boolean
        );
        comment on type http_api_success is 'GET http://localhost:{WireMockFixture.Port}/api/success';

        create function get_http_api_success(
            req http_api_success
        )
        returns boolean
        language plpgsql
        as
        $$
        begin
            return (req).success;
        end;
        $$;

        -- Test 6: Headers response (JSON)
        create type http_api_headers as (
            headers json
        );
        comment on type http_api_headers is 'GET http://localhost:{WireMockFixture.Port}/api/headers';

        create function get_http_api_headers(
            req http_api_headers
        )
        returns json
        language plpgsql
        as
        $$
        begin
            return (req).headers;
        end;
        $$;

        -- Test 7: All response fields combined
        create type http_api_all_fields as (
            body text,
            status_code int,
            content_type text,
            headers json,
            success boolean,
            error_message text
        );
        comment on type http_api_all_fields is 'GET http://localhost:{WireMockFixture.Port}/api/all-fields';

        create function get_http_api_all_fields(
            req http_api_all_fields
        )
        returns json
        language plpgsql
        as
        $$
        begin
            return json_build_object(
                'body', (req).body,
                'status_code', (req).status_code,
                'content_type', (req).content_type,
                'headers', (req).headers,
                'success', (req).success,
                'error_message', (req).error_message
            );
        end;
        $$;

        -- Test 8: Error response (non-2xx status)
        create type http_api_error as (
            body text,
            status_code int,
            success boolean,
            error_message text
        );
        comment on type http_api_error is 'GET http://localhost:{WireMockFixture.Port}/api/error';

        create function get_http_api_error(
            req http_api_error
        )
        returns json
        language plpgsql
        as
        $$
        begin
            return json_build_object(
                'body', (req).body,
                'status_code', (req).status_code,
                'success', (req).success,
                'error_message', (req).error_message
            );
        end;
        $$;

        -- Test 9: POST with body
        create type http_api_post as (
            body text,
            status_code int
        );
        comment on type http_api_post is 'POST http://localhost:{WireMockFixture.Port}/api/post
Content-Type: application/json

{{""name"": ""test""}}';

        create function get_http_api_post(
            req http_api_post
        )
        returns json
        language plpgsql
        as
        $$
        begin
            return json_build_object(
                'body', (req).body,
                'status_code', (req).status_code
            );
        end;
        $$;

        -- Test 10: Empty response body
        create type http_api_empty_body as (
            body text,
            status_code int
        );
        comment on type http_api_empty_body is 'GET http://localhost:{WireMockFixture.Port}/api/empty-body';

        create function get_http_api_empty_body(
            req http_api_empty_body
        )
        returns json
        language plpgsql
        as
        $$
        begin
            return json_build_object(
                'body', (req).body,
                'status_code', (req).status_code
            );
        end;
        $$;

        -- Test 11: Response with unicode characters
        create type http_api_unicode as (
            body text,
            status_code int
        );
        comment on type http_api_unicode is 'GET http://localhost:{WireMockFixture.Port}/api/unicode';

        create function get_http_api_unicode(
            req http_api_unicode
        )
        returns text
        language plpgsql
        as
        $$
        begin
            return (req).body;
        end;
        $$;

        -- Test 12: Response with special characters in JSON
        create type http_api_special_chars as (
            body json,
            status_code int
        );
        comment on type http_api_special_chars is 'GET http://localhost:{WireMockFixture.Port}/api/special-chars';

        create function get_http_api_special_chars(
            req http_api_special_chars
        )
        returns table (
            body json,
            status_code int
        )
        language plpgsql
        as
        $$
        begin
            return query select (req).body, (req).status_code;
        end;
        $$;

        -- Test 13: Multiple headers with same prefix
        create type http_api_multi_headers as (
            headers json,
            status_code int
        );
        comment on type http_api_multi_headers is 'GET http://localhost:{WireMockFixture.Port}/api/multi-headers';

        create function get_http_api_multi_headers(
            req http_api_multi_headers
        )
        returns json
        language plpgsql
        as
        $$
        begin
            return (req).headers;
        end;
        $$;

        -- Test 14: PUT method
        create type http_api_put as (
            body text,
            status_code int
        );
        comment on type http_api_put is 'PUT http://localhost:{WireMockFixture.Port}/api/put
Content-Type: application/json

{{""id"": 1, ""name"": ""updated""}}';

        create function get_http_api_put(
            req http_api_put
        )
        returns json
        language plpgsql
        as
        $$
        begin
            return json_build_object(
                'body', (req).body,
                'status_code', (req).status_code
            );
        end;
        $$;

        -- Test 15: PATCH method
        create type http_api_patch as (
            body text,
            status_code int
        );
        comment on type http_api_patch is 'PATCH http://localhost:{WireMockFixture.Port}/api/patch
Content-Type: application/json

{{""name"": ""patched""}}';

        create function get_http_api_patch(
            req http_api_patch
        )
        returns json
        language plpgsql
        as
        $$
        begin
            return json_build_object(
                'body', (req).body,
                'status_code', (req).status_code
            );
        end;
        $$;

        -- Test 16: DELETE method
        create type http_api_delete as (
            body text,
            status_code int,
            success boolean
        );
        comment on type http_api_delete is 'DELETE http://localhost:{WireMockFixture.Port}/api/delete/123';

        create function get_http_api_delete(
            req http_api_delete
        )
        returns json
        language plpgsql
        as
        $$
        begin
            return json_build_object(
                'body', (req).body,
                'status_code', (req).status_code,
                'success', (req).success
            );
        end;
        $$;

        -- Test 17: Multiple HTTP client types in one function (parallel requests)
        create type http_api_users as (
            body json,
            status_code int
        );
        comment on type http_api_users is 'GET http://localhost:{WireMockFixture.Port}/api/users';

        create type http_api_products as (
            body json,
            status_code int
        );
        comment on type http_api_products is 'GET http://localhost:{WireMockFixture.Port}/api/products';

        create function get_http_api_multi_types(
            _users http_api_users,
            _products http_api_products
        )
        returns json
        language plpgsql
        as
        $$
        begin
            return json_build_object(
                'users', (_users).body,
                'users_status', (_users).status_code,
                'products', (_products).body,
                'products_status', (_products).status_code
            );
        end;
        $$;

        -- Test 18: Three HTTP client types with mixed parameters
        create type http_api_service_a as (
            body json,
            status_code int,
            success boolean
        );
        comment on type http_api_service_a is 'GET http://localhost:{WireMockFixture.Port}/api/service-a';

        create type http_api_service_b as (
            body json,
            status_code int,
            success boolean
        );
        comment on type http_api_service_b is 'GET http://localhost:{WireMockFixture.Port}/api/service-b';

        create type http_api_service_c as (
            body json,
            status_code int,
            success boolean
        );
        comment on type http_api_service_c is 'GET http://localhost:{WireMockFixture.Port}/api/service-c';

        create function get_http_api_three_services(
            _prefix text,
            _a http_api_service_a,
            _b http_api_service_b,
            _suffix text,
            _c http_api_service_c
        )
        returns json
        language plpgsql
        as
        $$
        begin
            return json_build_object(
                'prefix', _prefix,
                'suffix', _suffix,
                'a_body', (_a).body,
                'a_success', (_a).success,
                'b_body', (_b).body,
                'b_success', (_b).success,
                'c_body', (_c).body,
                'c_success', (_c).success
            );
        end;
        $$;
");
    }
}

public class WireMockFixture : IDisposable
{
    public const int Port = 50953;
    public WireMockServer Server { get; }

    public WireMockFixture() => Server = WireMockServer.Start(new WireMockServerSettings { Port = Port });
    public void Dispose() => Server.Stop();
}

[Collection("TestFixture")]
public class HttpClientTypeTests : IClassFixture<WireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public HttpClientTypeTests(TestFixture test, WireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_get_http_api_test1_body_response()
    {
        _server
            .Given(Request.Create().WithPath("/api/test1").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("http api test1 response 1"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-test1/?reqBody=http_api_test1");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("http api test1 response 1");
    }

    [Fact]
    public async Task Test_get_http_api_test1_body_response_without_param()
    {
        _server
            .Given(Request.Create().WithPath("/api/test1").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("http api test1 response 2"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-test1/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("http api test1 response 2");
    }

    [Fact]
    public async Task Test_status_code_as_integer()
    {
        _server
            .Given(Request.Create().WithPath("/api/status-code").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithBody("created"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-status-code/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("201");
    }

    [Fact]
    public async Task Test_status_code_as_text()
    {
        _server
            .Given(Request.Create().WithPath("/api/status-code-text").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(202)
                .WithBody("accepted"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-status-code-text/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("202");
    }

    [Fact]
    public async Task Test_content_type_response()
    {
        _server
            .Given(Request.Create().WithPath("/api/content-type").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json; charset=utf-8")
                .WithBody("{}"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-content-type/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("application/json; charset=utf-8");
    }

    [Fact]
    public async Task Test_success_true_for_2xx()
    {
        _server
            .Given(Request.Create().WithPath("/api/success").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("ok"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-success/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("t"); // PostgreSQL returns 't' for true
    }

    [Fact]
    public async Task Test_headers_as_json()
    {
        _server
            .Given(Request.Create().WithPath("/api/headers").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("X-Custom-Header", "custom-value")
                .WithHeader("X-Another-Header", "another-value")
                .WithBody("ok"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-headers/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("\"X-Custom-Header\"");
        content.Should().Contain("\"custom-value\"");
        content.Should().Contain("\"X-Another-Header\"");
        content.Should().Contain("\"another-value\"");
    }

    [Fact]
    public async Task Test_all_fields_combined()
    {
        _server
            .Given(Request.Create().WithPath("/api/all-fields").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "text/plain")
                .WithHeader("X-Test", "test-value")
                .WithBody("response body"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-all-fields/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("\"body\" : \"response body\"");
        content.Should().Contain("\"status_code\" : 200");
        content.Should().Contain("\"content_type\" : \"text/plain\"");
        content.Should().Contain("\"success\" : true");
        content.Should().Contain("\"error_message\" : null");
    }

    [Fact]
    public async Task Test_error_response_4xx()
    {
        _server
            .Given(Request.Create().WithPath("/api/error").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(404)
                .WithBody("not found"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-error/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("\"status_code\" : 404");
        content.Should().Contain("\"success\" : false");
        content.Should().Contain("\"body\" : \"not found\"");
    }

    [Fact]
    public async Task Test_error_response_5xx()
    {
        _server
            .Given(Request.Create().WithPath("/api/error").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBody("internal server error"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-error/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("\"status_code\" : 500");
        content.Should().Contain("\"success\" : false");
    }

    [Fact]
    public async Task Test_post_with_body()
    {
        _server
            .Given(Request.Create()
                .WithPath("/api/post")
                .UsingPost()
                .WithBody("{\"name\": \"test\"}"))
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithBody("{\"id\": 1, \"name\": \"test\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-post/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("\"status_code\" : 201");
        content.Should().Contain("\\\"id\\\""); // Body is JSON escaped inside the outer JSON
    }

    [Fact]
    public async Task Test_status_code_for_different_values()
    {
        _server
            .Given(Request.Create().WithPath("/api/status-code").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(204)
                .WithBody(""));

        using var response = await _test.Client.GetAsync("/api/get-http-api-status-code/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("204");
    }

    [Fact]
    public async Task Test_get_http_api_empty_body()
    {
        _server
            .Given(Request.Create().WithPath("/api/empty-body").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody(""));

        using var response = await _test.Client.GetAsync("/api/get-http-api-empty-body/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("\"body\" : \"\"");
        content.Should().Contain("\"status_code\" : 200");
    }

    [Fact]
    public async Task Test_get_http_api_unicode()
    {
        _server
            .Given(Request.Create().WithPath("/api/unicode").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("Hello 世界 مرحبا 🌍"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-unicode/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("Hello 世界 مرحبا 🌍");
    }

    [Fact]
    public async Task Test_get_http_api_special_chars()
    {
        _server
            .Given(Request.Create().WithPath("/api/special-chars").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"message\": \"Hello\\nWorld\\t!\", \"path\": \"C:\\\\Users\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-special-chars/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("Hello\\nWorld\\t!");
    }

    [Fact]
    public async Task Test_get_http_api_multi_headers()
    {
        _server
            .Given(Request.Create().WithPath("/api/multi-headers").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("X-Rate-Limit", "100")
                .WithHeader("X-Rate-Limit-Remaining", "99")
                .WithHeader("X-Rate-Limit-Reset", "1234567890")
                .WithBody("ok"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-multi-headers/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("\"X-Rate-Limit\"");
        content.Should().Contain("\"100\"");
        content.Should().Contain("\"X-Rate-Limit-Remaining\"");
        content.Should().Contain("\"99\"");
    }

    [Fact]
    public async Task Test_get_http_api_put()
    {
        _server
            .Given(Request.Create()
                .WithPath("/api/put")
                .UsingPut()
                .WithBody("{\"id\": 1, \"name\": \"updated\"}"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"id\": 1, \"name\": \"updated\", \"updatedAt\": \"2024-01-01\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-put/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("\"status_code\" : 200");
        content.Should().Contain("updatedAt");
    }

    [Fact]
    public async Task Test_get_http_api_patch()
    {
        _server
            .Given(Request.Create()
                .WithPath("/api/patch")
                .UsingPatch()
                .WithBody("{\"name\": \"patched\"}"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"name\": \"patched\", \"patchedAt\": \"2024-01-01\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-patch/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("\"status_code\" : 200");
        content.Should().Contain("patchedAt");
    }

    [Fact]
    public async Task Test_get_http_api_delete()
    {
        _server
            .Given(Request.Create()
                .WithPath("/api/delete/123")
                .UsingDelete())
            .RespondWith(Response.Create()
                .WithStatusCode(204)
                .WithBody(""));

        using var response = await _test.Client.GetAsync("/api/get-http-api-delete/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("\"status_code\" : 204");
        content.Should().Contain("\"success\" : true");
    }

    [Fact]
    public async Task Test_get_http_api_multi_types()
    {
        _server
            .Given(Request.Create().WithPath("/api/users").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("[{\"id\": 1, \"name\": \"User1\"}]"));

        _server
            .Given(Request.Create().WithPath("/api/products").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("[{\"id\": 101, \"name\": \"Product1\"}]"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-multi-types/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("\"users_status\" : 200");
        content.Should().Contain("\"products_status\" : 200");
        content.Should().Contain("User1");
        content.Should().Contain("Product1");
    }

    [Fact]
    public async Task Test_get_http_api_three_services()
    {
        _server
            .Given(Request.Create().WithPath("/api/service-a").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"service\": \"A\"}"));

        _server
            .Given(Request.Create().WithPath("/api/service-b").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"service\": \"B\"}"));

        _server
            .Given(Request.Create().WithPath("/api/service-c").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"service\": \"C\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-three-services/?prefix=start&suffix=end");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("\"prefix\" : \"start\"");
        content.Should().Contain("\"suffix\" : \"end\"");
        content.Should().Contain("\"a_success\" : true");
        content.Should().Contain("\"b_success\" : true");
        content.Should().Contain("\"c_success\" : true");
        content.Should().Contain("service");
    }

    [Fact]
    public async Task Test_get_http_api_multi_types_one_fails()
    {
        _server
            .Given(Request.Create().WithPath("/api/users").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("[{\"id\": 1}]"));

        _server
            .Given(Request.Create().WithPath("/api/products").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBody("{\"error\": \"Internal Server Error\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-multi-types/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("\"users_status\" : 200");
        content.Should().Contain("\"products_status\" : 500");
    }
}
