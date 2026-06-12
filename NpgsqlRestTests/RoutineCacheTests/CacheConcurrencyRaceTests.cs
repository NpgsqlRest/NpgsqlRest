namespace NpgsqlRestTests;

public static partial class Database
{
    // Concurrency across the two cache state transitions the stampede tests don't cover:
    // (1) entry EXPIRY under a concurrent burst, (2) explicit INVALIDATION racing concurrent reads.
    // Each function records one row per actual execution so tests can prove exact execution counts.
    public static void CacheConcurrencyRaceTests()
    {
        script.Append(@"
create table cache_race_calls (id int generated always as identity primary key, label text not null);

-- short TTL + execution delay so a burst overlaps the execution window
create function cache_race_expiry(_k text)
returns text
language sql
as $$
    insert into cache_race_calls (label) values ('expiry:' || _k);
    select pg_sleep(0.2);
    select 'r:' || _k;
$$;
comment on function cache_race_expiry(text) is 'HTTP GET
cached _k
cache_expires 1 seconds';

create function cache_race_invalidate(_k text)
returns text
language sql
as $$
    insert into cache_race_calls (label) values ('inv:' || _k);
    select 'r:' || _k;
$$;
comment on function cache_race_invalidate(text) is 'HTTP GET
cached _k';
");
    }
}

[Collection("TestFixture")]
public class CacheConcurrencyRaceTests(TestFixture test)
{
    private static async Task<int> CountCallsAsync(string label)
    {
        await using var conn = Database.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*) from cache_race_calls where label = $1";
        var p = cmd.CreateParameter();
        p.Value = label;
        cmd.Parameters.Add(p);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Concurrent_Bursts_Across_TTL_Expiry_Execute_Exactly_Once_Per_Window()
    {
        const string key = "ttl-window";
        const string url = $"/api/cache-race-expiry/?k={key}";

        // Burst 1 (cold): all requests coalesce to a single execution.
        var burst1 = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => test.Client.GetStringAsync(url)));
        burst1.Should().AllBe($"r:{key}");
        (await CountCallsAsync($"expiry:{key}")).Should().Be(1, "a cold concurrent burst must coalesce to one execution");

        // Let the 1-second TTL pass with margin.
        await Task.Delay(TimeSpan.FromSeconds(1.6));

        // Burst 2 (expired entry): again exactly one new execution, never one per request.
        var burst2 = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => test.Client.GetStringAsync(url)));
        burst2.Should().AllBe($"r:{key}");
        (await CountCallsAsync($"expiry:{key}")).Should().Be(2, "a burst against an expired entry must coalesce to one re-execution");
    }

    [Fact]
    public async Task Concurrent_Invalidations_And_Reads_Never_Error_And_Stay_Consistent()
    {
        const string key = "inv-race";
        const string readUrl = $"/api/cache-race-invalidate/?k={key}";
        const string invalidateUrl = $"/api/cache-race-invalidate/invalidate?k={key}";

        // Hammer reads and invalidations concurrently. Correctness bar: no 5xx, every read
        // returns the exact payload, every invalidate returns its documented JSON shape.
        var tasks = new List<Task>();
        for (var i = 0; i < 20; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var response = await test.Client.GetAsync(readUrl);
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                (await response.Content.ReadAsStringAsync()).Should().Be($"r:{key}");
            }));
            tasks.Add(Task.Run(async () =>
            {
                using var response = await test.Client.GetAsync(invalidateUrl);
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                var body = await response.Content.ReadAsStringAsync();
                // Depending on interleaving the entry may or may not exist at invalidation time.
                body.Should().BeOneOf("{\"invalidated\":true}", "{\"invalidated\":false}");
            }));
        }
        await Task.WhenAll(tasks);

        // The cache must still work after the storm: two reads, at most one new execution between them.
        var before = await CountCallsAsync($"inv:{key}");
        var r1 = await test.Client.GetStringAsync(readUrl);
        var r2 = await test.Client.GetStringAsync(readUrl);
        r1.Should().Be($"r:{key}");
        r2.Should().Be($"r:{key}");
        var after = await CountCallsAsync($"inv:{key}");
        (after - before).Should().BeInRange(0, 1, "the second read must be served from cache");
    }
}
