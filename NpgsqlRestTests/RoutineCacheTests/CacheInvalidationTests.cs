namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CacheInvalidationTests()
    {
        script.Append(@"
create function cache_invalidation_test(_key text)
returns text
language sql
as $$
select _key || '_' || random()::text
$$;
comment on function cache_invalidation_test(text) is 'HTTP GET
cached _key';

create function cache_invalidation_no_params()
returns text
language sql
as $$
select random()::text
$$;
comment on function cache_invalidation_no_params() is 'HTTP GET
cached';
");
    }
}

[Collection("TestFixture")]
public class CacheInvalidationTests(TestFixture test)
{
    [Fact]
    public async Task Test_Cache_Invalidation_Returns_True_When_Entry_Exists()
    {
        // First, populate the cache
        using var result1 = await test.Client.GetAsync("/api/cache-invalidation-test/?key=invalidate-test-1");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);
        response1.Should().StartWith("invalidate-test-1_");

        // Verify it's cached
        using var result2 = await test.Client.GetAsync("/api/cache-invalidation-test/?key=invalidate-test-1");
        var response2 = await result2.Content.ReadAsStringAsync();
        response1.Should().Be(response2, "value should be cached");

        // Invalidate the cache
        using var invalidateResult = await test.Client.GetAsync("/api/cache-invalidation-test/invalidate?key=invalidate-test-1");
        var invalidateResponse = await invalidateResult.Content.ReadAsStringAsync();
        invalidateResult?.StatusCode.Should().Be(HttpStatusCode.OK);
        invalidateResponse.Should().Be("{\"invalidated\":true}");

        // Now the cache should be cleared, next call should return different value
        using var result3 = await test.Client.GetAsync("/api/cache-invalidation-test/?key=invalidate-test-1");
        var response3 = await result3.Content.ReadAsStringAsync();
        result3?.StatusCode.Should().Be(HttpStatusCode.OK);
        response3.Should().StartWith("invalidate-test-1_");
        response3.Should().NotBe(response1, "after invalidation, a new value should be returned");
    }

    [Fact]
    public async Task Test_Cache_Invalidation_Returns_False_When_Entry_Does_Not_Exist()
    {
        // Try to invalidate a cache entry that doesn't exist
        var uniqueKey = Guid.NewGuid().ToString("N");
        using var invalidateResult = await test.Client.GetAsync($"/api/cache-invalidation-test/invalidate?key={uniqueKey}");
        var invalidateResponse = await invalidateResult.Content.ReadAsStringAsync();
        invalidateResult?.StatusCode.Should().Be(HttpStatusCode.OK);
        invalidateResponse.Should().Be("{\"invalidated\":false}");
    }

    [Fact]
    public async Task Test_Cache_Invalidation_Different_Keys_Are_Independent()
    {
        // Populate cache for key1
        using var result1 = await test.Client.GetAsync("/api/cache-invalidation-test/?key=ind-key1");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);

        // Populate cache for key2
        using var result2 = await test.Client.GetAsync("/api/cache-invalidation-test/?key=ind-key2");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        // Invalidate only key1
        using var invalidateResult = await test.Client.GetAsync("/api/cache-invalidation-test/invalidate?key=ind-key1");
        invalidateResult?.StatusCode.Should().Be(HttpStatusCode.OK);

        // key2 should still be cached
        using var result2Again = await test.Client.GetAsync("/api/cache-invalidation-test/?key=ind-key2");
        var response2Again = await result2Again.Content.ReadAsStringAsync();
        response2Again.Should().Be(response2, "key2 should still be cached after invalidating key1");

        // key1 should return new value
        using var result1Again = await test.Client.GetAsync("/api/cache-invalidation-test/?key=ind-key1");
        var response1Again = await result1Again.Content.ReadAsStringAsync();
        response1Again.Should().NotBe(response1, "key1 should return new value after invalidation");
    }

    [Fact]
    public async Task Test_Cache_Invalidation_No_Params_Endpoint()
    {
        // Populate the cache
        using var result1 = await test.Client.GetAsync("/api/cache-invalidation-no-params/");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify it's cached
        using var result2 = await test.Client.GetAsync("/api/cache-invalidation-no-params/");
        var response2 = await result2.Content.ReadAsStringAsync();
        response1.Should().Be(response2, "value should be cached");

        // Invalidate
        using var invalidateResult = await test.Client.GetAsync("/api/cache-invalidation-no-params/invalidate");
        var invalidateResponse = await invalidateResult.Content.ReadAsStringAsync();
        invalidateResult?.StatusCode.Should().Be(HttpStatusCode.OK);
        invalidateResponse.Should().Be("{\"invalidated\":true}");

        // Should return new value
        using var result3 = await test.Client.GetAsync("/api/cache-invalidation-no-params/");
        var response3 = await result3.Content.ReadAsStringAsync();
        response3.Should().NotBe(response1, "after invalidation, a new value should be returned");
    }

    [Fact]
    public async Task Test_Cache_Invalidation_Multiple_Times()
    {
        // First invalidation (cache doesn't exist yet)
        using var inv1 = await test.Client.GetAsync("/api/cache-invalidation-test/invalidate?key=multi-inv");
        var invResponse1 = await inv1.Content.ReadAsStringAsync();
        invResponse1.Should().Be("{\"invalidated\":false}");

        // Populate cache
        using var result1 = await test.Client.GetAsync("/api/cache-invalidation-test/?key=multi-inv");
        var response1 = await result1.Content.ReadAsStringAsync();

        // Second invalidation (cache exists)
        using var inv2 = await test.Client.GetAsync("/api/cache-invalidation-test/invalidate?key=multi-inv");
        var invResponse2 = await inv2.Content.ReadAsStringAsync();
        invResponse2.Should().Be("{\"invalidated\":true}");

        // Third invalidation (cache was just cleared)
        using var inv3 = await test.Client.GetAsync("/api/cache-invalidation-test/invalidate?key=multi-inv");
        var invResponse3 = await inv3.Content.ReadAsStringAsync();
        invResponse3.Should().Be("{\"invalidated\":false}");
    }
}
