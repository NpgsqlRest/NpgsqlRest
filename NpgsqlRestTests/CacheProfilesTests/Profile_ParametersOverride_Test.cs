using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for Profile_ParametersOverride_Test.
    /// Function annotated with both `cache_profile fast` (whose Parameters=["key"]) AND
    /// `cached other` — the annotation must override the profile's parameter list, so the cache
    /// key uses `other`, not `key`. We prove it by varying `key` (no effect on key) and varying
    /// `other` (different cache entries).
    /// </summary>
    public static void Profile_ParametersOverride_Test()
    {
        script.Append(@"
        create function cp_params_override(key text, other text)
        returns text language plpgsql as $$
        begin
            return key || ':' || other || ':' || gen_random_uuid()::text;
        end;
        $$;
        comment on function cp_params_override(text, text) is '
        HTTP GET
        cache_profile fast
        cached other
        ';
        ");
    }
}

[Collection("CacheProfilesTestFixture")]
public class Profile_ParametersOverride_Test(CacheProfilesTestFixture test)
{
    /// <summary>
    /// `cached other` (annotation) must win over profile's `Parameters: ["key"]`. So the cache
    /// keys by `other`, not `key`. Two calls with same `other` but different `key` → cache hit
    /// (cached value retains the first `key` in its body). Two calls with same `key` but different
    /// `other` → cache miss (different entries).
    /// </summary>
    [Fact]
    public async Task Cached_annotation_parameters_override_profile_Parameters()
    {
        using var client = test.CreateClient();

        using var r1 = await client.GetAsync("/api/cp-params-override/?key=A&other=X");
        var body1 = await r1.Content.ReadAsStringAsync();
        body1.Should().StartWith("A:X:");

        // Same `other`, different `key` → cache hit (returns cached "A:X:UUID" from r1).
        using var r2 = await client.GetAsync("/api/cp-params-override/?key=B&other=X");
        var body2 = await r2.Content.ReadAsStringAsync();
        body2.Should().Be(body1);

        // Different `other` → cache miss → fresh execution → new UUID.
        using var r3 = await client.GetAsync("/api/cp-params-override/?key=A&other=Y");
        var body3 = await r3.Content.ReadAsStringAsync();
        body3.Should().StartWith("A:Y:");
        body3.Should().NotBe(body1);
    }
}
