using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// Setup for When_ArrayOfValues_OrSemantics_Test.
    /// Profile "skip_status_array" has `When: { "status": [null, ""] }` — bypass on null OR empty.
    /// </summary>
    public static void When_ArrayOfValues_OrSemantics_Test()
    {
        script.Append(@"
        create function cp_skip_status_array(status text default null)
        returns text language plpgsql as $$
        begin
            return 'len=' || coalesce(length(status)::text, 'null') || ':' || gen_random_uuid()::text;
        end;
        $$;
        comment on function cp_skip_status_array(text) is '
        HTTP GET
        cache_profile skip_status_array
        ';
        ");
    }
}

[Collection("CacheProfilesTestFixture")]
public class When_ArrayOfValues_OrSemantics_Test(CacheProfilesTestFixture test)
{
    /// <summary>
    /// `When: { "status": [null, ""] }` matches if status is null OR empty string.
    /// Confirms the OR semantics within an array. We test both branches:
    ///   - null status → bypass
    ///   - empty string status → bypass
    ///   - non-empty value → cache works (regression check)
    /// </summary>
    [Fact]
    public async Task Array_in_When_matches_any_listed_value()
    {
        using var client = test.CreateClient();

        // null status → bypass
        using var n1 = await client.GetAsync("/api/cp-skip-status-array/");
        var nb1 = await n1.Content.ReadAsStringAsync();
        nb1.Should().StartWith("len=null:");

        using var n2 = await client.GetAsync("/api/cp-skip-status-array/");
        var nb2 = await n2.Content.ReadAsStringAsync();
        nb2.Should().NotBe(nb1, "null matches array entry → bypass on both calls → fresh UUIDs");

        // empty string status → bypass
        using var e1 = await client.GetAsync("/api/cp-skip-status-array/?status=");
        var eb1 = await e1.Content.ReadAsStringAsync();
        eb1.Should().StartWith("len=0:");

        using var e2 = await client.GetAsync("/api/cp-skip-status-array/?status=");
        var eb2 = await e2.Content.ReadAsStringAsync();
        eb2.Should().NotBe(eb1, "empty string matches array entry \"\" → bypass on both calls → fresh UUIDs");

        // non-empty status — does NOT match either array entry → caches as normal
        using var v1 = await client.GetAsync("/api/cp-skip-status-array/?status=active");
        var vb1 = await v1.Content.ReadAsStringAsync();
        vb1.Should().StartWith("len=6:");

        using var v2 = await client.GetAsync("/api/cp-skip-status-array/?status=active");
        var vb2 = await v2.Content.ReadAsStringAsync();
        vb2.Should().Be(vb1, "non-matching value → cache active → second call hits cache");
    }
}
