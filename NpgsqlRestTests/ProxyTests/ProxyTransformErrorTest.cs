using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyTransformErrorTest()
    {
        script.Append(@"
create function proxy_transform_error(_proxy_error_message text)
returns text
language plpgsql
as
$$
begin
    return coalesce(_proxy_error_message, 'no error');
end;
$$;
comment on function proxy_transform_error(text) is 'HTTP GET
proxy';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyTransformErrorTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyTransformErrorTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_transform_error_no_error()
    {
        _server
            .Given(Request.Create().WithPath("/api/proxy-transform-error/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("ok"));

        using var response = await _test.Client.GetAsync("/api/proxy-transform-error/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("no error");
    }
}
