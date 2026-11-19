using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void HttpClientTypeUrlEncodingTests()
    {
        script.Append($@"
        create type http_enc_path as (body text, status_code int);
        comment on type http_enc_path is 'GET http://localhost:{WireMockFixture.Port}/api/enc/users/{{_name}}/profile';

        create function get_http_enc_path(_name text, _req http_enc_path)
        returns text language sql as $$ select (_req).status_code::text || ':' || coalesce((_req).body, '') $$;

        create type http_enc_query as (body text, status_code int);
        comment on type http_enc_query is 'GET http://localhost:{WireMockFixture.Port}/api/enc/search?q={{_q}}&limit=5';

        create function get_http_enc_query(_q text, _req http_enc_query)
        returns text language sql as $$ select (_req).status_code::text || ':' || coalesce((_req).body, '') $$;

        create function http_enc_echo(_value text)
        returns text language sql as $$ select 'echo:' || _value $$;
        comment on function http_enc_echo(text) is 'HTTP GET
        path /api/http-enc-echo/{{_value}}';

        create type http_enc_self as (body text, status_code int);
        comment on type http_enc_self is 'GET /api/http-enc-echo/{{_value}}';

        create function get_http_enc_self(_value text, _req http_enc_self)
        returns text language sql as $$ select (_req).body $$;
");
    }
}

/// <summary>
/// Security regression: request values substituted into an HTTP Custom Type URL are percent-encoded. They used to be
/// inserted raw, so a caller could add query parameters (x&amp;limit=1000) or walk the path (../../admin) of the
/// outbound request.
/// </summary>
[Collection("TestFixture")]
public class HttpClientTypeUrlEncodingTests : IClassFixture<WireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public HttpClientTypeUrlEncodingTests(TestFixture test, WireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
        _server
            .Given(Request.Create().WithPath(new WireMock.Matchers.WildcardMatcher("/*")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok"));
    }

    [Fact]
    public async Task Path_value_cannot_traverse_out_of_its_segment()
    {
        // _name = "../../admin?x=1"
        using var response = await _test.Client.GetAsync("/api/get-http-enc-path/?name=..%2F..%2Fadmin%3Fx%3D1");
        (await response.Content.ReadAsStringAsync()).Should().Be("200:ok");

        var received = _server.LogEntries.Single().RequestMessage;
        // one segment: '/' and '?' stay encoded (System.Uri canonicalizes %3D back to '=', which is inert in a path)
        received.AbsoluteUrl.Should().Be($"http://localhost:{WireMockFixture.Port}/api/enc/users/..%2F..%2Fadmin%3Fx=1/profile");
    }

    [Fact]
    public async Task Query_value_cannot_add_or_override_parameters()
    {
        // _q = "x&limit=1000"
        using var response = await _test.Client.GetAsync("/api/get-http-enc-query/?q=x%26limit%3D1000");
        (await response.Content.ReadAsStringAsync()).Should().Be("200:ok");

        var received = _server.LogEntries.Single().RequestMessage;
        received.AbsoluteUrl.Should().Be($"http://localhost:{WireMockFixture.Port}/api/enc/search?q=x%26limit%3D1000&limit=5");
        received.Query!["q"].Should().BeEquivalentTo(["x&limit=1000"]);
        received.Query!["limit"].Should().BeEquivalentTo(["5"]);
    }

    [Fact]
    public async Task Self_call_path_value_is_decoded_for_the_target_endpoint()
    {
        // _value = "a b/c" - encoded into one path segment, decoded again as the target's route value
        using var response = await _test.Client.GetAsync("/api/get-http-enc-self/?value=a%20b%2Fc");
        (await response.Content.ReadAsStringAsync()).Should().Be("echo:a b/c");
    }
}
