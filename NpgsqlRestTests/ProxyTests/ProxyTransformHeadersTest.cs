using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyTransformHeadersTest()
    {
        script.Append(@"
create function proxy_transform_headers(_proxy_headers json)
returns json
language plpgsql
as
$$
begin
    return _proxy_headers;
end;
$$;
comment on function proxy_transform_headers(json) is 'HTTP GET
proxy';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyTransformHeadersTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyTransformHeadersTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_transform_headers()
    {
        _server
            .Given(Request.Create().WithPath("/api/proxy-transform-headers/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("X-Custom-Header", "custom-value")
                .WithBody("ok"));

        using var response = await _test.Client.GetAsync("/api/proxy-transform-headers/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("X-Custom-Header");
        content.Should().Contain("custom-value");
    }
}
