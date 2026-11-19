namespace NpgsqlRestTests;

/// <summary>
/// Security regression: the in-memory response cache and the per-key gates of the set-returning cache path are
/// bounded. Cache keys contain request values, so both used to grow by one entry per distinct value a caller sent
/// (and cache entries without an expiration were never removed).
/// </summary>
[Collection("TestFixture")]
public class CacheMemoryBoundsTests(TestFixture test)
{
    [Fact]
    public void Memory_cache_does_not_admit_new_keys_when_full()
    {
        var options = RoutineCache.CurrentOptions;
        var saved = options.MemoryCacheMaxEntries;
        var cache = new RoutineCache();
        var prefix = Guid.NewGuid().ToString("N");
        try
        {
            options.MemoryCacheMaxEntries = RoutineCache.EntryCount + 2;
            var ttl = TimeSpan.FromMinutes(1);
            cache.AddOrUpdate(null!, prefix + "1", "one", ttl);
            cache.AddOrUpdate(null!, prefix + "2", "two", ttl);
            cache.AddOrUpdate(null!, prefix + "3", "three", ttl); // full: not admitted

            cache.Get(null!, prefix + "1", out var one).Should().BeTrue();
            one.Should().Be("one");
            cache.Get(null!, prefix + "3", out _).Should().BeFalse();

            // an existing key can still be updated when full
            cache.AddOrUpdate(null!, prefix + "2", "two-updated", ttl);
            cache.Get(null!, prefix + "2", out var two).Should().BeTrue();
            two.Should().Be("two-updated");
        }
        finally
        {
            options.MemoryCacheMaxEntries = saved;
            cache.Remove(prefix + "1");
            cache.Remove(prefix + "2");
        }
    }

    [Fact]
    public async Task Set_cache_gates_are_removed_after_the_requests()
    {
        for (var i = 1; i <= 5; i++)
        {
            using var response = await test.Client.GetAsync($"/api/cache-get-set/?count={100 + i}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        NpgsqlRestEndpoint.RecordCacheGateCount.Should().Be(0);
    }
}
