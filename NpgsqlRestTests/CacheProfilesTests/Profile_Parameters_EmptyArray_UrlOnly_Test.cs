using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for Profile_Parameters_EmptyArray_UrlOnly_Test.
    /// Function takes one param (`p`) and is annotated with `cache_profile url_only`. The
    /// "url_only" profile sets `Parameters: []` (empty array) — explicit "no params in cache key".
    /// Result: ALL calls share a single cache entry per endpoint URL, regardless of `p`.
    /// </summary>
    public static void Profile_Parameters_EmptyArray_UrlOnly_Test()
    {
        script.Append(@"
        create function cp_url_only(p text)
        returns text language plpgsql as $$
        begin
            return p || ':' || gen_random_uuid()::text;
        end;
        $$;
        comment on function cp_url_only(text) is '
        HTTP GET
        cache_profile url_only
        ';
        ");
    }
}

[Collection("CacheProfilesTestFixture")]
public class Profile_Parameters_EmptyArray_UrlOnly_Test(CacheProfilesTestFixture test)
{
    /// <summary>
    /// With `Parameters: []` the profile says "use no parameters in the cache key". Different `p`
    /// values must therefore share the same cache entry — the second call returns the cached
    /// response from the first (including the first call's `p` value).
    /// </summary>
    [Fact]
    public async Task Empty_Parameters_array_means_one_cache_entry_per_endpoint_regardless_of_inputs()
    {
        using var client = test.CreateClient();

        using var r1 = await client.GetAsync("/api/cp-url-only/?p=first");
        var body1 = await r1.Content.ReadAsStringAsync();
        body1.Should().StartWith("first:");

        // Different param value, but cache key has no params → same entry → first response returned.
        using var r2 = await client.GetAsync("/api/cp-url-only/?p=second");
        var body2 = await r2.Content.ReadAsStringAsync();
        body2.Should().Be(body1);
    }
}
