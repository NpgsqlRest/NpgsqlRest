using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyForwardingSecurityTests()
    {
        script.Append(@"
create function proxy_sec_user_params(
    _user_id text default null,
    _proxy_status_code int default null,
    _proxy_body text default null
)
returns json
language sql
as $$ select json_build_object('proxy_status', _proxy_status_code) $$;
comment on function proxy_sec_user_params(text, int, text) is 'HTTP GET
user_params
proxy';

create function proxy_sec_user_context(
    _proxy_status_code int default null,
    _proxy_body text default null
)
returns json
language sql
as $$ select json_build_object('proxy_status', _proxy_status_code) $$;
comment on function proxy_sec_user_context(int, text) is 'HTTP GET
user_context
proxy';

create function proxy_sec_passthrough()
returns void
language plpgsql
as $$ begin raise exception 'not called'; end; $$;
comment on function proxy_sec_passthrough() is 'HTTP GET
proxy';
        ");
    }
}

/// <summary>
/// Security regressions for proxy forwarding:
/// - server-filled values (user parameters, user context headers) must replace client-supplied ones, never be
///   appended after them, and clients must not be able to supply them at all when anonymous;
/// - the caller's credentials (Cookie, Authorization) are not forwarded to the upstream by default, and the
///   upstream cannot set cookies (Set-Cookie) on this application's origin by default.
/// Login uses proxy_with_user_params_login (ProxyWithUserParamsTest): name_identifier 999, name proxyuser.
/// </summary>
[Collection("TestFixture")]
public class ProxyForwardingSecurityTests : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyForwardingSecurityTests(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
        _server
            .Given(Request.Create().UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok").WithHeader("Set-Cookie", "upstream=evil; Path=/"));
    }

    private WireMock.Logging.ILogEntry? LastRequest(string path) =>
        _server.LogEntries.LastOrDefault(r => r.RequestMessage.Path == path);

    [Fact]
    public async Task Authenticated_user_parameter_is_forwarded_once_with_the_server_value()
    {
        using var client = _test.Application.CreateClient();
        using var login = await client.PostAsync("/api/proxy-with-user-params-login/", null);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        using var response = await client.GetAsync("/api/proxy-sec-user-params/?userId=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var upstream = LastRequest("/api/proxy-sec-user-params/");
        upstream.Should().NotBeNull();
        upstream!.RequestMessage.Query!["userId"].Should().BeEquivalentTo(["999"]);
    }

    [Fact]
    public async Task Anonymous_client_cannot_supply_a_user_parameter_to_the_upstream()
    {
        using var response = await _test.Client.GetAsync("/api/proxy-sec-user-params/?userId=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var upstream = LastRequest("/api/proxy-sec-user-params/");
        upstream.Should().NotBeNull();
        (upstream!.RequestMessage.Query?.ContainsKey("userId") ?? false).Should().BeFalse();
    }

    [Fact]
    public async Task Anonymous_client_cannot_supply_a_user_context_header_to_the_upstream()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/proxy-sec-user-context/");
        request.Headers.TryAddWithoutValidation("request.user_id", "1");
        request.Headers.TryAddWithoutValidation("request.user_roles", "{admin}");
        using var response = await _test.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var upstream = LastRequest("/api/proxy-sec-user-context/");
        upstream.Should().NotBeNull();
        upstream!.RequestMessage.Headers!.ContainsKey("request.user_id").Should().BeFalse();
        upstream.RequestMessage.Headers.ContainsKey("request.user_roles").Should().BeFalse();
    }

    [Fact]
    public async Task Authenticated_user_context_header_is_the_server_value_only()
    {
        using var client = _test.Application.CreateClient();
        using var login = await client.PostAsync("/api/proxy-with-user-params-login/", null);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/proxy-sec-user-context/");
        request.Headers.TryAddWithoutValidation("request.user_id", "1");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var upstream = LastRequest("/api/proxy-sec-user-context/");
        upstream.Should().NotBeNull();
        upstream!.RequestMessage.Headers!["request.user_id"].Should().BeEquivalentTo(["999"]);
    }

    [Fact]
    public async Task Credentials_are_not_forwarded_and_upstream_cookies_are_not_returned_by_default()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/proxy-sec-passthrough/");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer secret-token");
        request.Headers.TryAddWithoutValidation("Cookie", "session=secret-session");
        request.Headers.TryAddWithoutValidation("X-Custom", "forwarded");
        using var response = await _test.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("ok");
        response.Headers.Contains("Set-Cookie").Should().BeFalse();

        var upstream = LastRequest("/api/proxy-sec-passthrough/");
        upstream.Should().NotBeNull();
        upstream!.RequestMessage.Headers!.ContainsKey("Authorization").Should().BeFalse();
        upstream.RequestMessage.Headers.ContainsKey("Cookie").Should().BeFalse();
        upstream.RequestMessage.Headers["X-Custom"].Should().BeEquivalentTo(["forwarded"]);
    }
}
