using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ResolvedParameterInBodyTests()
    {
        script.Append($@"
        /*
        Resolved param in POST body template.
        _token is resolved from DB (hardcoded user 'myname' in the SQL expression).
        _payload comes from the HTTP request.
        POST body template: {{""token"": ""{{_token}}"", ""data"": ""{{_payload}}""}}
        */
        create type http_api_resolved_body as (
            body json,
            status_code int
        );
        comment on type http_api_resolved_body is 'POST http://localhost:{WireMockFixture.Port}/api/resolved/webhook
Content-Type: application/json

{{""token"": ""{{_token}}"", ""data"": ""{{_payload}}""}}';

        create function get_http_resolved_body(
            _payload text,
            _req http_api_resolved_body,
            _token text default null
        )
        returns table (body json, status_code int)
        language plpgsql as $$
        begin
            return query select (_req).body, (_req).status_code;
        end;
        $$;
        comment on function get_http_resolved_body(text, http_api_resolved_body, text) is '
_token = select api_token from http_api_tokens where user_name = ''myname''
';
");
    }
}

[Collection("TestFixture")]
public class ResolvedParameterInBodyTests : IClassFixture<WireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ResolvedParameterInBodyTests(TestFixture test, WireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_resolved_param_in_post_body()
    {
        // _token resolved from DB (hardcoded user 'myname' in the annotation expression)
        // _payload comes from the HTTP request
        // POST body template: {"token": "{_token}", "data": "{_payload}"}
        // Expected body: {"token": "secret-token-abc123", "data": "hello-world"}
        _server
            .Given(Request.Create()
                .WithPath("/api/resolved/webhook")
                .UsingPost()
                .WithBody("{\"token\": \"secret-token-abc123\", \"data\": \"hello-world\"}"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"webhook\": \"received\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-resolved-body/?payload=hello-world");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("received");
    }
}
