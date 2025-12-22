using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyWithUserParamsTest()
    {
        script.Append(@"
create function proxy_with_user_params_login()
returns table (
    name_identifier int,
    name text,
    role text[]
)
language sql as $$
select
    999 as name_identifier,
    'proxyuser' as name,
    array['proxyrole'] as role
$$;
comment on function proxy_with_user_params_login() is 'login';

create function proxy_with_user_params(
    _user_id text default null,
    _user_name text default null,
    _proxy_status_code int default null,
    _proxy_body text default null
)
returns json
language plpgsql
as
$$
begin
    return json_build_object(
        'user_id', _user_id,
        'user_name', _user_name,
        'proxy_status', _proxy_status_code,
        'proxy_body', _proxy_body
    );
end;
$$;
comment on function proxy_with_user_params(text, text, int, text) is 'HTTP GET
authorize
user_params
proxy';

create function proxy_with_ip_and_claims(
    _ip_address text default null,
    _user_claims json default null,
    _proxy_status_code int default null,
    _proxy_body text default null
)
returns json
language plpgsql
as
$$
begin
    return json_build_object(
        'ip_address', _ip_address,
        'user_claims', _user_claims,
        'proxy_status', _proxy_status_code,
        'proxy_body', _proxy_body
    );
end;
$$;
comment on function proxy_with_ip_and_claims(text, json, int, text) is 'HTTP GET
authorize
user_params
proxy';

create function proxy_with_user_context(
    _proxy_status_code int default null,
    _proxy_body text default null
)
returns json
language plpgsql
as
$$
begin
    return json_build_object(
        'proxy_status', _proxy_status_code,
        'proxy_body', _proxy_body
    );
end;
$$;
comment on function proxy_with_user_context(int, text) is 'HTTP GET
authorize
user_context
proxy';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyWithUserParamsTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyWithUserParamsTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_with_user_params_unauthorized()
    {
        // Without login, should return 401 Unauthorized
        using var response = await _test.Client.GetAsync("/api/proxy-with-user-params/");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Test_proxy_with_user_params_authorized()
    {
        // Setup WireMock to expect user claim params in query string
        _server
            .Given(Request.Create()
                .WithPath("/api/proxy-with-user-params/")
                .WithParam("userId", "999")
                .WithParam("userName", "proxyuser")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("proxy response data"));

        // Create a new client to maintain cookies
        using var client = _test.Application.CreateClient();

        // Login first
        using var login = await client.PostAsync("/api/proxy-with-user-params-login/", null);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        // Now call the proxy endpoint
        using var response = await client.GetAsync("/api/proxy-with-user-params/");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Should contain user claims AND proxy response data
        content.Should().Contain("\"user_id\" : \"999\"");
        content.Should().Contain("\"user_name\" : \"proxyuser\"");
        content.Should().Contain("\"proxy_status\" : 200");
        content.Should().Contain("\"proxy_body\" : \"proxy response data\"");
    }

    [Fact]
    public async Task Test_proxy_with_ip_and_claims()
    {
        // Setup WireMock to expect IP address and user claims in query string
        // Note: ipAddress may be null in test environment, userClaims will be a JSON string
        _server
            .Given(Request.Create()
                .WithPath("/api/proxy-with-ip-and-claims/")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("ip and claims response"));

        // Create a new client to maintain cookies
        using var client = _test.Application.CreateClient();

        // Login first
        using var login = await client.PostAsync("/api/proxy-with-user-params-login/", null);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        // Now call the proxy endpoint
        using var response = await client.GetAsync("/api/proxy-with-ip-and-claims/");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Should contain proxy response data
        content.Should().Contain("\"proxy_status\" : 200");
        content.Should().Contain("\"proxy_body\" : \"ip and claims response\"");

        // Verify the proxy received the userClaims parameter by checking WireMock logs
        var requests = _server.LogEntries;
        var proxyRequest = requests.FirstOrDefault(r => r.RequestMessage.Path == "/api/proxy-with-ip-and-claims/");
        proxyRequest.Should().NotBeNull();

        // Check that userClaims parameter was forwarded (URL encoded JSON)
        proxyRequest!.RequestMessage.Query.Should().ContainKey("userClaims");
        var userClaimsValue = proxyRequest.RequestMessage.Query["userClaims"].First();
        userClaimsValue.Should().Contain("name_identifier");
        userClaimsValue.Should().Contain("999");
        userClaimsValue.Should().Contain("proxyuser");

        // Note: ipAddress may be null in test environment (no RemoteIpAddress set)
        // When available, it would be forwarded as "ipAddress" query parameter
    }

    [Fact]
    public async Task Test_proxy_with_user_context_headers()
    {
        // Setup WireMock to capture request headers
        _server
            .Given(Request.Create()
                .WithPath("/api/proxy-with-user-context/")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("user context response"));

        // Create a new client to maintain cookies
        using var client = _test.Application.CreateClient();

        // Login first
        using var login = await client.PostAsync("/api/proxy-with-user-params-login/", null);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        // Now call the proxy endpoint with user_context
        using var response = await client.GetAsync("/api/proxy-with-user-context/");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("\"proxy_status\" : 200");
        content.Should().Contain("\"proxy_body\" : \"user context response\"");

        // Verify the proxy received the user context as headers
        var requests = _server.LogEntries;
        var proxyRequest = requests.FirstOrDefault(r => r.RequestMessage.Path == "/api/proxy-with-user-context/");
        proxyRequest.Should().NotBeNull();

        // Check that user context headers were forwarded (using ContextKeyClaimsMapping defaults)
        // Default mappings: request.user_id -> user_id, request.user_name -> user_name, request.user_roles -> user_roles
        proxyRequest!.RequestMessage.Headers.Should().ContainKey("request.user_id");
        proxyRequest.RequestMessage.Headers["request.user_id"].First().Should().Be("999");

        proxyRequest.RequestMessage.Headers.Should().ContainKey("request.user_name");
        proxyRequest.RequestMessage.Headers["request.user_name"].First().Should().Be("proxyuser");

        proxyRequest.RequestMessage.Headers.Should().ContainKey("request.user_roles");
        proxyRequest.RequestMessage.Headers["request.user_roles"].First().Should().Contain("proxyrole");
    }
}
