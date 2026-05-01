using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for When_NullValue_BypassesCache_Test.
    /// Function takes a `end_date text default null` parameter. Annotated with `cache_profile skip_to`.
    /// The "skip_to" profile has `When: { "end_date": null }` — so any call where end_date is
    /// null/DBNull bypasses the cache entirely (no read, no write — fresh execution).
    ///
    /// Default value `null` lets us send the parameter as missing in the request, which then resolves
    /// to DBNull at runtime, exercising the bypass path.
    /// </summary>
    public static void When_NullValue_BypassesCache_Test()
    {
        script.Append(@"
        create function cp_skip_when_to_null(end_date text default null)
        returns text language plpgsql as $$
        begin
            return coalesce(end_date, 'null') || ':' || gen_random_uuid()::text;
        end;
        $$;
        comment on function cp_skip_when_to_null(text) is '
        HTTP GET
        cache_profile skip_to
        ';
        ");
    }
}

[Collection("CacheProfilesTestFixture")]
public class When_NullValue_BypassesCache_Test(CacheProfilesTestFixture test)
{
    /// <summary>
    /// Two consecutive calls without `end_date` (parameter resolves to DBNull) must each return
    /// a fresh UUID — proving the cache was bypassed on both reads (no entry was read) and writes
    /// (no entry was stored, otherwise the second call would have hit the cache).
    /// </summary>
    [Fact]
    public async Task Null_param_value_matching_When_null_bypasses_cache_on_both_calls()
    {
        using var client = test.CreateClient();

        using var r1 = await client.GetAsync("/api/cp-skip-when-to-null/");
        var body1 = await r1.Content.ReadAsStringAsync();
        body1.Should().StartWith("null:");

        using var r2 = await client.GetAsync("/api/cp-skip-when-to-null/");
        var body2 = await r2.Content.ReadAsStringAsync();
        body2.Should().StartWith("null:");
        body2.Should().NotBe(body1, "skip-when-null bypassed both reads and writes — second call must compute fresh");
    }
}
