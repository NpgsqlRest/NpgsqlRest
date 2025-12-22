using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyTransformSuccessTest()
    {
        script.Append(@"
create function proxy_transform_success(_proxy_success boolean)
returns boolean
language plpgsql
as
$$
begin
    return _proxy_success;
end;
$$;
comment on function proxy_transform_success(boolean) is 'HTTP GET
proxy';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyTransformSuccessTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyTransformSuccessTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_transform_success_true()
    {
        _server
            .Given(Request.Create().WithPath("/api/proxy-transform-success/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("ok"));

        using var response = await _test.Client.GetAsync("/api/proxy-transform-success/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("t"); // PostgreSQL returns 't' for true
    }

    [Fact]
    public async Task Test_proxy_transform_success_false()
    {
        _server
            .Given(Request.Create().WithPath("/api/proxy-transform-success/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBody("error"));

        using var response = await _test.Client.GetAsync("/api/proxy-transform-success/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("f"); // PostgreSQL returns 'f' for false
    }
}
