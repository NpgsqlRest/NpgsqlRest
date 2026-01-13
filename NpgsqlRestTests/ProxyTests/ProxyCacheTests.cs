using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyCacheTests()
    {
        script.Append(@"
-- Passthrough proxy with caching (no proxy response parameters)
create function proxy_cache_passthrough()
returns void
language plpgsql
as
$$
begin
    raise exception 'This should not be called - passthrough proxy';
end;
$$;
comment on function proxy_cache_passthrough() is 'HTTP GET
proxy
cached';

-- Passthrough proxy with caching and cache key parameter
create function proxy_cache_passthrough_with_key(_key text)
returns void
language plpgsql
as
$$
begin
    raise exception 'This should not be called - passthrough proxy with key';
end;
$$;
comment on function proxy_cache_passthrough_with_key(text) is 'HTTP GET
proxy
cached _key';

-- Passthrough proxy with caching and expiration
create function proxy_cache_passthrough_expires()
returns void
language plpgsql
as
$$
begin
    raise exception 'This should not be called - passthrough proxy expires';
end;
$$;
comment on function proxy_cache_passthrough_expires() is 'HTTP GET
proxy
cached
cache_expires_in 1 second';

-- Passthrough proxy without caching (for comparison)
create function proxy_no_cache_passthrough()
returns void
language plpgsql
as
$$
begin
    raise exception 'This should not be called - no cache passthrough';
end;
$$;
comment on function proxy_no_cache_passthrough() is 'HTTP GET
proxy';

-- Separate endpoint for status code preservation test
create function proxy_cache_status_test()
returns void
language plpgsql
as
$$
begin
    raise exception 'This should not be called - status test';
end;
$$;
comment on function proxy_cache_status_test() is 'HTTP GET
proxy
cached';

-- Separate endpoint for content type preservation test
create function proxy_cache_content_type_test()
returns void
language plpgsql
as
$$
begin
    raise exception 'This should not be called - content type test';
end;
$$;
comment on function proxy_cache_content_type_test() is 'HTTP GET
proxy
cached';
        ");
    }
}

[Collection("TestFixture")]
public class ProxyCacheTests : IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _server;
    private static int _requestCounter = 0;

    public ProxyCacheTests(TestFixture test, ProxyWireMockFixture wireMock)
    {
        _test = test;
        _server = wireMock.Server;
        _server.Reset();
    }

    [Fact]
    public async Task Test_Proxy_Cache_Returns_Same_Response_On_Subsequent_Calls()
    {
        var counter = Interlocked.Increment(ref _requestCounter);

        _server
            .Given(Request.Create().WithPath("/api/proxy-cache-passthrough/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody($"{{\"counter\": {counter}, \"timestamp\": \"{DateTime.UtcNow:O}\"}}"));

        // First request - should hit the proxy
        using var result1 = await _test.Client.GetAsync("/api/proxy-cache-passthrough/");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);

        // Update the mock to return different value
        _server.Reset();
        var counter2 = Interlocked.Increment(ref _requestCounter);
        _server
            .Given(Request.Create().WithPath("/api/proxy-cache-passthrough/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody($"{{\"counter\": {counter2}, \"timestamp\": \"{DateTime.UtcNow:O}\"}}"));

        await Task.Delay(10);

        // Second request - should return cached response
        using var result2 = await _test.Client.GetAsync("/api/proxy-cache-passthrough/");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().Be(response2, "cached proxy endpoint should return same value");
    }

    [Fact]
    public async Task Test_Proxy_Cache_With_Key_Same_Key_Returns_Cached()
    {
        _server
            .Given(Request.Create().WithPath("/api/proxy-cache-passthrough-with-key/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody($"{{\"value\": \"first-{Guid.NewGuid()}\"}}"));

        // First request with key=test1
        using var result1 = await _test.Client.GetAsync("/api/proxy-cache-passthrough-with-key/?key=test1");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);

        // Update mock
        _server.Reset();
        _server
            .Given(Request.Create().WithPath("/api/proxy-cache-passthrough-with-key/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody($"{{\"value\": \"second-{Guid.NewGuid()}\"}}"));

        // Second request with same key - should return cached
        using var result2 = await _test.Client.GetAsync("/api/proxy-cache-passthrough-with-key/?key=test1");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().Be(response2, "same cache key should return cached proxy response");
    }

    [Fact]
    public async Task Test_Proxy_Cache_With_Key_Different_Keys_Return_Different()
    {
        var guid1 = Guid.NewGuid().ToString();
        _server
            .Given(Request.Create().WithPath("/api/proxy-cache-passthrough-with-key/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody($"{{\"value\": \"{guid1}\"}}"));

        // First request with key=keyA
        using var result1 = await _test.Client.GetAsync("/api/proxy-cache-passthrough-with-key/?key=keyA");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);

        // Update mock for different response
        _server.Reset();
        var guid2 = Guid.NewGuid().ToString();
        _server
            .Given(Request.Create().WithPath("/api/proxy-cache-passthrough-with-key/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody($"{{\"value\": \"{guid2}\"}}"));

        // Second request with different key - should hit proxy again
        using var result2 = await _test.Client.GetAsync("/api/proxy-cache-passthrough-with-key/?key=keyB");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().NotBe(response2, "different cache keys should return different proxy responses");
    }

    [Fact]
    public async Task Test_Proxy_Cache_Expires_Returns_Fresh_After_Expiration()
    {
        var guid1 = Guid.NewGuid().ToString();
        _server
            .Given(Request.Create().WithPath("/api/proxy-cache-passthrough-expires/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody($"{{\"value\": \"{guid1}\"}}"));

        // First request
        using var result1 = await _test.Client.GetAsync("/api/proxy-cache-passthrough-expires/");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);

        // Wait for cache to expire
        await Task.Delay(1500);

        // Update mock
        _server.Reset();
        var guid2 = Guid.NewGuid().ToString();
        _server
            .Given(Request.Create().WithPath("/api/proxy-cache-passthrough-expires/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody($"{{\"value\": \"{guid2}\"}}"));

        // Second request after expiration - should hit proxy again
        using var result2 = await _test.Client.GetAsync("/api/proxy-cache-passthrough-expires/");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().NotBe(response2, "cache should expire and return fresh proxy response");
    }

    [Fact]
    public async Task Test_Proxy_No_Cache_Returns_Different_On_Each_Call()
    {
        var guid1 = Guid.NewGuid().ToString();
        _server
            .Given(Request.Create().WithPath("/api/proxy-no-cache-passthrough/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody($"{{\"value\": \"{guid1}\"}}"));

        // First request
        using var result1 = await _test.Client.GetAsync("/api/proxy-no-cache-passthrough/");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);

        // Update mock
        _server.Reset();
        var guid2 = Guid.NewGuid().ToString();
        _server
            .Given(Request.Create().WithPath("/api/proxy-no-cache-passthrough/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody($"{{\"value\": \"{guid2}\"}}"));

        // Second request - should hit proxy again (no caching)
        using var result2 = await _test.Client.GetAsync("/api/proxy-no-cache-passthrough/");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().NotBe(response2, "non-cached proxy should return different values");
    }

    [Fact]
    public async Task Test_Proxy_Cache_Preserves_Status_Code()
    {
        _server
            .Given(Request.Create().WithPath("/api/proxy-cache-status-test/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"created\": true}"));

        // First request
        using var result1 = await _test.Client.GetAsync("/api/proxy-cache-status-test/");
        result1?.StatusCode.Should().Be(HttpStatusCode.Created);

        // Update mock to different status
        _server.Reset();
        _server
            .Given(Request.Create().WithPath("/api/proxy-cache-status-test/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"ok\": true}"));

        // Second request - should return cached status code
        using var result2 = await _test.Client.GetAsync("/api/proxy-cache-status-test/");
        result2?.StatusCode.Should().Be(HttpStatusCode.Created, "cached proxy should preserve status code");
    }

    [Fact]
    public async Task Test_Proxy_Cache_Preserves_Content_Type()
    {
        _server
            .Given(Request.Create().WithPath("/api/proxy-cache-content-type-test/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "text/xml")
                .WithBody("<data>test</data>"));

        // First request
        using var result1 = await _test.Client.GetAsync("/api/proxy-cache-content-type-test/");
        var contentType1 = result1?.Content.Headers.ContentType?.ToString();
        contentType1.Should().Contain("text/xml");

        // Update mock to different content type
        _server.Reset();
        _server
            .Given(Request.Create().WithPath("/api/proxy-cache-content-type-test/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"data\": \"test\"}"));

        // Second request - should return cached content type
        using var result2 = await _test.Client.GetAsync("/api/proxy-cache-content-type-test/");
        var contentType2 = result2?.Content.Headers.ContentType?.ToString();
        contentType2.Should().Contain("text/xml", "cached proxy should preserve content type");
    }
}
