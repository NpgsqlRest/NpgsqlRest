using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyCustomHostTest()
    {
        script.Append($@"
create function proxy_custom_host()
returns void
language plpgsql
as
$$
begin
    raise exception 'This should not be called';
end;
$$;
comment on function proxy_custom_host() is 'HTTP GET
proxy http://localhost:{ProxyWireMockFixture.Port}';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyCustomHostTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyCustomHostTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_custom_host_forwards_to_specified_host()
    {
        _server
            .Given(Request.Create().WithPath("/api/proxy-custom-host/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("custom host response"));

        using var response = await _test.Client.GetAsync("/api/proxy-custom-host/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("custom host response");
    }
}
