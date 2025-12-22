using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyPostBodyTest()
    {
        script.Append(@"
create function proxy_post_body()
returns void
language plpgsql
as
$$
begin
    raise exception 'This should not be called';
end;
$$;
comment on function proxy_post_body() is 'HTTP POST
proxy';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyPostBodyTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyPostBodyTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_post_forwards_body()
    {
        _server
            .Given(Request.Create()
                .WithPath("/api/proxy-post-body/")
                .UsingPost()
                .WithBody("{\"name\": \"test\"}"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("received"));

        using var response = await _test.Client.PostAsync("/api/proxy-post-body/",
            new StringContent("{\"name\": \"test\"}", System.Text.Encoding.UTF8, "application/json"));
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("received");
    }
}
