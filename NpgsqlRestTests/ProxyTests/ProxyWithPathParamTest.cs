using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyWithPathParamTest()
    {
        script.Append(@"
create function proxy_with_path_param(p_id int)
returns void
language plpgsql
as
$$
begin
    raise exception 'This should not be called';
end;
$$;
comment on function proxy_with_path_param(int) is 'HTTP GET /api/proxy-with-path-param/{p_id}
proxy';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyWithPathParamTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyWithPathParamTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_with_path_parameter()
    {
        _server
            .Given(Request.Create().WithPath("/api/proxy-with-path-param/123").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("path param received"));

        using var response = await _test.Client.GetAsync("/api/proxy-with-path-param/123");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("path param received");
    }
}
