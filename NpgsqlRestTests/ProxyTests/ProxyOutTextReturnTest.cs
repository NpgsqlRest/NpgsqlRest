using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyOutTextReturnTest()
    {
        script.Append(@"
create function proxy_out_text_return()
returns text
language plpgsql
as
$$
begin
    return 'plain text body for upstream';
end;
$$;
comment on function proxy_out_text_return() is 'HTTP GET
proxy_out POST';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyOutTextReturnTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyOutTextReturnTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_out_forwards_text_return_as_body()
    {
        _server
            .Given(Request.Create()
                .WithPath("/api/proxy-out-text-return/")
                .UsingPost()
                .WithBody("plain text body for upstream"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("text processed"));

        using var response = await _test.Client.GetAsync("/api/proxy-out-text-return/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("text processed");
    }
}
