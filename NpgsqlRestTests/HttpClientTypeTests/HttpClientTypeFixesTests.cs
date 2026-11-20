using WireMock;
using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Types;
using WireMock.Util;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void HttpClientTypeFixesTests()
    {
        script.Append($@"
        -- a timed-out attempt followed by a successful retry
        create type http_fix_stale as (body text, status_code int, success boolean, error_message text);
        comment on type http_fix_stale is 'timeout 500ms
@retry_delay 50ms
GET http://localhost:{WireMockFixture.Port}/api/fix-stale';

        create function get_http_fix_stale(_req http_fix_stale) returns json language sql as $$
        select json_build_object('body', (_req).body, 'status_code', (_req).status_code, 'success', (_req).success, 'error_message', (_req).error_message)
        $$;
");
        script.Append(@"
        -- a call to an endpoint of this application, with a placeholder in a header value
        create function fix_echo_headers() returns text language sql as 'select current_setting(''request.headers'', true)';
        comment on function fix_echo_headers() is 'HTTP GET
request_headers context';

        create type http_fix_self_header as (body text, status_code int);
        comment on type http_fix_self_header is 'GET /api/fix-echo-headers
X-Fix-Value: value-{_v}';

        create function get_http_fix_self_header(_v text, _req http_fix_self_header) returns text language sql as $$
        select (_req).body
        $$;

        -- a call to an endpoint of this application that takes longer than the type's timeout
        create function fix_slow_endpoint() returns text language sql as 'select pg_sleep(3)::text';
        comment on function fix_slow_endpoint() is 'HTTP GET';

        create type http_fix_self_timeout as (body text, status_code int, success boolean, error_message text);
        comment on type http_fix_self_timeout is 'timeout 300ms
GET /api/fix-slow-endpoint';

        create function get_http_fix_self_timeout(_req http_fix_self_timeout) returns json language sql as $$
        select json_build_object('status_code', (_req).status_code, 'success', (_req).success, 'error_message', (_req).error_message)
        $$;
");
    }
}

/// <summary>Fixes in HTTP Custom Types (3.22.0).</summary>
[Collection("TestFixture")]
public class HttpClientTypeFixesTests : IClassFixture<WireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public HttpClientTypeFixesTests(TestFixture test, WireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Successful_retry_does_not_keep_the_error_message_of_the_failed_attempt()
    {
        // first attempt: slower than the 500 ms timeout; second: answers at once
        int attempts = 0;
        _server
            .Given(Request.Create().WithPath("/api/fix-stale").UsingGet())
            .RespondWith(Response.Create().WithCallback(req =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    Thread.Sleep(2000);
                }
                return new ResponseMessage
                {
                    StatusCode = 200,
                    BodyData = new BodyData { BodyAsString = "ok", DetectedBodyType = BodyType.String }
                };
            }));

        using var response = await _test.Client.GetAsync("/api/get-http-fix-stale/");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // error_message used to stay "Request timed out after 0.5 seconds" on this successful response
        content.Should().Be("{\"body\" : \"ok\", \"status_code\" : 200, \"success\" : true, \"error_message\" : null}");
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task Call_to_an_own_endpoint_resolves_placeholders_in_header_values()
    {
        using var response = await _test.Client.GetAsync("/api/get-http-fix-self-header/?v=hello");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // the header used to arrive as written: "value-{_v}"
        content.Should().Contain("\"X-Fix-Value\":\"value-hello\"");
        content.Should().NotContain("{_v}");
    }

    [Fact]
    public async Task Call_to_an_own_endpoint_honors_the_timeout()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var response = await _test.Client.GetAsync("/api/get-http-fix-self-timeout/");
        sw.Stop();
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // the endpoint sleeps 3 seconds; the type allows 300 ms. Without the timeout this took 3 seconds and said 200.
        content.Should().Be("{\"status_code\" : 408, \"success\" : false, \"error_message\" : \"Request timed out after 0.3 seconds\"}");
        sw.Elapsed.TotalSeconds.Should().BeLessThan(2.5);
    }
}
