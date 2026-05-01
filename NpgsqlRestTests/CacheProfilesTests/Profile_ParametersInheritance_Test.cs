using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for Profile_ParametersInheritance_Test.
    /// Function takes two params (`key`, `other`); annotated with `cache_profile fast` only — no
    /// `cached` annotation. The "fast" profile sets `Parameters: ["key"]`, so only `key` should
    /// participate in the cache key. Different `other` values with the same `key` must hit cache.
    /// </summary>
    public static void Profile_ParametersInheritance_Test()
    {
        script.Append(@"
        create function cp_params_inheritance(key text, other text)
        returns text language plpgsql as $$
        begin
            return key || ':' || other || ':' || gen_random_uuid()::text;
        end;
        $$;
        comment on function cp_params_inheritance(text, text) is '
        HTTP GET
        cache_profile fast
        ';
        ");
    }
}

[Collection("CacheProfilesTestFixture")]
public class Profile_ParametersInheritance_Test(CacheProfilesTestFixture test)
{
    /// <summary>
    /// The endpoint inherits the profile's `Parameters: ["key"]` list because it has no
    /// `cached p1, p2` annotation of its own. We verify by calling with the same `key` but
    /// different `other` values: both calls must return the SAME response (same UUID), proving
    /// `other` is NOT part of the cache key.
    /// </summary>
    [Fact]
    public async Task Endpoint_uses_profile_Parameters_when_cached_annotation_is_absent()
    {
        using var client = test.CreateClient();

        using var r1 = await client.GetAsync("/api/cp-params-inheritance/?key=abc&other=foo");
        var body1 = await r1.Content.ReadAsStringAsync();
        body1.Should().StartWith("abc:foo:");

        using var r2 = await client.GetAsync("/api/cp-params-inheritance/?key=abc&other=bar");
        var body2 = await r2.Content.ReadAsStringAsync();
        // Cache hit: returns the cached value from r1, which has "other=foo" and the original UUID.
        body2.Should().Be(body1);
    }
}
