using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyOutBasicTest()
    {
        script.Append(@"
create function proxy_out_basic()
returns json
language plpgsql
as
$$
begin
    return json_build_object('key', 'value', 'number', 42);
end;
$$;
comment on function proxy_out_basic() is 'HTTP GET
proxy_out POST';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyOutBasicTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyOutBasicTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_out_executes_function_then_forwards_body_to_upstream()
    {
        _server
            .Given(Request.Create()
                .WithPath("/api/proxy-out-basic/")
                .UsingPost()
                .WithBody(b => b.Contains("key") && b.Contains("value")))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"processed\": true}"));

        using var response = await _test.Client.GetAsync("/api/proxy-out-basic/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("processed");
    }
}
