namespace NpgsqlRestTests;

public static partial class Database
{
    // Regression coverage for bare `@cached` (no explicit parameter list). It is documented to key on ALL
    // routine parameters; the bug was that it keyed on the routine identifier only, so every call shared one
    // entry. Each function records one row per actual execution so a test can prove cache hits vs misses.
    public static void CacheAllParamsTests()
    {
        script.Append(@"
create table cache_allparams_calls (id int generated always as identity primary key, label text not null);

-- bare `cached` over a routine WITH a param: distinct values must produce distinct cache entries.
create function cache_allparams_search(_p text default null) returns text language sql as $$
    insert into cache_allparams_calls (label) values ('search:' || coalesce(_p, '<null>'));
    select 'r:' || coalesce(_p, '<null>');
$$;
comment on function cache_allparams_search(text) is 'HTTP GET
cached';

-- bare `cached` over a routine with NO params: a single shared entry (one execution across calls).
create function cache_allparams_noparam() returns text language sql as $$
    insert into cache_allparams_calls (label) values ('noparam');
    select 'noparam-result';
$$;
comment on function cache_allparams_noparam() is 'HTTP GET
cached';

-- explicit `cached _x`: only `_x` keys the cache; `_y` does not. Verifies the fix didn't break the list path.
create function cache_allparams_explicit(_x text default null, _y text default null) returns text language sql as $$
    insert into cache_allparams_calls (label) values ('explicit:' || coalesce(_x, '') || ':' || coalesce(_y, ''));
    select 'r';
$$;
comment on function cache_allparams_explicit(text, text) is 'HTTP GET
cached _x';
");
    }
}

[Collection("TestFixture")]
public class CacheAllParamsTests(TestFixture test)
{
    private static async Task<int> CountAsync(string label)
    {
        await using var conn = Database.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*) from cache_allparams_calls where label = $1";
        var p = cmd.CreateParameter();
        p.Value = label;
        cmd.Parameters.Add(p);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Bare_cached_keys_on_all_params_so_distinct_values_get_distinct_entries()
    {
        // First call for p=a executes and caches under a key that includes p.
        using (var a1 = await test.Client.GetAsync("/api/cache-allparams-search/?p=a"))
        {
            a1.StatusCode.Should().Be(HttpStatusCode.OK);
            (await a1.Content.ReadAsStringAsync()).Should().Be("r:a");
        }
        // Same value again → served from cache, no second execution.
        using (var a2 = await test.Client.GetAsync("/api/cache-allparams-search/?p=a"))
            (await a2.Content.ReadAsStringAsync()).Should().Be("r:a");

        // Different value → distinct cache entry → its own execution and its own correct response.
        // (Pre-fix this returned "r:a" from the single routine-keyed entry.)
        using (var b1 = await test.Client.GetAsync("/api/cache-allparams-search/?p=b"))
        {
            b1.StatusCode.Should().Be(HttpStatusCode.OK);
            (await b1.Content.ReadAsStringAsync()).Should().Be("r:b");
        }

        (await CountAsync("search:a")).Should().Be(1, "two p=a calls must collapse to one execution");
        (await CountAsync("search:b")).Should().Be(1, "p=b is a distinct key and must execute on its own");
    }

    [Fact]
    public async Task Bare_cached_with_no_params_uses_a_single_shared_entry()
    {
        using (var r1 = await test.Client.GetAsync("/api/cache-allparams-noparam/"))
            (await r1.Content.ReadAsStringAsync()).Should().Be("noparam-result");
        using (var r2 = await test.Client.GetAsync("/api/cache-allparams-noparam/"))
            (await r2.Content.ReadAsStringAsync()).Should().Be("noparam-result");

        (await CountAsync("noparam")).Should().Be(1, "a paramless cached routine has exactly one cache entry");
    }

    [Fact]
    public async Task Explicit_cached_list_keys_only_the_listed_params()
    {
        // `cached _x` → only _x is in the key. x=a,y=1 then x=a,y=2 share the entry (y is ignored).
        using (var r1 = await test.Client.GetAsync("/api/cache-allparams-explicit/?x=a&y=1"))
            r1.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var r2 = await test.Client.GetAsync("/api/cache-allparams-explicit/?x=a&y=2"))
            r2.StatusCode.Should().Be(HttpStatusCode.OK);
        // Different x → distinct key → executes.
        using (var r3 = await test.Client.GetAsync("/api/cache-allparams-explicit/?x=b&y=1"))
            r3.StatusCode.Should().Be(HttpStatusCode.OK);

        (await CountAsync("explicit:a:1")).Should().Be(1, "x=a executed once; the x=a,y=2 call is a cache hit (y not keyed)");
        (await CountAsync("explicit:a:2")).Should().Be(0, "x=a,y=2 must hit the x=a entry, not execute");
        (await CountAsync("explicit:b:1")).Should().Be(1, "x=b is a distinct key and must execute");
    }
}
