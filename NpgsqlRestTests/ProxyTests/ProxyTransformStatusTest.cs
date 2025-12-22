using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyTransformStatusTest()
    {
        script.Append(@"
create function proxy_transform_status(_proxy_status_code int)
returns int
language plpgsql
as
$$
begin
    return _proxy_status_code * 2;
end;
$$;
comment on function proxy_transform_status(int) is 'HTTP GET
proxy';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyTransformStatusTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyTransformStatusTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_transform_status_code()
    {
        _server
            .Given(Request.Create().WithPath("/api/proxy-transform-status/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithBody("ok"));

        using var response = await _test.Client.GetAsync("/api/proxy-transform-status/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // 201 * 2 = 402
        content.Should().Be("402");
    }
}
