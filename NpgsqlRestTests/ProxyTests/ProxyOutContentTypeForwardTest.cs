using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyOutContentTypeForwardTest()
    {
        script.Append(@"
create function proxy_out_content_type()
returns json
language plpgsql
as
$$
begin
    return json_build_object('render', 'this');
end;
$$;
comment on function proxy_out_content_type() is 'HTTP GET
proxy_out POST';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyOutContentTypeForwardTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyOutContentTypeForwardTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_out_returns_upstream_content_type_to_client()
    {
        _server
            .Given(Request.Create()
                .WithPath("/api/proxy-out-content-type/")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/pdf")
                .WithBody("fake-pdf-bytes"));

        using var response = await _test.Client.GetAsync("/api/proxy-out-content-type/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        response?.Content.Headers.ContentType?.ToString().Should().Contain("application/pdf");
        content.Should().Be("fake-pdf-bytes");
    }
}
