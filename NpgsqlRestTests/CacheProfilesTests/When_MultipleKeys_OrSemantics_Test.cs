using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for When_MultipleKeys_OrSemantics_Test.
    /// Profile "skip_to_or_format" has `When: { "end_date": null, "format": "csv" }` —
    /// bypass cache when EITHER `end_date` is null OR `format` equals "csv".
    /// </summary>
    public static void When_MultipleKeys_OrSemantics_Test()
    {
        script.Append(@"
        create function cp_skip_multi_keys(end_date text default null, format text default 'json')
        returns text language plpgsql as $$
        begin
            return coalesce(end_date, 'null') || ':' || format || ':' || gen_random_uuid()::text;
        end;
        $$;
        comment on function cp_skip_multi_keys(text, text) is '
        HTTP GET
        cache_profile skip_to_or_format
        ';
        ");
    }
}

[Collection("CacheProfilesTestFixture")]
public class When_MultipleKeys_OrSemantics_Test(CacheProfilesTestFixture test)
{
    /// <summary>
    /// Multiple keys in When rules are combined with OR. Three scenarios verified:
    ///   1. Only `end_date` matches null → bypass.
    ///   2. Only `format` matches "csv" → bypass.
    ///   3. Neither matches → cache works.
    /// </summary>
    [Fact]
    public async Task Multiple_When_keys_combine_with_OR()
    {
        using var client = test.CreateClient();

        // (1) end_date=null, format=json → matches first key (end_date null) → bypass
        using var a1 = await client.GetAsync("/api/cp-skip-multi-keys/?format=json");
        var ab1 = await a1.Content.ReadAsStringAsync();
        ab1.Should().StartWith("null:json:");

        using var a2 = await client.GetAsync("/api/cp-skip-multi-keys/?format=json");
        var ab2 = await a2.Content.ReadAsStringAsync();
        ab2.Should().NotBe(ab1, "end_date=null matches → bypass → fresh UUIDs");

        // (2) end_date=2024-01-01, format=csv → matches second key (format csv) → bypass
        using var b1 = await client.GetAsync("/api/cp-skip-multi-keys/?endDate=2024-01-01&format=csv");
        var bb1 = await b1.Content.ReadAsStringAsync();
        bb1.Should().StartWith("2024-01-01:csv:");

        using var b2 = await client.GetAsync("/api/cp-skip-multi-keys/?endDate=2024-01-01&format=csv");
        var bb2 = await b2.Content.ReadAsStringAsync();
        bb2.Should().NotBe(bb1, "format=csv matches → bypass → fresh UUIDs");

        // (3) Neither matches → cache active
        using var c1 = await client.GetAsync("/api/cp-skip-multi-keys/?endDate=2024-02-02&format=json");
        var cb1 = await c1.Content.ReadAsStringAsync();
        cb1.Should().StartWith("2024-02-02:json:");

        using var c2 = await client.GetAsync("/api/cp-skip-multi-keys/?endDate=2024-02-02&format=json");
        var cb2 = await c2.Content.ReadAsStringAsync();
        cb2.Should().Be(cb1, "neither key matches → cache normally → second call hits");
    }
}
