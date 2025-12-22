using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyTransformAllTest()
    {
        script.Append(@"
create function proxy_transform_all(
    _proxy_status_code int,
    _proxy_body text,
    _proxy_headers json,
    _proxy_content_type text,
    _proxy_success boolean,
    _proxy_error_message text
)
returns json
language plpgsql
as
$$
begin
    return json_build_object(
        'status_code', _proxy_status_code,
        'body', _proxy_body,
        'headers', _proxy_headers,
        'content_type', _proxy_content_type,
        'success', _proxy_success,
        'error_message', _proxy_error_message
    );
end;
$$;
comment on function proxy_transform_all(int, text, json, text, boolean, text) is 'HTTP GET
proxy';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyTransformAllTest : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public ProxyTransformAllTest(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_proxy_transform_all_parameters()
    {
        _server
            .Given(Request.Create().WithPath("/api/proxy-transform-all/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithHeader("X-Test", "test-value")
                .WithBody("{\"data\": 123}"));

        using var response = await _test.Client.GetAsync("/api/proxy-transform-all/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("\"status_code\" : 200");
        content.Should().Contain("\"body\" : \"{\\\"data\\\": 123}\"");
        content.Should().Contain("\"success\" : true");
        content.Should().Contain("\"error_message\" : null");
    }
}
