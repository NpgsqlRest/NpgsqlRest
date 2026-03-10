using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyOutMethodOverrideTest()
    {
        script.Append(@"
create function proxy_out_method_override()
returns json
language plpgsql
as
$$
begin
    return json_build_object('action', 'update');
end;
$$;
comment on function proxy_out_method_override() is 'HTTP GET
proxy_out PUT';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyOutMethodOverrideTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyOutMethodOverrideTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_out_uses_specified_http_method()
    {
        _server
            .Given(Request.Create()
                .WithPath("/api/proxy-out-method-override/")
                .UsingPut())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("updated via PUT"));

        using var response = await _test.Client.GetAsync("/api/proxy-out-method-override/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("updated via PUT");
    }
}
