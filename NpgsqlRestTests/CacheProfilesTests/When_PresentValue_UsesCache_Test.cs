using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for When_PresentValue_UsesCache_Test.
    /// Same function as When_NullValue_BypassesCache_Test (bypass when end_date is null). Here
    /// we send a non-null value, so the When rule condition does NOT trigger and the cache works
    /// as normal.
    /// </summary>
    public static void When_PresentValue_UsesCache_Test()
    {
        script.Append(@"
        create function cp_skip_when_present(end_date text default null)
        returns text language plpgsql as $$
        begin
            return coalesce(end_date, 'null') || ':' || gen_random_uuid()::text;
        end;
        $$;
        comment on function cp_skip_when_present(text) is '
        HTTP GET
        cache_profile skip_to
        ';
        ");
    }
}

[Collection("CacheProfilesTestFixture")]
public class When_PresentValue_UsesCache_Test(CacheProfilesTestFixture test)
{
    /// <summary>
    /// When the When rule condition does NOT match the parameter value, the cache works normally.
    /// Two calls with the same `end_date=2024-01-01` must return the same response (cache hit).
    /// </summary>
    [Fact]
    public async Task Non_null_end_date_does_not_match_When_so_cache_works_normally()
    {
        using var client = test.CreateClient();

        using var r1 = await client.GetAsync("/api/cp-skip-when-present/?endDate=2024-01-01");
        var body1 = await r1.Content.ReadAsStringAsync();
        body1.Should().StartWith("2024-01-01:");

        using var r2 = await client.GetAsync("/api/cp-skip-when-present/?endDate=2024-01-01");
        var body2 = await r2.Content.ReadAsStringAsync();
        body2.Should().Be(body1);
    }
}
