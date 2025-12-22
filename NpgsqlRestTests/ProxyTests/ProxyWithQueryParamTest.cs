using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyWithQueryParamTest()
    {
        script.Append(@"
create function proxy_with_query(p_name text)
returns void
language plpgsql
as
$$
begin
    raise exception 'This should not be called';
end;
$$;
comment on function proxy_with_query(text) is 'HTTP GET
proxy';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyWithQueryParamTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyWithQueryParamTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_with_query_parameter()
    {
        _server
            .Given(Request.Create()
                .WithPath("/api/proxy-with-query/")
                .WithParam("pName", "testvalue")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("query param received"));

        using var response = await _test.Client.GetAsync("/api/proxy-with-query/?pName=testvalue");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("query param received");
    }
}
