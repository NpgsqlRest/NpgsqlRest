using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyTransformContentTypeTest()
    {
        script.Append(@"
create function proxy_transform_content_type(_proxy_content_type text)
returns text
language plpgsql
as
$$
begin
    return _proxy_content_type;
end;
$$;
comment on function proxy_transform_content_type(text) is 'HTTP GET
proxy';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyTransformContentTypeTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyTransformContentTypeTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_transform_content_type()
    {
        _server
            .Given(Request.Create().WithPath("/api/proxy-transform-content-type/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "text/xml")
                .WithBody("<ok/>"));

        using var response = await _test.Client.GetAsync("/api/proxy-transform-content-type/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("text/xml");
    }
}
