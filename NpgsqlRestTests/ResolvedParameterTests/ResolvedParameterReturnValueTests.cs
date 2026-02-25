using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ResolvedParameterReturnValueTests()
    {
        script.Append($@"
        /*
        Resolved param value available to the PostgreSQL function.
        _token is resolved from DB and passed to the function as a regular parameter.
        The function returns it in the JSON result, proving it received the resolved value.
        */
        create function get_http_resolved_return_token(
            _name text,
            _req http_api_resolved_auth,
            _token text default null
        )
        returns json
        language plpgsql as $$
        begin
            return json_build_object(
                'token_was', _token,
                'response_body', (_req).body,
                'response_status', (_req).status_code
            );
        end;
        $$;
        comment on function get_http_resolved_return_token(text, http_api_resolved_auth, text) is '
_token = select api_token from http_api_tokens where user_name = {{_name}}
';
");
    }
}

[Collection("TestFixture")]
public class ResolvedParameterReturnValueTests : IClassFixture<WireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ResolvedParameterReturnValueTests(TestFixture test, WireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_resolved_param_value_available_to_pg_function()
    {
        // The PG function returns json_build_object('token_was', _token, ...)
        // _token is resolved from DB, so the function should receive it
        _server
            .Given(Request.Create()
                .WithPath("/api/resolved/protected")
                .WithHeader("Authorization", "Bearer secret-token-abc123")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"external\": \"data\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-resolved-return-token/?name=myname");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // The function returns json_build_object with token_was = the resolved token
        content.Should().Contain("\"token_was\" : \"secret-token-abc123\"");
        content.Should().Contain("\"response_status\" : 200");
    }
}
