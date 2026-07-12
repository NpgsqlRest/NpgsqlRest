using Microsoft.Extensions.Logging;
using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    // Main-database side of the per-connection discovery tests (`pcd_` prefix - the
    // PerConnectionDiscoveryTestFixture maps only `pcd_%`; the default test fixture excludes `pcd%`).
    // The second database's schema lives in PerConnectionDiscoveryTestFixture.AltDatabaseScript and is
    // deliberately DIFFERENT: pcd_alt_only exists only there, pcd_shared exists on both (collision),
    // and pcd_comp has one extra field there.
    public static void PerConnectionDiscoveryTests()
    {
        script.Append("""
-- main-only endpoint: proves main discovery is unaffected by the alt source. (#1)
create function pcd_main(_x int) returns text language sql as 'select current_database()';
comment on function pcd_main(int) is 'HTTP GET';

-- exists on BOTH databases with the same signature -> cross-source path collision, alt wins (registered last). (#3)
create function pcd_shared() returns text language sql as 'select ''from-main''';
comment on function pcd_shared() is 'HTTP GET';

-- same-named composite type as on the alt database but with FEWER fields - pins the per-database
-- composite type cache (before the fix the second database''s types never loaded). (#4)
create type pcd_comp as (a int, b text);
create function pcd_main_comp() returns table (val pcd_comp) language sql as 'select row(1, ''x'')::pcd_comp';
comment on function pcd_main_comp() is 'HTTP GET';
""");
    }
}

[Collection("PerConnectionDiscoveryFixture")]
public class PerConnectionDiscoveryTests(PerConnectionDiscoveryTestFixture test)
{
    // #1 — main-database endpoints are unaffected by the additional source
    [Fact]
    public async Task Main_endpoint_executes_on_the_main_database()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/pcd-main/?x=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("npgsql_rest_test");
    }

    // #2 — an endpoint discovered from the alt connection executes ON the alt connection (the OLTP/OLAP case)
    [Fact]
    public async Task Alt_only_endpoint_is_discovered_and_executes_on_the_alt_database()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/pcd-alt-only/?x=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // the routine exists ONLY on the second database - discovered there, executed there
        (await response.Content.ReadAsStringAsync()).Should().Be("npgsql_rest_test_pcd_alt");
    }

    // #2 — an explicit `connection` annotation on an alt-discovered routine overrides the source connection
    [Fact]
    public async Task Connection_annotation_overrides_the_source_connection()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/pcd-alt-override/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // discovered from "alt", but its comment says `connection alt2` (same database, ApplicationName=alt2)
        (await response.Content.ReadAsStringAsync()).Should().Be("alt2");
    }

    // #3 — same path from both sources: the later source wins and a warning names both
    [Fact]
    public async Task Cross_source_collision_last_source_wins()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/pcd-shared/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("from-alt");
    }

    [Fact]
    public void Cross_source_collision_logs_a_warning()
    {
        test.StartupLogs.Any(l =>
            l.Level == LogLevel.Warning &&
            l.Message.Contains("pcd-shared") &&
            l.Message.Contains("conflicts with an endpoint from source"))
            .Should().BeTrue("the same path discovered from two sources must be loudly reported");
    }

    // #4 — composite types resolve per database: same type name, different shapes
    [Fact]
    public async Task Composite_types_resolve_against_each_source_database()
    {
        using var client = test.CreateClient();

        using var mainResponse = await client.GetAsync("/api/pcd-main-comp/");
        mainResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await mainResponse.Content.ReadAsStringAsync()).Should().Be("""[{"val":{"a":1,"b":"x"}}]""");

        using var altResponse = await client.GetAsync("/api/pcd-alt-comp/");
        altResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        // the alt database's pcd_comp has the extra "c" field - before the per-database composite
        // cache fix, the alt source saw the main database's 2-field type (or none at all)
        (await altResponse.Content.ReadAsStringAsync()).Should().Be("""[{"val":{"a":1,"b":"x","c":2}}]""");
    }
}
