namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CacheStampedeTests()
    {
        // Each test uses its own function/endpoint so the process-wide static memory cache from one
        // test never warms another's key (xUnit does not guarantee method execution order).
        // Every function records one row per actual execution and sleeps briefly, so a burst of
        // concurrent identical requests must overlap inside the factory for coalescing to be observable.
        script.Append(@"
create table cache_stampede_calls (
    id int generated always as identity primary key,
    label text not null,
    called_at timestamptz not null default clock_timestamp()
);

create function cache_stampede_scalar()
returns text
language sql
as $$
    insert into cache_stampede_calls (label) values ('scalar');
    select pg_sleep(0.2);
    select 'stampede-scalar-result';
$$;
comment on function cache_stampede_scalar() is 'HTTP GET
cached';

create function cache_stampede_warm()
returns text
language sql
as $$
    insert into cache_stampede_calls (label) values ('warm');
    select pg_sleep(0.2);
    select 'stampede-warm-result';
$$;
comment on function cache_stampede_warm() is 'HTTP GET
cached';

create function cache_stampede_param(_k text)
returns text
language sql
as $$
    insert into cache_stampede_calls (label) values ('param:' || _k);
    select pg_sleep(0.2);
    select 'r:' || _k;
$$;
comment on function cache_stampede_param(text) is 'HTTP GET
cached _k';

-- Set-returning, within MaxCacheableRows (default 1000): a cold burst must coalesce to one execution.
create function cache_stampede_set()
returns table (n int, v text)
language sql
as $$
    insert into cache_stampede_calls (label) values ('set');
    select g.n, 'row-' || g.n::text from generate_series(1, 3) g(n), pg_sleep(0.2);
$$;
comment on function cache_stampede_set() is 'HTTP GET
cached';

-- Set-returning, ABOVE MaxCacheableRows (1001 > 1000): never cached, so the gate serializes but every
-- request still executes its own query. Verifies over-limit is handled correctly (no coalescing, no
-- corruption, no caching of a partial/over-limit result).
create function cache_stampede_set_big()
returns table (n int)
language sql
as $$
    insert into cache_stampede_calls (label) values ('setbig');
    select g.n from generate_series(1, 1001) g(n), pg_sleep(0.1);
$$;
comment on function cache_stampede_set_big() is 'HTTP GET
cached';
");
    }
}

[Collection("TestFixture")]
public class CacheStampedeTests(TestFixture test)
{
    private static async Task<int> CountCallsAsync(string label)
    {
        await using var conn = Database.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*) from cache_stampede_calls where label = $1";
        var p = cmd.CreateParameter();
        p.Value = label;
        cmd.Parameters.Add(p);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task<int> CountCallsLikeAsync(string pattern)
    {
        await using var conn = Database.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*) from cache_stampede_calls where label like $1";
        var p = cmd.CreateParameter();
        p.Value = pattern;
        cmd.Parameters.Add(p);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Test_Stampede_Coalesces_Concurrent_Cold_Requests_To_Single_Execution()
    {
        const int n = 50;
        var tasks = Enumerable.Range(0, n)
            .Select(_ => test.Client.GetAsync("/api/cache-stampede-scalar/"))
            .ToArray();
        var responses = await Task.WhenAll(tasks);

        foreach (var r in responses)
        {
            r.StatusCode.Should().Be(HttpStatusCode.OK);
            (await r.Content.ReadAsStringAsync()).Should().Be("stampede-scalar-result");
            r.Dispose();
        }

        (await CountCallsAsync("scalar")).Should().Be(1,
            "a burst of identical cached requests must coalesce into exactly one DB execution");
    }

    [Fact]
    public async Task Test_Stampede_Warm_Cache_Burst_Adds_No_Executions()
    {
        // Prime the cache with a single cold request.
        using (var first = await test.Client.GetAsync("/api/cache-stampede-warm/"))
        {
            first.StatusCode.Should().Be(HttpStatusCode.OK);
            (await first.Content.ReadAsStringAsync()).Should().Be("stampede-warm-result");
        }
        (await CountCallsAsync("warm")).Should().Be(1, "the priming request executes exactly once");

        // A subsequent burst is served entirely from cache - no factory runs at all.
        var tasks = Enumerable.Range(0, 30)
            .Select(_ => test.Client.GetAsync("/api/cache-stampede-warm/"))
            .ToArray();
        var responses = await Task.WhenAll(tasks);
        foreach (var r in responses)
        {
            r.StatusCode.Should().Be(HttpStatusCode.OK);
            (await r.Content.ReadAsStringAsync()).Should().Be("stampede-warm-result");
            r.Dispose();
        }

        (await CountCallsAsync("warm")).Should().Be(1, "a warm-cache burst must add zero further executions");
    }

    [Fact]
    public async Task Test_Stampede_Distinct_Keys_Do_Not_Coalesce()
    {
        const int perKey = 8;
        var keys = new[] { "a", "b", "c", "d" };
        var tasks = keys
            .SelectMany(k => Enumerable.Range(0, perKey)
                .Select(_ => (key: k, task: test.Client.GetAsync($"/api/cache-stampede-param/?k={k}"))))
            .ToArray();
        await Task.WhenAll(tasks.Select(t => t.task));

        foreach (var (key, task) in tasks)
        {
            using var r = await task;
            r.StatusCode.Should().Be(HttpStatusCode.OK);
            (await r.Content.ReadAsStringAsync()).Should().Be($"r:{key}");
        }

        // Each distinct key coalesces independently -> exactly one execution per key.
        (await CountCallsLikeAsync("param:%")).Should().Be(keys.Length,
            "each distinct cache key coalesces on its own, so there is exactly one execution per key");
    }

    [Fact]
    public async Task Test_Stampede_Set_Within_Limit_Coalesces_To_Single_Execution()
    {
        const int n = 50;
        var tasks = Enumerable.Range(0, n)
            .Select(_ => test.Client.GetAsync("/api/cache-stampede-set/"))
            .ToArray();
        var responses = await Task.WhenAll(tasks);

        string? firstBody = null;
        foreach (var r in responses)
        {
            r.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await r.Content.ReadAsStringAsync();
            body.Should().StartWith("[").And.Contain("row-1").And.Contain("row-3");
            firstBody ??= body;
            body.Should().Be(firstBody, "every coalesced caller must receive the identical cached set");
            r.Dispose();
        }

        (await CountCallsAsync("set")).Should().Be(1,
            "a within-limit cached set must coalesce a concurrent burst into exactly one execution");
    }

    [Fact]
    public async Task Test_Stampede_Set_Over_Limit_Executes_Per_Request_Without_Caching()
    {
        // 1001 rows exceeds the default MaxCacheableRows (1000), so nothing is cached. The gate
        // serializes the requests but each still runs its own query and returns the full, correct set.
        const int n = 4;
        var tasks = Enumerable.Range(0, n)
            .Select(_ => test.Client.GetAsync("/api/cache-stampede-set-big/"))
            .ToArray();
        var responses = await Task.WhenAll(tasks);

        foreach (var r in responses)
        {
            r.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await r.Content.ReadAsStringAsync();
            body.Should().StartWith("[").And.Contain("1001");
            r.Dispose();
        }

        (await CountCallsAsync("setbig")).Should().Be(n,
            "an over-limit set is never cached, so each request executes its own query (serialized, not coalesced)");
    }
}
