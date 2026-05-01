using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for When_NullDoesNotMatchEmptyString_Test.
    /// Same shape as When_NullValue_BypassesCache_Test: profile bypasses when `end_date` is null.
    /// Here we send `?endDate=` (empty string) — JSON null in When rules must NOT match an empty
    /// string parameter value (per the design: empty string is a value, not null).
    /// </summary>
    public static void When_NullDoesNotMatchEmptyString_Test()
    {
        script.Append(@"
        create function cp_skip_when_null_vs_empty(end_date text default null)
        returns text language plpgsql as $$
        begin
            -- Show the literal length so we can distinguish '' (cached) from null (bypass).
            return 'len=' || coalesce(length(end_date)::text, 'null') || ':' || gen_random_uuid()::text;
        end;
        $$;
        comment on function cp_skip_when_null_vs_empty(text) is '
        HTTP GET
        cache_profile skip_to
        ';
        ");
    }
}

[Collection("CacheProfilesTestFixture")]
public class When_NullDoesNotMatchEmptyString_Test(CacheProfilesTestFixture test)
{
    /// <summary>
    /// When rule with `null` Value condition should NOT trigger when the parameter value is an empty string.
    /// We call twice with `?endDate=` and expect the same response — proving the cache was active
    /// (write on call 1, hit on call 2). If the bypass had triggered, the two UUIDs would differ.
    /// </summary>
    [Fact]
    public async Task When_null_does_not_match_empty_string_param_value()
    {
        using var client = test.CreateClient();

        using var r1 = await client.GetAsync("/api/cp-skip-when-null-vs-empty/?endDate=");
        var body1 = await r1.Content.ReadAsStringAsync();
        body1.Should().StartWith("len=0:", "end_date should be empty string (length 0), not null");

        using var r2 = await client.GetAsync("/api/cp-skip-when-null-vs-empty/?endDate=");
        var body2 = await r2.Content.ReadAsStringAsync();
        body2.Should().Be(body1, "empty string did not match When-rule null Value condition; cache should be active");
    }
}
