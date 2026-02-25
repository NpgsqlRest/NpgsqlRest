using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ResolvedParameterMultiPlaceholderTests()
    {
        script.Append($@"
        /*
        Multiple placeholders in a single resolved expression.
        _token expression references both {{_name}} and {{_role}} to verify
        that positional parameters map to the correct named values.
        The SQL: select api_token from http_api_tokens where user_name = {{_name}} and {{_role}} = 'admin'
        $1 must be _name's value, $2 must be _role's value.
        */
        create type http_api_resolved_multi_ph as (
            body json,
            status_code int
        );
        comment on type http_api_resolved_multi_ph is 'GET http://localhost:{WireMockFixture.Port}/api/resolved/multi-ph
Authorization: Bearer {{_token}}';

        create function get_http_resolved_multi_placeholder(
            _role text,
            _name text,
            _req http_api_resolved_multi_ph,
            _token text default null
        )
        returns table (body json, status_code int)
        language plpgsql as $$
        begin
            return query select (_req).body, (_req).status_code;
        end;
        $$;
        comment on function get_http_resolved_multi_placeholder(text, text, http_api_resolved_multi_ph, text) is '
_token = select api_token from http_api_tokens where user_name = {{_name}} and {{_role}} = ''admin''
';
");
    }
}

[Collection("TestFixture")]
public class ResolvedParameterMultiPlaceholderTests : IClassFixture<WireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ResolvedParameterMultiPlaceholderTests(TestFixture test, WireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_multi_placeholder_correct_name_mapping()
    {
        // Expression: select api_token from http_api_tokens where user_name = {_name} and {_role} = 'admin'
        // Parameterized: select api_token from http_api_tokens where user_name = $1 and $2 = 'admin'
        // _name='myname' → $1='myname', _role='admin' → $2='admin'
        // Result: 'secret-token-abc123'
        _server
            .Given(Request.Create()
                .WithPath("/api/resolved/multi-ph")
                .WithHeader("Authorization", "Bearer secret-token-abc123")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"multi_ph\": \"ok\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-resolved-multi-placeholder/?name=myname&role=admin");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("ok");
    }

    [Fact]
    public async Task Test_multi_placeholder_wrong_role_no_match()
    {
        // Same expression but _role='viewer' → 'viewer' = 'admin' is false → no rows → NULL token
        // NULL resolves to empty string in placeholder, so header becomes "Authorization: Bearer "
        // WireMock won't match that, so the external call returns 404 from WireMock
        // The PG function returns that 404 response
        _server
            .Given(Request.Create()
                .WithPath("/api/resolved/multi-ph")
                .WithHeader("Authorization", "Bearer secret-token-abc123")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"multi_ph\": \"should_not_match\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-resolved-multi-placeholder/?name=myname&role=viewer");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // The token is NULL (no match), so "Bearer secret-token-abc123" header is NOT sent.
        // WireMock returns 404 "No matching mapping" — proving the wrong token was NOT used.
        content.Should().NotContain("should_not_match");
        content.Should().Contain("No matching mapping");
    }

    [Fact]
    public async Task Test_multi_placeholder_swapped_params_still_correct()
    {
        // Call with parameters in reversed order in query string (role first, name second)
        // The name-based mapping must still resolve correctly:
        // $1 = _name = 'myname', $2 = _role = 'admin'
        _server
            .Given(Request.Create()
                .WithPath("/api/resolved/multi-ph")
                .WithHeader("Authorization", "Bearer secret-token-abc123")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"multi_ph\": \"swapped_ok\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-resolved-multi-placeholder/?role=admin&name=myname");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("swapped_ok");
    }
}
