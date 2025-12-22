using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyPassthroughTest()
    {
        script.Append(@"
create function proxy_passthrough()
returns void
language plpgsql
as
$$
begin
    raise exception 'This should not be called';
end;
$$;
comment on function proxy_passthrough() is 'HTTP GET
proxy';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyPassthroughTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyPassthroughTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_passthrough_returns_response_directly()
    {
        _server
            .Given(Request.Create().WithPath("/api/proxy-passthrough/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"message\": \"hello from proxy\"}"));

        using var response = await _test.Client.GetAsync("/api/proxy-passthrough/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("hello from proxy");
    }

    [Fact]
    public async Task Test_proxy_passthrough_forwards_status_code()
    {
        _server
            .Given(Request.Create().WithPath("/api/proxy-passthrough/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(404)
                .WithBody("not found"));

        using var response = await _test.Client.GetAsync("/api/proxy-passthrough/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.NotFound);
        content.Should().Be("not found");
    }

    [Fact]
    public async Task Test_proxy_passthrough_forwards_content_type()
    {
        _server
            .Given(Request.Create().WithPath("/api/proxy-passthrough/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "text/xml")
                .WithBody("<data/>"));

        using var response = await _test.Client.GetAsync("/api/proxy-passthrough/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        response?.Content.Headers.ContentType?.ToString().Should().Contain("text/xml");
        content.Should().Be("<data/>");
    }

    [Fact]
    public async Task Test_proxy_error_response()
    {
        _server
            .Given(Request.Create().WithPath("/api/proxy-passthrough/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBody("internal server error"));

        using var response = await _test.Client.GetAsync("/api/proxy-passthrough/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        content.Should().Be("internal server error");
    }
}
