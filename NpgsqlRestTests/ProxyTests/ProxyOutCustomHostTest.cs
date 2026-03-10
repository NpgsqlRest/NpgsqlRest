using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyOutCustomHostTest()
    {
        script.Append($@"
create function proxy_out_custom_host()
returns text
language plpgsql
as
$$
begin
    return 'payload from function';
end;
$$;
comment on function proxy_out_custom_host() is 'HTTP GET
proxy_out POST http://localhost:{ProxyWireMockFixture.Port}';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyOutCustomHostTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyOutCustomHostTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_out_custom_host_forwards_to_specified_host()
    {
        _server
            .Given(Request.Create()
                .WithPath("/api/proxy-out-custom-host/")
                .UsingPost()
                .WithBody("payload from function"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("custom host processed"));

        using var response = await _test.Client.GetAsync("/api/proxy-out-custom-host/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("custom host processed");
    }
}
