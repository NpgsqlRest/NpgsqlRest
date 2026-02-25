using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ResolvedParameterWithUserParamsTests()
    {
        script.Append($@"
        /*
        Resolved param with user_parameters.
        _user_name is auto-filled from JWT claim ""name"" (via user_params).
        _token is resolved via SQL using the claim-provided _user_name.
        The client never sends either parameter.
        */
        create function get_http_resolved_with_user_params(
            _user_name text,
            _req http_api_resolved_auth,
            _token text default null
        )
        returns table (body json, status_code int)
        language plpgsql as $$
        begin
            return query select (_req).body, (_req).status_code;
        end;
        $$;
        comment on function get_http_resolved_with_user_params(text, http_api_resolved_auth, text) is '
authorize
user_params
_token = select api_token from http_api_tokens where user_name = {{_user_name}}
';
");
    }
}

[Collection("TestFixture")]
public class ResolvedParameterWithUserParamsTests : IClassFixture<WireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ResolvedParameterWithUserParamsTests(TestFixture test, WireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_resolved_param_with_user_params_jwt_claim_feeds_sql()
    {
        // _user_name is auto-filled from JWT claim "name" = "myname"
        // Then _token is resolved via: select api_token from http_api_tokens where user_name = {_user_name}
        // So _token should be "secret-token-abc123"
        _server
            .Given(Request.Create()
                .WithPath("/api/resolved/protected")
                .WithHeader("Authorization", "Bearer secret-token-abc123")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"data\": \"authenticated-data\"}"));

        // Use a dedicated client to maintain auth cookies
        using var client = _test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        // Login first — sets name="myname" claim via user_params_login
        using var login = await client.PostAsync("/api/user-params-login/", null);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        // Call the endpoint — no params needed, _user_name from JWT, _token resolved from DB
        using var response = await client.GetAsync("/api/get-http-resolved-with-user-params/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("authenticated-data");
    }

    [Fact]
    public async Task Test_resolved_param_with_user_params_unauthorized()
    {
        // Without login, _user_name has no default and isn't in query string → 404
        // (parameter validation runs before authorization check)
        using var response = await _test.Client.GetAsync("/api/get-http-resolved-with-user-params/");

        response?.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
