using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for Profile_Parameters_Missing_AllParams_Test.
    /// Function takes two params; annotated with `cache_profile all_params`. The "all_params"
    /// profile has `Parameters: null` (missing) — meaning "use ALL routine parameters" as the
    /// cache key. Different combinations of `a`/`b` must produce different cache entries.
    /// </summary>
    public static void Profile_Parameters_Missing_AllParams_Test()
    {
        script.Append(@"
        create function cp_all_params(a text, b text)
        returns text language plpgsql as $$
        begin
            return a || ':' || b || ':' || gen_random_uuid()::text;
        end;
        $$;
        comment on function cp_all_params(text, text) is '
        HTTP GET
        cache_profile all_params
        ';
        ");
    }
}

[Collection("CacheProfilesTestFixture")]
public class Profile_Parameters_Missing_AllParams_Test(CacheProfilesTestFixture test)
{
    /// <summary>
    /// Profile has no `Parameters` set → the cache key includes every routine parameter. Same
    /// `(a, b)` returns the cached value; changing either `a` or `b` produces a fresh entry.
    /// </summary>
    [Fact]
    public async Task Null_Parameters_means_all_routine_parameters_participate_in_cache_key()
    {
        using var client = test.CreateClient();

        using var r1 = await client.GetAsync("/api/cp-all-params/?a=1&b=2");
        var body1 = await r1.Content.ReadAsStringAsync();
        body1.Should().StartWith("1:2:");

        // Same a, b → cache hit
        using var r2 = await client.GetAsync("/api/cp-all-params/?a=1&b=2");
        var body2 = await r2.Content.ReadAsStringAsync();
        body2.Should().Be(body1);

        // Different a → cache miss
        using var r3 = await client.GetAsync("/api/cp-all-params/?a=99&b=2");
        var body3 = await r3.Content.ReadAsStringAsync();
        body3.Should().StartWith("99:2:");
        body3.Should().NotBe(body1);

        // Different b → cache miss
        using var r4 = await client.GetAsync("/api/cp-all-params/?a=1&b=88");
        var body4 = await r4.Content.ReadAsStringAsync();
        body4.Should().StartWith("1:88:");
        body4.Should().NotBe(body1);
    }
}
