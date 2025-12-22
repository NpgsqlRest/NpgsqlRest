using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyTransformBodyTest()
    {
        script.Append(@"
create function proxy_transform_body(_proxy_body text)
returns text
language plpgsql
as
$$
begin
    return 'Received: ' || coalesce(_proxy_body, 'NULL');
end;
$$;
comment on function proxy_transform_body(text) is 'HTTP GET
proxy';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyTransformBodyTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyTransformBodyTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_transform_body()
    {
        _server
            .Given(Request.Create().WithPath("/api/proxy-transform-body/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("proxy response body"));

        using var response = await _test.Client.GetAsync("/api/proxy-transform-body/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("Received: proxy response body");
    }
}
