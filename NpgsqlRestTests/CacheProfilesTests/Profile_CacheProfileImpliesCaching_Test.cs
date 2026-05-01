using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for Profile_CacheProfileImpliesCaching_Test.
    /// Function annotated with `cache_profile slow` only — NO `cached` annotation. The
    /// `cache_profile` annotation must imply caching on its own; otherwise users would have to
    /// write `cached` everywhere they reference a profile, which is redundant.
    /// </summary>
    public static void Profile_CacheProfileImpliesCaching_Test()
    {
        script.Append(@"
        create function cp_implies_caching()
        returns text language plpgsql as $$
        begin
            return gen_random_uuid()::text;
        end;
        $$;
        comment on function cp_implies_caching() is '
        HTTP GET
        cache_profile slow
        ';
        ");
    }
}

[Collection("CacheProfilesTestFixture")]
public class Profile_CacheProfileImpliesCaching_Test(CacheProfilesTestFixture test)
{
    /// <summary>
    /// `cache_profile slow` (no `cached`) must still produce a cached endpoint. Two calls return
    /// the same UUID — proving the profile annotation alone is enough to enable caching.
    /// </summary>
    [Fact]
    public async Task Cache_profile_annotation_alone_enables_caching_without_cached_annotation()
    {
        using var client = test.CreateClient();

        using var r1 = await client.GetAsync("/api/cp-implies-caching/");
        var body1 = await r1.Content.ReadAsStringAsync();

        using var r2 = await client.GetAsync("/api/cp-implies-caching/");
        var body2 = await r2.Content.ReadAsStringAsync();
        body2.Should().Be(body1);
    }
}
