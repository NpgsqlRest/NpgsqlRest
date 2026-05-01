using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for Profile_BasicResolution_Test.
    /// SQL function returns "<key>:<random uuid>" so we can detect cache hit/miss across calls.
    /// Annotated with `cache_profile fast` — the fixture's "fast" profile uses Parameters=["key"]
    /// and an in-memory backend separate from the root cache.
    /// </summary>
    public static void Profile_BasicResolution_Test()
    {
        script.Append(@"
        create function cp_basic_resolution(key text)
        returns text language plpgsql as $$
        begin
            return key || ':' || gen_random_uuid()::text;
        end;
        $$;
        comment on function cp_basic_resolution(text) is '
        HTTP GET
        cache_profile fast
        ';
        ");
    }
}

[Collection("CacheProfilesTestFixture")]
public class Profile_BasicResolution_Test(CacheProfilesTestFixture test)
{
    /// <summary>
    /// Smoke test: an endpoint annotated with `cache_profile fast` actually caches the response.
    /// Two consecutive calls with the same `key` parameter should return identical responses
    /// (proving the entry was written on the first call and read on the second).
    /// `cache_profile` implies caching even without a separate `cached` annotation.
    /// </summary>
    [Fact]
    public async Task Cache_profile_routes_through_profile_backend()
    {
        using var client = test.CreateClient();

        using var r1 = await client.GetAsync("/api/cp-basic-resolution/?key=abc");
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        var body1 = await r1.Content.ReadAsStringAsync();
        body1.Should().StartWith("abc:");

        using var r2 = await client.GetAsync("/api/cp-basic-resolution/?key=abc");
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
        var body2 = await r2.Content.ReadAsStringAsync();
        body2.Should().Be(body1);
    }
}
