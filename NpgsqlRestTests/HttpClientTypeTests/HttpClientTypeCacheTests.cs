using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock;
using WireMock.Types;
using WireMock.Util;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void HttpClientTypeCacheTests()
    {
        script.Append($@"
        -- Cache test 1: basic GET with @cache, body response
        create type cache_basic_api as (
            body text
        );
        comment on type cache_basic_api is '@cache 60s
GET http://localhost:{WireMockFixture.Port}/api/cache-basic';

        create function get_cache_basic(
            req cache_basic_api
        )
        returns text
        language plpgsql
        as
        $$
        begin
            return (req).body;
        end;
        $$;

        -- Cache test 2: multi-field (6) type with @cache - dedup + cache combined
        create type cache_sixfield_api as (
            body text,
            status_code int,
            content_type text,
            headers json,
            success boolean,
            error_message text
        );
        comment on type cache_sixfield_api is '@cache 60s
GET http://localhost:{WireMockFixture.Port}/api/cache-sixfield';

        create function get_cache_sixfield(
            req cache_sixfield_api
        )
        returns text
        language plpgsql
        as
        $$
        begin
            return (req).body;
        end;
        $$;

        -- Cache test 3: error responses must NOT be cached
        create type cache_error_api as (
            body text,
            status_code int,
            success boolean
        );
        comment on type cache_error_api is '@cache 60s
GET http://localhost:{WireMockFixture.Port}/api/cache-error';

        create function get_cache_error(
            req cache_error_api
        )
        returns json
        language plpgsql
        as
        $$
        begin
            return json_build_object(
                'body', (req).body,
                'status_code', (req).status_code,
                'success', (req).success
            );
        end;
        $$;

        -- Cache test 4: @cache on POST is ignored (only GET is cacheable)
        create type cache_post_api as (
            body text,
            status_code int
        );
        comment on type cache_post_api is '@cache 60s
POST http://localhost:{WireMockFixture.Port}/api/cache-post
Content-Type: application/json

{{""ping"": true}}';

        create function get_cache_post(
            req cache_post_api
        )
        returns text
        language plpgsql
        as
        $$
        begin
            return (req).body;
        end;
        $$;

        -- Cache test 5: short TTL for expiry verification
        create type cache_ttl_api as (
            body text
        );
        comment on type cache_ttl_api is '@cache 1s
GET http://localhost:{WireMockFixture.Port}/api/cache-ttl';

        create function get_cache_ttl(
            req cache_ttl_api
        )
        returns text
        language plpgsql
        as
        $$
        begin
            return (req).body;
        end;
        $$;
");
    }
}

[Collection("TestFixture")]
public class HttpClientTypeCacheTests : IClassFixture<WireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;

    public HttpClientTypeCacheTests(TestFixture test, WireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    // A cached GET fires the outbound call once; the second request is served from the cache. The
    // callback embeds its invocation count in the body, so a cache hit returns the SAME body the
    // first call produced - proving the second response is the cached one, not a fresh fetch.
    [Fact]
    public async Task Test_cached_get_fires_outbound_call_once()
    {
        int calls = 0;
        _server
            .Given(Request.Create().WithPath("/api/cache-basic").UsingGet())
            .RespondWith(Response.Create().WithCallback(req =>
            {
                int n = Interlocked.Increment(ref calls);
                return new ResponseMessage
                {
                    StatusCode = 200,
                    BodyData = new BodyData { BodyAsString = $"cache-basic-call-{n}", DetectedBodyType = BodyType.String }
                };
            }));

        using var first = await _test.Client.GetAsync("/api/get-cache-basic/");
        var firstContent = await first.Content.ReadAsStringAsync();
        using var second = await _test.Client.GetAsync("/api/get-cache-basic/");
        var secondContent = await second.Content.ReadAsStringAsync();

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        firstContent.Should().Be("cache-basic-call-1");
        secondContent.Should().Be("cache-basic-call-1"); // served from cache, not "call-2"
        calls.Should().Be(1);
    }

    // A 6-field composite type combines the dedup fix (one call per request, not 6) with caching
    // (one call across requests). Two requests against a 6-field cached type → exactly one call.
    [Fact]
    public async Task Test_cached_multi_field_type_fires_one_call_across_requests()
    {
        int calls = 0;
        _server
            .Given(Request.Create().WithPath("/api/cache-sixfield").UsingGet())
            .RespondWith(Response.Create().WithCallback(req =>
            {
                Interlocked.Increment(ref calls);
                return new ResponseMessage
                {
                    StatusCode = 200,
                    BodyData = new BodyData { BodyAsString = "sixfield-body", DetectedBodyType = BodyType.String }
                };
            }));

        using var first = await _test.Client.GetAsync("/api/get-cache-sixfield/");
        var firstContent = await first.Content.ReadAsStringAsync();
        using var second = await _test.Client.GetAsync("/api/get-cache-sixfield/");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        firstContent.Should().Be("sixfield-body");
        calls.Should().Be(1);
    }

    // A failed (non-2xx) response must not be cached, so a transient upstream error is not pinned for
    // the whole TTL. First call returns 500, second returns 200 - the second must reach upstream and
    // observe the 200, and both calls must hit the server.
    [Fact]
    public async Task Test_error_response_is_not_cached()
    {
        int calls = 0;
        _server
            .Given(Request.Create().WithPath("/api/cache-error").UsingGet())
            .RespondWith(Response.Create().WithCallback(req =>
            {
                int n = Interlocked.Increment(ref calls);
                return n == 1
                    ? new ResponseMessage
                    {
                        StatusCode = 500,
                        BodyData = new BodyData { BodyAsString = "boom", DetectedBodyType = BodyType.String }
                    }
                    : new ResponseMessage
                    {
                        StatusCode = 200,
                        BodyData = new BodyData { BodyAsString = "recovered", DetectedBodyType = BodyType.String }
                    };
            }));

        using var first = await _test.Client.GetAsync("/api/get-cache-error/");
        var firstContent = await first.Content.ReadAsStringAsync();
        using var second = await _test.Client.GetAsync("/api/get-cache-error/");
        var secondContent = await second.Content.ReadAsStringAsync();

        firstContent.Should().Contain("\"status_code\" : 500");
        firstContent.Should().Contain("\"success\" : false");
        secondContent.Should().Contain("\"status_code\" : 200");
        secondContent.Should().Contain("\"success\" : true");
        calls.Should().Be(2); // the 500 was not cached
    }

    // @cache on a non-GET method is ignored (warned at startup), so POST fires every request.
    [Fact]
    public async Task Test_cache_directive_ignored_for_post()
    {
        int calls = 0;
        _server
            .Given(Request.Create().WithPath("/api/cache-post").UsingPost())
            .RespondWith(Response.Create().WithCallback(req =>
            {
                Interlocked.Increment(ref calls);
                return new ResponseMessage
                {
                    StatusCode = 200,
                    BodyData = new BodyData { BodyAsString = "posted", DetectedBodyType = BodyType.String }
                };
            }));

        using var first = await _test.Client.GetAsync("/api/get-cache-post/");
        using var second = await _test.Client.GetAsync("/api/get-cache-post/");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        calls.Should().Be(2); // POST is never cached
    }

    // After the TTL elapses, the entry expires and the next request re-fetches.
    [Fact]
    public async Task Test_cached_response_expires_after_ttl()
    {
        int calls = 0;
        _server
            .Given(Request.Create().WithPath("/api/cache-ttl").UsingGet())
            .RespondWith(Response.Create().WithCallback(req =>
            {
                Interlocked.Increment(ref calls);
                return new ResponseMessage
                {
                    StatusCode = 200,
                    BodyData = new BodyData { BodyAsString = "ttl-body", DetectedBodyType = BodyType.String }
                };
            }));

        using var first = await _test.Client.GetAsync("/api/get-cache-ttl/");      // miss -> 1 call
        using var second = await _test.Client.GetAsync("/api/get-cache-ttl/");     // hit  -> still 1
        calls.Should().Be(1);

        await Task.Delay(TimeSpan.FromSeconds(2)); // TTL is 1s; generous margin

        using var third = await _test.Client.GetAsync("/api/get-cache-ttl/");      // expired -> 2 calls
        third.StatusCode.Should().Be(HttpStatusCode.OK);
        calls.Should().Be(2);
    }
}
