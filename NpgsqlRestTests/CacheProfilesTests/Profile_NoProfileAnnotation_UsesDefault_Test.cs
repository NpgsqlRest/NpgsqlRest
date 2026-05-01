using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for Profile_NoProfileAnnotation_UsesDefault_Test.
    /// Function annotated with `cached` only — no `cache_profile`. Should route through the
    /// default (root) cache, exactly as in pre-3.13 behavior. This is a regression check.
    /// </summary>
    public static void Profile_NoProfileAnnotation_UsesDefault_Test()
    {
        script.Append(@"
        create function cp_no_profile_annotation()
        returns text language plpgsql as $$
        begin
            return gen_random_uuid()::text;
        end;
        $$;
        comment on function cp_no_profile_annotation() is '
        HTTP GET
        cached
        ';
        ");
    }
}

[Collection("CacheProfilesTestFixture")]
public class Profile_NoProfileAnnotation_UsesDefault_Test(CacheProfilesTestFixture test)
{
    /// <summary>
    /// An endpoint with bare `cached` (no `cache_profile`) must continue to use the root cache.
    /// Two calls return the same UUID → confirms caching still works through the default path
    /// even when profiles are configured on the side.
    /// </summary>
    [Fact]
    public async Task Endpoint_without_cache_profile_annotation_uses_default_root_cache()
    {
        using var client = test.CreateClient();

        using var r1 = await client.GetAsync("/api/cp-no-profile-annotation/");
        var body1 = await r1.Content.ReadAsStringAsync();

        using var r2 = await client.GetAsync("/api/cp-no-profile-annotation/");
        var body2 = await r2.Content.ReadAsStringAsync();
        body2.Should().Be(body1);
    }
}
