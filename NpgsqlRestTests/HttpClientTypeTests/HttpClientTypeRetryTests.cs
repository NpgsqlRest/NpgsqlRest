using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock;
using WireMock.Types;
using WireMock.Util;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void HttpClientTypeRetryTests()
    {
        script.Append($@"
        -- Retry basic: 3 retries with 100ms delays, no status code filter
        create type http_api_retry_basic as (
            body text,
            status_code int,
            error_message text
        );
        comment on type http_api_retry_basic is '@retry_delay 500ms, 500ms, 500ms
GET http://localhost:{WireMockFixture.Port}/api/retry-basic';

        create function get_http_retry_basic(
            _req http_api_retry_basic
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

        -- Retry with status codes: 2 retries, only on 429 and 503
        create type http_api_retry_with_codes as (
            body text,
            status_code int
        );
        comment on type http_api_retry_with_codes is '@retry_delay 100ms, 100ms on 429, 503
GET http://localhost:{WireMockFixture.Port}/api/retry-with-codes';

        create function get_http_retry_with_codes(
            _req http_api_retry_with_codes
        )
        returns json
        language plpgsql as $$
        begin
            return json_build_object(
                'body', (_req).body,
                'status_code', (_req).status_code
            );
        end;
        $$;

        -- Retry exhausted: 2 retries, all fail
        create type http_api_retry_exhausted as (
            body text,
            status_code int,
            error_message text
        );
        comment on type http_api_retry_exhausted is '@retry_delay 100ms, 100ms
GET http://localhost:{WireMockFixture.Port}/api/retry-exhausted';

        create function get_http_retry_exhausted(
            _req http_api_retry_exhausted
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
public class HttpClientTypeRetryTests : IClassFixture<WireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public HttpClientTypeRetryTests(TestFixture test, WireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_retry_basic_succeeds_after_failures()
    {
        // First 2 calls return 503, third returns 200. Implemented via a per-call counter rather
        // than WireMock's stateful scenarios because the scenario state-transition write races
        // with retry timing under CI load — three back-to-back hits could land in the catch-all
        // 503 stub before the prior response's WillSetStateTo had applied. The counter is read
        // and incremented inside the callback that builds each response, atomic with respect to
        // the response itself, so there is no transition window for the next request to observe
        // a stale state.
        int attempts = 0;
        _server
            .Given(Request.Create().WithPath("/api/retry-basic").UsingGet())
            .RespondWith(Response.Create().WithCallback(req =>
            {
                int n = Interlocked.Increment(ref attempts);
                return n >= 3
                    ? new ResponseMessage
                    {
                        StatusCode = 200,
                        BodyData = new BodyData { BodyAsString = "success-after-retry", DetectedBodyType = BodyType.String }
                    }
                    : new ResponseMessage
                    {
                        StatusCode = 503,
                        BodyData = new BodyData { BodyAsString = "service unavailable", DetectedBodyType = BodyType.String }
                    };
            }));

        using var response = await _test.Client.GetAsync("/api/get-http-retry-basic/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("success-after-retry");
        content.Should().Contain("\"status_code\" : 200");
    }

    [Fact]
    public async Task Test_retry_with_codes_retries_on_429()
    {
        // Counter-based to avoid the same WireMock-scenario race the basic test hit on CI.
        // First call returns 429, second returns 200.
        int attempts = 0;
        _server
            .Given(Request.Create().WithPath("/api/retry-with-codes").UsingGet())
            .RespondWith(Response.Create().WithCallback(req =>
            {
                int n = Interlocked.Increment(ref attempts);
                return n >= 2
                    ? new ResponseMessage
                    {
                        StatusCode = 200,
                        BodyData = new BodyData { BodyAsString = "ok-after-429", DetectedBodyType = BodyType.String }
                    }
                    : new ResponseMessage
                    {
                        StatusCode = 429,
                        BodyData = new BodyData { BodyAsString = "rate limited", DetectedBodyType = BodyType.String }
                    };
            }));

        using var response = await _test.Client.GetAsync("/api/get-http-retry-with-codes/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("ok-after-429");
        content.Should().Contain("\"status_code\" : 200");
    }

    [Fact]
    public async Task Test_retry_with_codes_no_retry_on_400()
    {
        // 400 is not in the retry list (429, 503) — should return immediately
        _server
            .Given(Request.Create().WithPath("/api/retry-with-codes").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(400).WithBody("bad request"));

        using var response = await _test.Client.GetAsync("/api/get-http-retry-with-codes/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        // The PG function returns json with the 400 status code from the external call
        content.Should().Contain("\"status_code\" : 400");
    }

    [Fact]
    public async Task Test_retry_exhausted_returns_last_error()
    {
        // All 3 calls (initial + 2 retries) return 503
        _server
            .Given(Request.Create().WithPath("/api/retry-exhausted").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(503).WithBody("always failing"));

        using var response = await _test.Client.GetAsync("/api/get-http-retry-exhausted/");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("\"status_code\" : 503");
    }
}
