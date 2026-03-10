using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyOutQueryParamBindingTest()
    {
        script.Append(@"
create function proxy_out_query_binding(p_format text, p_id int)
returns json
language plpgsql
as
$$
begin
    return json_build_object('id', p_id, 'data', 'report');
end;
$$;
comment on function proxy_out_query_binding(text, int) is 'HTTP GET
proxy_out POST';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyOutQueryParamBindingTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyOutQueryParamBindingTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_out_forwards_original_query_string_to_upstream()
    {
        _server
            .Given(Request.Create()
                .WithPath("/api/proxy-out-query-binding/")
                .WithParam("pFormat", "pdf")
                .WithParam("pId", "123")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/pdf")
                .WithBody("pdf-binary-content"));

        using var response = await _test.Client.GetAsync("/api/proxy-out-query-binding/?pFormat=pdf&pId=123");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Be("pdf-binary-content");
    }
}
