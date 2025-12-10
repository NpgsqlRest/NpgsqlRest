namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CacheKeyCollisionTests()
    {
        script.Append(@"
-- Test: concatenation collision - 'ab' + 'c' vs 'a' + 'bc' should produce different cache keys
create function cache_concat_collision(_key1 text, _key2 text)
returns text
language sql
as $$
select _key1 || '|' || _key2 || '|' || random()::text
$$;
comment on function cache_concat_collision(text, text) is 'HTTP GET
cached _key1, _key2';

-- Test: null vs empty string - should produce different cache keys
create function cache_null_vs_empty(_key text default null)
returns text
language sql
as $$
select coalesce(_key, 'NULL') || '_' || random()::text
$$;
comment on function cache_null_vs_empty(text) is 'HTTP GET
cached _key';

-- Test: null in different positions (both params required - no defaults)
create function cache_null_positions(_key1 text, _key2 text)
returns text
language sql
as $$
select coalesce(_key1, 'NULL1') || '_' || coalesce(_key2, 'NULL2') || '_' || random()::text
$$;
comment on function cache_null_positions(text, text) is 'HTTP GET
cached _key1, _key2';

-- Test: empty string vs missing parameter
create function cache_empty_string(_key text default 'default')
returns text
language sql
as $$
select _key || '_' || random()::text
$$;
comment on function cache_empty_string(text) is 'HTTP GET
cached _key';
");
    }
}

[Collection("TestFixture")]
public class CacheKeyCollisionTests(TestFixture test)
{
    [Fact]
    public async Task Test_Cache_Concatenation_Collision_Ab_C_Vs_A_Bc()
    {
        // Test that "ab" + "c" produces different cache key than "a" + "bc"
        using var result1 = await test.Client.GetAsync("/api/cache-concat-collision/?key1=ab&key2=c");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);
        response1.Should().StartWith("ab|c|");

        using var result2 = await test.Client.GetAsync("/api/cache-concat-collision/?key1=a&key2=bc");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.Should().StartWith("a|bc|");

        response1.Should().NotBe(response2, "different parameter splits should produce different cache keys");
    }

    [Fact]
    public async Task Test_Cache_Concatenation_Collision_Abc_Empty_Vs_Empty_Abc()
    {
        // Test that "abc" + "" produces different cache key than "" + "abc"
        using var result1 = await test.Client.GetAsync("/api/cache-concat-collision/?key1=abc&key2=");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);

        using var result2 = await test.Client.GetAsync("/api/cache-concat-collision/?key1=&key2=abc");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().NotBe(response2, "empty string in different positions should produce different cache keys");
    }

    [Fact]
    public async Task Test_Cache_Null_Vs_Empty_String_Are_Different()
    {
        // Test that null produces different cache key than empty string
        using var result1 = await test.Client.GetAsync("/api/cache-null-vs-empty/");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);
        response1.Should().StartWith("NULL_");

        using var result2 = await test.Client.GetAsync("/api/cache-null-vs-empty/?key=");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().NotBe(response2, "null and empty string should produce different cache keys");
    }

    [Fact]
    public async Task Test_Cache_Empty_String_In_First_Position_Vs_Second_Position()
    {
        // Test that empty string in key1 vs key2 produces different cache keys
        // Use unique values to avoid collision with other tests
        var unique = Guid.NewGuid().ToString("N")[..8];

        // Pass empty for key1, value for key2 -> coalesce returns empty string for key1
        using var result1 = await test.Client.GetAsync($"/api/cache-null-positions/?key1=&key2={unique}");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);
        // Empty string for key1 means coalesce returns empty string, not NULL1
        response1.Should().StartWith($"_{unique}_");

        // Pass value for key1, empty for key2 -> coalesce returns empty string for key2
        using var result2 = await test.Client.GetAsync($"/api/cache-null-positions/?key1={unique}&key2=");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.Should().StartWith($"{unique}__");

        response1.Should().NotBe(response2, "empty string in different positions should produce different cache keys");
    }

    [Fact]
    public async Task Test_Cache_Both_Null_Returns_Same_Value()
    {
        // Test that two calls with both params null return cached value
        using var result1 = await test.Client.GetAsync("/api/cache-null-positions/?key1=&key2=");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);

        using var result2 = await test.Client.GetAsync("/api/cache-null-positions/?key1=&key2=");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().Be(response2, "both null should return cached value");
    }

    [Fact]
    public async Task Test_Cache_Empty_String_Param_Returns_Same()
    {
        using var result1 = await test.Client.GetAsync("/api/cache-empty-string/?key=");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);
        response1.Should().StartWith("_");

        using var result2 = await test.Client.GetAsync("/api/cache-empty-string/?key=");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().Be(response2, "same empty string should return cached value");
    }

    [Fact]
    public async Task Test_Cache_Missing_Param_Vs_Empty_String_Are_Different()
    {
        // Missing param uses default value, empty string is explicitly empty
        using var result1 = await test.Client.GetAsync("/api/cache-empty-string/");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);
        response1.Should().StartWith("default_");

        using var result2 = await test.Client.GetAsync("/api/cache-empty-string/?key=");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.Should().StartWith("_");

        response1.Should().NotBe(response2, "missing param (using default) vs empty string should be different");
    }
}
