using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyOutUpstreamErrorTest()
    {
        script.Append(@"
create function proxy_out_upstream_error()
returns json
language plpgsql
as
$$
begin
    return json_build_object('data', 'to process');
end;
$$;
comment on function proxy_out_upstream_error() is 'HTTP GET
proxy_out POST';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyOutUpstreamErrorTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyOutUpstreamErrorTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_out_forwards_upstream_error_to_client()
    {
        _server
            .Given(Request.Create()
                .WithPath("/api/proxy-out-upstream-error/")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBody("upstream processing failed"));

        using var response = await _test.Client.GetAsync("/api/proxy-out-upstream-error/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        content.Should().Be("upstream processing failed");
    }
}
