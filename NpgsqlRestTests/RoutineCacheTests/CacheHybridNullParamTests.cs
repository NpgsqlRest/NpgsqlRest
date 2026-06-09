using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    // HybridCache null-parameter cache-key fix. Functions in the shared `public` schema, `chn_` prefix
    // (the CacheHybridNullTestFixture maps only `chn_%`). Each cached function records one row per actual
    // execution so a test can prove the second (cache-hit) call did NOT re-execute. Pre-fix, the \x00
    // null marker made HybridCache reject the key and silently bypass the cache, so the count would be 2.
    public static void CacheHybridNullParamTests()
    {
        script.Append(@"
create table chn_scalar_calls (id int generated always as identity primary key);
create function chn_scalar(a int default null, b int default null) returns text language sql as $$
    insert into chn_scalar_calls default values;
    select 'scalar-result';
$$;
comment on function chn_scalar(int, int) is '
HTTP GET
cached a, b
cache_expires_in 30s';
create function chn_scalar_count() returns bigint language sql as 'select count(*) from chn_scalar_calls';
comment on function chn_scalar_count() is 'HTTP GET';

create table chn_array_calls (id int generated always as identity primary key);
create function chn_array(thing_ids int[] default null, exp_ids int[] default null) returns text language sql as $$
    insert into chn_array_calls default values;
    select 'array-result';
$$;
comment on function chn_array(int[], int[]) is '
HTTP GET
cached thing_ids, exp_ids
cache_expires_in 30s';
create function chn_array_count() returns bigint language sql as 'select count(*) from chn_array_calls';
comment on function chn_array_count() is 'HTTP GET';
");
    }
}

/// <summary>
/// Regression tests for the HybridCache "Cache key contains invalid content" bug: a cached routine with a
/// nullable parameter, called with that parameter null, built a cache key containing the \x00 null marker,
/// which HybridCache rejected — silently bypassing the cache. The fix hashes every key in the wrapper (and
/// drops \x00 from the marker source-side). These prove the call succeeds AND is served from cache the
/// second time (the per-execution counter stays at 1).
/// </summary>
[Collection("CacheHybridNullFixture")]
public class CacheHybridNullParamTests(CacheHybridNullTestFixture test)
{
    [Fact]
    public async Task Nullable_scalar_param_null_does_not_throw_and_caches()
    {
        using var client = test.CreateClient();

        // `b` absent -> null -> the null marker goes into the cache key.
        using var r1 = await client.GetAsync("/api/chn-scalar?a=1");
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r1.Content.ReadAsStringAsync()).Should().Be("scalar-result");

        using var r2 = await client.GetAsync("/api/chn-scalar?a=1");
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r2.Content.ReadAsStringAsync()).Should().Be("scalar-result");

        using var count = await client.GetAsync("/api/chn-scalar-count");
        (await count.Content.ReadAsStringAsync()).Should().Be("1");  // second call was a cache hit, not a re-run
    }

    [Fact]
    public async Task Nullable_array_param_null_does_not_throw_and_caches()
    {
        using var client = test.CreateClient();

        // The original reproducer: `expIds` absent -> null array element of the cache key.
        using var r1 = await client.GetAsync("/api/chn-array?thingIds=43");
        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r1.Content.ReadAsStringAsync()).Should().Be("array-result");

        using var r2 = await client.GetAsync("/api/chn-array?thingIds=43");
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r2.Content.ReadAsStringAsync()).Should().Be("array-result");

        using var count = await client.GetAsync("/api/chn-array-count");
        (await count.Content.ReadAsStringAsync()).Should().Be("1");
    }
}
