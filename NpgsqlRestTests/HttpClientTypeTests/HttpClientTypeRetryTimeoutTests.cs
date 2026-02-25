using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void HttpClientTypeRetryTimeoutTests()
    {
        script.Append($@"
        -- Retry on timeout: timeout 1s with 2 retries
        create type http_api_retry_timeout as (
            body text,
            status_code int,
            error_message text
        );
        comment on type http_api_retry_timeout is 'timeout 1s
@retry_delay 100ms, 100ms
GET http://localhost:{WireMockFixture.Port}/api/retry-timeout';

        create function get_http_retry_timeout(
            _req http_api_retry_timeout
        )
        returns json
        language plpgsql as $$
        begin
            return json_build_object(
                'body', (_req).body,
                'status_code', (_req).status_code,
                'error_message', (_req).error_message
            );
        end;
        $$;
");
    }
}

[Collection("TestFixture")]
public class HttpClientTypeRetryTimeoutTests : IClassFixture<WireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public HttpClientTypeRetryTimeoutTests(TestFixture test, WireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_retry_on_timeout_exhausts_retries()
    {
        // All calls delay 5s, timeout is 1s → all 3 attempts (initial + 2 retries) timeout
        // With 2 retries @ 100ms delay each, total time ≈ 1s + 0.1s + 1s + 0.1s + 1s ≈ 3.2s
        // This proves retries happened (without retries it would be ~1s)
        _server
            .Given(Request.Create().WithPath("/api/retry-timeout").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("slow").WithDelay(5000));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var response = await _test.Client.GetAsync("/api/get-http-retry-timeout/");
        sw.Stop();
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("\"status_code\" : 408");
        content.Should().Contain("timed out");

        // Without retries, request would complete in ~1s. With 2 retries, it takes ~3s.
        sw.Elapsed.TotalSeconds.Should().BeGreaterThan(2.5);
    }
}
