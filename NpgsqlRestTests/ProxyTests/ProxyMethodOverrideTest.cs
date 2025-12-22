using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyMethodOverrideTest()
    {
        script.Append(@"
create function proxy_method_override()
returns void
language plpgsql
as
$$
begin
    raise exception 'This should not be called';
end;
$$;
comment on function proxy_method_override() is 'HTTP GET
proxy POST';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyMethodOverrideTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyMethodOverrideTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_method_override_uses_specified_method()
    {
        _server
            .Given(Request.Create().WithPath("/api/proxy-method-override/").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithBody("created via POST"));

        using var response = await _test.Client.GetAsync("/api/proxy-method-override/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.Created);
        content.Should().Be("created via POST");
    }
}
