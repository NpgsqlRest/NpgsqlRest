using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ResolvedParameterMultiTests()
    {
        script.Append($@"
        /*
        Multiple resolved parameters test.
        Two params (_token and _api_key) are each resolved by separate SQL expressions.
        Both are used in different outgoing HTTP headers.
        */
        create type http_api_resolved_multi as (
            body json,
            status_code int
        );
        comment on type http_api_resolved_multi is 'GET http://localhost:{WireMockFixture.Port}/api/resolved/multi
Authorization: Bearer {{_token}}
X-Api-Key: {{_api_key}}';

        create function get_http_resolved_multi(
            _name text,
            _req http_api_resolved_multi,
            _token text default null,
            _api_key text default null
        )
        returns table (body json, status_code int)
        language plpgsql as $$
        begin
            return query select (_req).body, (_req).status_code;
        end;
        $$;
        comment on function get_http_resolved_multi(text, http_api_resolved_multi, text, text) is '
_token = select api_token from http_api_tokens where user_name = {{_name}}
_api_key = select ''static-key-'' || api_token from http_api_tokens where user_name = {{_name}}
';
");
    }
}

[Collection("TestFixture")]
public class ResolvedParameterMultiTests : IClassFixture<WireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ResolvedParameterMultiTests(TestFixture test, WireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_multiple_resolved_params_in_headers()
    {
        // Two resolved params:
        //   _token = select api_token from http_api_tokens where user_name = {_name}
        //   _api_key = select 'static-key-' || api_token from http_api_tokens where user_name = {_name}
        // For name='myname': _token='secret-token-abc123', _api_key='static-key-secret-token-abc123'
        _server
            .Given(Request.Create()
                .WithPath("/api/resolved/multi")
                .WithHeader("Authorization", "Bearer secret-token-abc123")
                .WithHeader("X-Api-Key", "static-key-secret-token-abc123")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"multi\": \"success\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-resolved-multi/?name=myname");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("success");
    }
}
