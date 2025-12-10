namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CacheRecordSetTests()
    {
        script.Append(@"
-- Test: single row record caching
create function cache_get_record(_id int)
returns table(id int, name text, created_at timestamp)
language sql
as $$
select _id, 'Name_' || random()::text, now()
$$;
comment on function cache_get_record(int) is 'HTTP GET
cached _id';

-- Test: multi-column record without set
create function cache_get_single_record(_key text)
returns record
language plpgsql
as $$
declare
    result record;
begin
    select _key as key, random()::text as value, now() as ts into result;
    return result;
end;
$$;
comment on function cache_get_single_record(text) is 'HTTP GET
cached _key';

-- Test: set returning function caching
create function cache_get_set(_count int)
returns table(id int, value text)
language sql
as $$
select generate_series(1, _count), 'Value_' || random()::text
$$;
comment on function cache_get_set(int) is 'HTTP GET
cached _count';

-- Test: set returning with different params
create function cache_get_set_params(_prefix text, _count int)
returns table(id int, name text)
language sql
as $$
select generate_series(1, _count), _prefix || '_' || random()::text
$$;
comment on function cache_get_set_params(text, int) is 'HTTP GET
cached _prefix, _count';

-- Test: unnamed set (returns setof text)
create function cache_get_unnamed_set(_count int)
returns setof text
language sql
as $$
select 'Item_' || generate_series(1, _count) || '_' || random()::text
$$;
comment on function cache_get_unnamed_set(int) is 'HTTP GET
cached _count';

-- Test: JSON record
create function cache_get_json_record(_key text)
returns table(key text, data json)
language sql
as $$
select _key, json_build_object('random', random(), 'key', _key)
$$;
comment on function cache_get_json_record(text) is 'HTTP GET
cached _key';

-- Test: large set (for testing MaxCacheableRows limit)
create function cache_get_large_set(_count int)
returns table(id int, value text)
language sql
as $$
select generate_series(1, _count), 'LargeValue_' || random()::text
$$;
comment on function cache_get_large_set(int) is 'HTTP GET
cached _count';

-- Test: empty set
create function cache_get_empty_set()
returns table(id int, value text)
language sql
as $$
select id, value from (select 1 as id, 'x' as value) t where false
$$;
comment on function cache_get_empty_set() is 'HTTP GET
cached';

-- Test: set with nulls
create function cache_get_set_with_nulls(_count int)
returns table(id int, value text)
language sql
as $$
select s.id,
       case when s.id % 2 = 0 then null else 'Val_' || random()::text end
from generate_series(1, _count) as s(id)
$$;
comment on function cache_get_set_with_nulls(int) is 'HTTP GET
cached _count';
");
    }
}

[Collection("TestFixture")]
public class CacheRecordSetTests(TestFixture test)
{
    [Fact]
    public async Task Test_Cache_Record_Returns_Same_Value()
    {
        using var result1 = await test.Client.GetAsync("/api/cache-get-record/?id=1");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);
        result1?.Content?.Headers?.ContentType?.MediaType.Should().Be("application/json");

        using var result2 = await test.Client.GetAsync("/api/cache-get-record/?id=1");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().Be(response2, "cached record should return same value");
    }

    [Fact]
    public async Task Test_Cache_Record_Different_Params_Returns_Different()
    {
        using var result1 = await test.Client.GetAsync("/api/cache-get-record/?id=100");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);

        using var result2 = await test.Client.GetAsync("/api/cache-get-record/?id=101");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().NotBe(response2, "different params should return different cached values");
    }

    [Fact]
    public async Task Test_Cache_Set_Returns_Same_Value()
    {
        using var result1 = await test.Client.GetAsync("/api/cache-get-set/?count=5");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);
        result1?.Content?.Headers?.ContentType?.MediaType.Should().Be("application/json");
        response1.Should().StartWith("[");
        response1.Should().EndWith("]");

        using var result2 = await test.Client.GetAsync("/api/cache-get-set/?count=5");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().Be(response2, "cached set should return same value");
    }

    [Fact]
    public async Task Test_Cache_Set_Different_Params_Returns_Different()
    {
        using var result1 = await test.Client.GetAsync("/api/cache-get-set/?count=3");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);

        using var result2 = await test.Client.GetAsync("/api/cache-get-set/?count=4");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().NotBe(response2, "different count should return different sets");
    }

    [Fact]
    public async Task Test_Cache_Set_With_Multiple_Params()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];

        using var result1 = await test.Client.GetAsync($"/api/cache-get-set-params/?prefix={unique}&count=3");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);
        response1.Should().Contain(unique);

        using var result2 = await test.Client.GetAsync($"/api/cache-get-set-params/?prefix={unique}&count=3");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().Be(response2, "same params should return cached set");
    }

    [Fact]
    public async Task Test_Cache_Unnamed_Set_Returns_Same_Value()
    {
        using var result1 = await test.Client.GetAsync("/api/cache-get-unnamed-set/?count=3");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);
        response1.Should().StartWith("[");

        using var result2 = await test.Client.GetAsync("/api/cache-get-unnamed-set/?count=3");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().Be(response2, "cached unnamed set should return same value");
    }

    [Fact]
    public async Task Test_Cache_Json_Record()
    {
        var unique = Guid.NewGuid().ToString("N")[..6];

        using var result1 = await test.Client.GetAsync($"/api/cache-get-json-record/?key={unique}");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);
        response1.Should().Contain(unique);
        response1.Should().Contain("random");

        using var result2 = await test.Client.GetAsync($"/api/cache-get-json-record/?key={unique}");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().Be(response2, "cached JSON record should return same value");
    }

    [Fact]
    public async Task Test_Cache_Empty_Set()
    {
        using var result1 = await test.Client.GetAsync("/api/cache-get-empty-set/");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);
        response1.Should().Be("[]");

        using var result2 = await test.Client.GetAsync("/api/cache-get-empty-set/");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().Be(response2, "cached empty set should return same value");
    }

    [Fact]
    public async Task Test_Cache_Set_With_Nulls()
    {
        using var result1 = await test.Client.GetAsync("/api/cache-get-set-with-nulls/?count=4");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);
        response1.Should().Contain("null");

        using var result2 = await test.Client.GetAsync("/api/cache-get-set-with-nulls/?count=4");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().Be(response2, "cached set with nulls should return same value");
    }

    [Fact]
    public async Task Test_Cache_Large_Set_Still_Returns_Data()
    {
        // Even if set exceeds MaxCacheableRows, data should still be returned correctly
        // (just not cached on subsequent calls)
        using var result1 = await test.Client.GetAsync("/api/cache-get-large-set/?count=10");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);
        response1.Should().StartWith("[");
        response1.Should().EndWith("]");
        response1.Should().Contain("LargeValue_");

        // Verify the response has 10 items
        var count = response1.Split("\"id\"").Length - 1;
        count.Should().Be(10);
    }
}
