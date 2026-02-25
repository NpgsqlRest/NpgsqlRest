using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ResolvedParameterBasicTests()
    {
        script.Append($@"
        -- Shared infrastructure for resolved parameter tests
        create table http_api_tokens (
            user_name text primary key,
            api_token text not null
        );
        insert into http_api_tokens values ('myname', 'secret-token-abc123');
        insert into http_api_tokens values ('other_user', 'other-token-xyz');

        create type http_api_resolved_auth as (
            body json,
            status_code int
        );
        comment on type http_api_resolved_auth is 'GET http://localhost:{WireMockFixture.Port}/api/resolved/protected
Authorization: Bearer {{_token}}';

        /*
        Basic resolved parameter test.
        _token is resolved server-side via SQL expression using {{_name}} from the HTTP request.
        The resolved token is used in the Authorization header of the outgoing HTTP call.
        */
        create function get_http_resolved_basic(
            _name text,
            _req http_api_resolved_auth,
            _token text default null
        )
        returns table (body json, status_code int)
        language plpgsql as $$
        begin
            return query select (_req).body, (_req).status_code;
        end;
        $$;
        comment on function get_http_resolved_basic(text, http_api_resolved_auth, text) is '
_token = select api_token from http_api_tokens where user_name = {{_name}}
';
");
    }
}

[Collection("TestFixture")]
public class ResolvedParameterBasicTests : IClassFixture<WireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ResolvedParameterBasicTests(TestFixture test, WireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_resolved_param_basic_token_in_auth_header()
    {
        // The token "secret-token-abc123" should be resolved from http_api_tokens
        // table where user_name = 'myname', and used in Authorization header
        _server
            .Given(Request.Create()
                .WithPath("/api/resolved/protected")
                .WithHeader("Authorization", "Bearer secret-token-abc123")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"data\": \"protected-resource\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-resolved-basic/?name=myname");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("protected-resource");
    }

    [Fact]
    public async Task Test_resolved_param_basic_different_user()
    {
        // Token for other_user should be "other-token-xyz"
        _server
            .Given(Request.Create()
                .WithPath("/api/resolved/protected")
                .WithHeader("Authorization", "Bearer other-token-xyz")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"data\": \"other-user-resource\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-resolved-basic/?name=other_user");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("other-user-resource");
    }
}
