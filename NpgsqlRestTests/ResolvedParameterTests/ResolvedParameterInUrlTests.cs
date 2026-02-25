using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ResolvedParameterInUrlTests()
    {
        script.Append($@"
        /*
        Resolved param used in URL path placeholder.
        _secret_path is resolved from the DB and substituted into the outgoing URL.
        The client only provides _name; the actual URL path segment is a server-side secret.
        */
        create type http_api_resolved_url as (
            body json,
            status_code int
        );
        comment on type http_api_resolved_url is 'GET http://localhost:{WireMockFixture.Port}/api/resolved/resource/{{_secret_path}}';

        create function get_http_resolved_url(
            _name text,
            _req http_api_resolved_url,
            _secret_path text default null
        )
        returns table (body json, status_code int)
        language plpgsql as $$
        begin
            return query select (_req).body, (_req).status_code;
        end;
        $$;
        comment on function get_http_resolved_url(text, http_api_resolved_url, text) is '
_secret_path = select api_token from http_api_tokens where user_name = {{_name}}
';
");
    }
}

[Collection("TestFixture")]
public class ResolvedParameterInUrlTests : IClassFixture<WireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ResolvedParameterInUrlTests(TestFixture test, WireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_resolved_param_used_in_url_path()
    {
        // _secret_path is resolved from DB: api_token for user_name='myname' = 'secret-token-abc123'
        // This value is used in the URL: GET /api/resolved/resource/{_secret_path}
        _server
            .Given(Request.Create()
                .WithPath("/api/resolved/resource/secret-token-abc123")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("{\"resource\": \"found\"}"));

        using var response = await _test.Client.GetAsync("/api/get-http-resolved-url/?name=myname");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("found");
    }
}
