namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CacheArrayTests()
    {
        script.Append(@"
-- Test: array parameters with caching (unique function name per test)
create function cache_array_param_same(_arr text[])
returns text
language sql
as $$
select array_to_string(_arr, ',') || '_' || random()::text
$$;
comment on function cache_array_param_same(text[]) is 'HTTP GET
cached _arr';

create function cache_array_param_diff(_arr text[])
returns text
language sql
as $$
select array_to_string(_arr, ',') || '_' || random()::text
$$;
comment on function cache_array_param_diff(text[]) is 'HTTP GET
cached _arr';

create function cache_array_param_order(_arr text[])
returns text
language sql
as $$
select array_to_string(_arr, ',') || '_' || random()::text
$$;
comment on function cache_array_param_order(text[]) is 'HTTP GET
cached _arr';
");
    }
}

[Collection("TestFixture")]
public class CacheArrayTests(TestFixture test)
{
    [Fact]
    public async Task Test_Cache_Array_Same_Values_Returns_Same()
    {
        // Use unique values to avoid collision with other tests
        var u1 = Guid.NewGuid().ToString("N")[..4];
        var u2 = Guid.NewGuid().ToString("N")[..4];
        var u3 = Guid.NewGuid().ToString("N")[..4];

        using var result1 = await test.Client.GetAsync($"/api/cache-array-param-same/?arr={u1}&arr={u2}&arr={u3}");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);
        response1.Should().StartWith($"{u1},{u2},{u3}_");

        using var result2 = await test.Client.GetAsync($"/api/cache-array-param-same/?arr={u1}&arr={u2}&arr={u3}");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().Be(response2, "same array should return cached value");
    }

    [Fact]
    public async Task Test_Cache_Array_Different_Values_Returns_Different()
    {
        // Use unique values to avoid collision with other tests
        var u1 = Guid.NewGuid().ToString("N")[..4];
        var u2 = Guid.NewGuid().ToString("N")[..4];
        var u3 = Guid.NewGuid().ToString("N")[..4];

        using var result1 = await test.Client.GetAsync($"/api/cache-array-param-diff/?arr={u1}&arr={u2}");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);

        using var result2 = await test.Client.GetAsync($"/api/cache-array-param-diff/?arr={u1}&arr={u3}");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().NotBe(response2, "different array values should produce different cache keys");
    }

    [Fact]
    public async Task Test_Cache_Array_Order_Matters()
    {
        // Use unique values to avoid collision with other tests
        var u1 = Guid.NewGuid().ToString("N")[..4];
        var u2 = Guid.NewGuid().ToString("N")[..4];

        using var result1 = await test.Client.GetAsync($"/api/cache-array-param-order/?arr={u1}&arr={u2}");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);

        using var result2 = await test.Client.GetAsync($"/api/cache-array-param-order/?arr={u2}&arr={u1}");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().NotBe(response2, "array order should matter for cache key");
    }
}
