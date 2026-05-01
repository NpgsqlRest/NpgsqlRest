using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for When_DynamicTtlOverride_Test.
    /// Function takes a `tier` parameter; annotated with `cache_profile tier_ttl`. The "tier_ttl" profile has
    /// two When rules:
    ///   - tier="free" → ThenExpiration = 1 second
    ///   - tier="pro"  → ThenExpiration = 1 hour
    /// We verify that different tier values cache with different TTLs by waiting and observing expiry.
    /// </summary>
    public static void When_DynamicTtlOverride_Test()
    {
        script.Append(@"
        create function cp_dynamic_ttl(tier text)
        returns text language plpgsql as $$
        begin
            return tier || ':' || gen_random_uuid()::text;
        end;
        $$;
        comment on function cp_dynamic_ttl(text) is '
        HTTP GET
        cache_profile tier_ttl
        ';
        ");
    }
}

[Collection("CacheProfilesTestFixture")]
public class When_DynamicTtlOverride_Test(CacheProfilesTestFixture test)
{
    /// <summary>
    /// `tier=free` matches the first rule → entry written with 1-second TTL → expires almost immediately.
    /// `tier=pro` matches the second rule → entry written with 1-hour TTL → still alive after a 2-second wait.
    /// This is the headline use case for the When-rule "dynamic TTL" feature: tier-based cache lifetimes
    /// without separate endpoints or profiles.
    /// </summary>
    [Fact]
    public async Task When_rule_Then_interval_overrides_default_expiration_per_request()
    {
        using var client = test.CreateClient();

        // tier=pro → 1h TTL → cache hit on second call (well within TTL)
        using var p1 = await client.GetAsync("/api/cp-dynamic-ttl/?tier=pro");
        var pb1 = await p1.Content.ReadAsStringAsync();
        pb1.Should().StartWith("pro:");

        using var p2 = await client.GetAsync("/api/cp-dynamic-ttl/?tier=pro");
        var pb2 = await p2.Content.ReadAsStringAsync();
        pb2.Should().Be(pb1, "pro tier has 1-hour TTL — second call must hit cache");

        // tier=free → 1s TTL → after waiting 2 seconds, the entry must be expired
        using var f1 = await client.GetAsync("/api/cp-dynamic-ttl/?tier=free");
        var fb1 = await f1.Content.ReadAsStringAsync();
        fb1.Should().StartWith("free:");

        await Task.Delay(TimeSpan.FromSeconds(2));

        using var f2 = await client.GetAsync("/api/cp-dynamic-ttl/?tier=free");
        var fb2 = await f2.Content.ReadAsStringAsync();
        fb2.Should().NotBe(fb1, "free tier has 1-second TTL — after 2s the entry must have expired and been refreshed");

        // pro should still be cached after the wait (sanity check that the per-rule TTL really differs)
        using var p3 = await client.GetAsync("/api/cp-dynamic-ttl/?tier=pro");
        var pb3 = await p3.Content.ReadAsStringAsync();
        pb3.Should().Be(pb1, "pro tier's 1-hour TTL is unaffected by the 2-second wait");
    }
}
