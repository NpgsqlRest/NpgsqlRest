using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void HttpClientTypePlaceholderTests()
    {
        script.Append($@"
        -- Test placeholder substitution in URL
        create type http_api_placeholder as (
            body json,
            status_code int
        );
        comment on type http_api_placeholder is 'GET http://localhost:{WireMockFixture.Port}/api/users/{{_user_id}}';

        create function get_http_api_placeholder(
            _user_id int,
            _req http_api_placeholder
        )
        returns table (
            body json,
            status_code int
        )
        language plpgsql
        as
        $$
        begin
            return query select (_req).body, (_req).status_code;
        end;
        $$;

        -- Test placeholder substitution in URL and headers
        create type http_api_placeholder_header as (
            body json,
            status_code int
        );
        comment on type http_api_placeholder_header is 'GET http://localhost:{WireMockFixture.Port}/api/secure/{{_resource}}
Authorization: Bearer {{_token}}';

        create function get_http_api_placeholder_header(
            _resource text,
            _token text,
            _req http_api_placeholder_header
        )
        returns table (
            body json,
            status_code int
        )
        language plpgsql
        as
        $$
        begin
            return query select (_req).body, (_req).status_code;
        end;
        $$;

        -- Test placeholder substitution in POST body
        create type http_api_placeholder_body as (
            body json,
            status_code int
        );
        comment on type http_api_placeholder_body is 'POST http://localhost:{WireMockFixture.Port}/api/create
Content-Type: application/json

{{""name"": ""{{_name}}"", ""email"": ""{{_email}}""}}';

        create function get_http_api_placeholder_body(
            _name text,
            _email text,
            _req http_api_placeholder_body
        )
        returns table (
            body json,
            status_code int
        )
        language plpgsql
        as
        $$
        begin
            return query select (_req).body, (_req).status_code;
        end;
        $$;

        -- Test multiple placeholders in URL (query string)
        create type http_api_placeholder_query as (
            body json,
            status_code int
        );
        comment on type http_api_placeholder_query is 'GET http://localhost:{WireMockFixture.Port}/api/search?q={{_query}}&limit={{_limit}}';

        create function get_http_api_placeholder_query(
            _query text,
            _limit int,
            _req http_api_placeholder_query
        )
        returns table (
            body json,
            status_code int
        )
        language plpgsql
        as
        $$
        begin
            return query select (_req).body, (_req).status_code;
        end;
        $$;

        -- Test placeholder from HTTP type field (custom type field as placeholder)
        create type http_api_type_field_placeholder as (
            user_id int,
            body json,
            status_code int
        );
        comment on type http_api_type_field_placeholder is 'GET http://localhost:{WireMockFixture.Port}/api/users/{{user_id}}';

        create function get_http_api_type_field_placeholder(
            _req http_api_type_field_placeholder
        )
        returns table (
            body json,
            status_code int
        )
        language plpgsql
        as
        $$
        begin
            return query select (_req).body, (_req).status_code;
        end;
        $$;

        -- Test multiple placeholders from HTTP type fields
        create type http_api_type_multi_field as (
            tenant_id text,
            resource_id int,
            body json,
            status_code int
        );
        comment on type http_api_type_multi_field is 'GET http://localhost:{WireMockFixture.Port}/api/tenants/{{tenant_id}}/resources/{{resource_id}}';

        create function get_http_api_type_multi_field(
            _req http_api_type_multi_field
        )
        returns table (
            body json,
            status_code int
        )
        language plpgsql
        as
        $$
        begin
            return query select (_req).body, (_req).status_code;
        end;
        $$;

        -- Test placeholder from HTTP type field in header
        create type http_api_type_field_header as (
            api_key text,
            body json,
            status_code int
        );
        comment on type http_api_type_field_header is 'GET http://localhost:{WireMockFixture.Port}/api/protected
X-Api-Key: {{api_key}}';

        create function get_http_api_type_field_header(
            _req http_api_type_field_header
        )
        returns table (
            body json,
            status_code int
        )
        language plpgsql
        as
        $$
        begin
            return query select (_req).body, (_req).status_code;
        end;
        $$;

        -- Test placeholder from HTTP type field in POST body
        create type http_api_type_field_body as (
            username text,
            password text,
            body json,
            status_code int
        );
        comment on type http_api_type_field_body is 'POST http://localhost:{WireMockFixture.Port}/api/login
Content-Type: application/json

{{""username"": ""{{username}}"", ""password"": ""{{password}}""}}';

        create function get_http_api_type_field_body(
            _req http_api_type_field_body
        )
        returns table (
            body json,
            status_code int
        )
        language plpgsql
        as
        $$
        begin
            return query select (_req).body, (_req).status_code;
        end;
        $$;

        -- Test mixed placeholders: regular parameter + HTTP type field
        create type http_api_mixed_placeholder as (
            resource_type text,
            body json,
            status_code int
        );
        comment on type http_api_mixed_placeholder is 'GET http://localhost:{WireMockFixture.Port}/api/{{_tenant}}/{{resource_type}}/{{_id}}';

        create function get_http_api_mixed_placeholder(
            _tenant text,
            _id int,
            _req http_api_mixed_placeholder
        )
        returns table (
            body json,
            status_code int
        )
        language plpgsql
        as
        $$
        begin
            return query select (_req).body, (_req).status_code;
        end;
        $$;
");
    }
}

[Collection("TestFixture")]
public class HttpClientTypePlaceholderTests : IClassFixture<WireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public HttpClientTypePlaceholderTests(TestFixture test, WireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_get_http_api_placeholder()
    {
        _server
            .Given(Request.Create().WithPath("/api/users/123").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"id\": 123, \"name\": \"John\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-placeholder/?userId=123");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"body\":{\"id\": 123, \"name\": \"John\"},\"statusCode\":200}]");
    }

    [Fact]
    public async Task Test_get_http_api_placeholder_header()
    {
        _server
            .Given(Request.Create()
                .WithPath("/api/secure/documents")
                .WithHeader("Authorization", "Bearer secret-token-123")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"documents\": []}"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-placeholder-header/?resource=documents&token=secret-token-123");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"body\":{\"documents\": []},\"statusCode\":200}]");
    }

    [Fact]
    public async Task Test_get_http_api_placeholder_body()
    {
        _server
            .Given(Request.Create()
                .WithPath("/api/create")
                .WithBody("{\"name\": \"Alice\", \"email\": \"alice@example.com\"}")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithBody("{\"id\": 1, \"name\": \"Alice\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-placeholder-body/?name=Alice&email=alice@example.com");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"body\":{\"id\": 1, \"name\": \"Alice\"},\"statusCode\":201}]");
    }

    [Fact]
    public async Task Test_get_http_api_placeholder_query()
    {
        _server
            .Given(Request.Create()
                .WithPath("/api/search")
                .WithParam("q", "test")
                .WithParam("limit", "10")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"results\": [], \"total\": 0}"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-placeholder-query/?query=test&limit=10");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"body\":{\"results\": [], \"total\": 0},\"statusCode\":200}]");
    }

    [Fact]
    public async Task Test_get_http_api_placeholder_different_user()
    {
        _server
            .Given(Request.Create().WithPath("/api/users/456").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"id\": 456, \"name\": \"Jane\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-placeholder/?userId=456");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"body\":{\"id\": 456, \"name\": \"Jane\"},\"statusCode\":200}]");
    }

    [Fact]
    public async Task Test_get_http_api_type_field_placeholder()
    {
        _server
            .Given(Request.Create().WithPath("/api/users/789").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"id\": 789, \"name\": \"Bob\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-type-field-placeholder/?reqUserId=789");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"body\":{\"id\": 789, \"name\": \"Bob\"},\"statusCode\":200}]");
    }

    [Fact]
    public async Task Test_get_http_api_type_multi_field()
    {
        _server
            .Given(Request.Create().WithPath("/api/tenants/acme/resources/42").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"tenant\": \"acme\", \"resourceId\": 42}"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-type-multi-field/?reqTenantId=acme&reqResourceId=42");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"body\":{\"tenant\": \"acme\", \"resourceId\": 42},\"statusCode\":200}]");
    }

    [Fact]
    public async Task Test_get_http_api_type_field_header()
    {
        _server
            .Given(Request.Create()
                .WithPath("/api/protected")
                .WithHeader("X-Api-Key", "secret-api-key-123")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"access\": \"granted\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-type-field-header/?reqApiKey=secret-api-key-123");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"body\":{\"access\": \"granted\"},\"statusCode\":200}]");
    }

    [Fact]
    public async Task Test_get_http_api_type_field_body()
    {
        _server
            .Given(Request.Create()
                .WithPath("/api/login")
                .WithBody("{\"username\": \"admin\", \"password\": \"secret123\"}")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"token\": \"jwt-token-here\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-type-field-body/?reqUsername=admin&reqPassword=secret123");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"body\":{\"token\": \"jwt-token-here\"},\"statusCode\":200}]");
    }

    [Fact]
    public async Task Test_get_http_api_mixed_placeholder()
    {
        _server
            .Given(Request.Create().WithPath("/api/corp/documents/999").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"tenant\": \"corp\", \"type\": \"documents\", \"id\": 999}"));

        using var response = await _test.Client.GetAsync("/api/get-http-api-mixed-placeholder/?tenant=corp&reqResourceType=documents&id=999");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("[{\"body\":{\"tenant\": \"corp\", \"type\": \"documents\", \"id\": 999},\"statusCode\":200}]");
    }
}
