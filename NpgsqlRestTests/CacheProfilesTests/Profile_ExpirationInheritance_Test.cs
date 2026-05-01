using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for Profile_ExpirationInheritance_Test.
    /// Function annotated with `cache_profile short_ttl` only — no `cache_expires` annotation.
    /// The "short_ttl" profile (1-second expiration) should be inherited.
    /// </summary>
    public static void Profile_ExpirationInheritance_Test()
    {
        script.Append(@"
        create function cp_short_ttl_inherited()
        returns text language plpgsql as $$
        begin
            return gen_random_uuid()::text;
        end;
        $$;
        comment on function cp_short_ttl_inherited() is '
        HTTP GET
        cache_profile short_ttl
        ';
        ");
    }
}

[Collection("CacheProfilesTestFixture")]
public class Profile_ExpirationInheritance_Test(CacheProfilesTestFixture test)
{
    /// <summary>
    /// When the endpoint has no `cache_expires` annotation, the profile's `Expiration` value
    /// (1 second in the fixture's "short_ttl" profile) should be inherited. We prove this by:
    ///   1. Calling the endpoint (writes cache).
    ///   2. Calling immediately again (cache hit, same UUID).
    ///   3. Waiting > 1 second and calling once more (cache expired, fresh UUID).
    /// </summary>
    [Fact]
    public async Task Endpoint_inherits_profile_Expiration_when_cache_expires_annotation_is_absent()
    {
        using var client = test.CreateClient();

        using var r1 = await client.GetAsync("/api/cp-short-ttl-inherited/");
        var body1 = await r1.Content.ReadAsStringAsync();

        using var r2 = await client.GetAsync("/api/cp-short-ttl-inherited/");
        var body2 = await r2.Content.ReadAsStringAsync();
        body2.Should().Be(body1, "second call within TTL must hit cache");

        await Task.Delay(TimeSpan.FromSeconds(2));

        using var r3 = await client.GetAsync("/api/cp-short-ttl-inherited/");
        var body3 = await r3.Content.ReadAsStringAsync();
        body3.Should().NotBe(body1, "after TTL expires, a fresh UUID should be returned");
    }
}
