namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CacheBasicTests()
    {
        script.Append(@"
create function cache_get_timestamp()
returns text
language sql
as $$
select clock_timestamp()::text
$$;
comment on function cache_get_timestamp() is 'HTTP GET
cached';

create function cache_get_random()
returns text
language sql
as $$
select random()::text
$$;
comment on function cache_get_random() is 'HTTP GET
cached';

create function cache_get_value_with_param(_key text)
returns text
language sql
as $$
select _key || '_' || random()::text
$$;
comment on function cache_get_value_with_param(text) is 'HTTP GET
cached _key';

create function cache_get_value_with_two_params(_key1 text, _key2 text)
returns text
language sql
as $$
select _key1 || '_' || _key2 || '_' || random()::text
$$;
comment on function cache_get_value_with_two_params(text, text) is 'HTTP GET
cached _key1, _key2';

create function cache_get_value_partial_key(_key1 text, _key2 text)
returns text
language sql
as $$
select _key1 || '_' || _key2 || '_' || random()::text
$$;
comment on function cache_get_value_partial_key(text, text) is 'HTTP GET
cached _key1';

create function cache_post_value(_input text)
returns text
language sql
as $$
select _input || '_' || random()::text
$$;
comment on function cache_post_value(text) is 'cached _input';

create function cache_expires_test()
returns text
language sql
as $$
select random()::text
$$;
comment on function cache_expires_test() is 'HTTP GET
cached
cache_expires_in 1 second';

create function cache_no_cache_test()
returns text
language sql
as $$
select random()::text
$$;
");
    }
}

[Collection("TestFixture")]
public class CacheBasicTests(TestFixture test)
{
    [Fact]
    public async Task Test_Cache_Returns_Same_Value_On_Subsequent_Calls()
    {
        using var result1 = await test.Client.GetAsync("/api/cache-get-timestamp/");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);

        await Task.Delay(10);

        using var result2 = await test.Client.GetAsync("/api/cache-get-timestamp/");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().Be(response2, "cached endpoint should return same value");
    }

    [Fact]
    public async Task Test_Cache_Random_Returns_Same_Value()
    {
        using var result1 = await test.Client.GetAsync("/api/cache-get-random/");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);

        using var result2 = await test.Client.GetAsync("/api/cache-get-random/");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().Be(response2, "cached random should return same value");
    }

    [Fact]
    public async Task Test_Cache_With_Param_Same_Key_Returns_Same_Value()
    {
        using var result1 = await test.Client.GetAsync("/api/cache-get-value-with-param/?key=test1");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);
        response1.Should().StartWith("test1_");

        using var result2 = await test.Client.GetAsync("/api/cache-get-value-with-param/?key=test1");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().Be(response2, "same cache key should return same value");
    }

    [Fact]
    public async Task Test_Cache_With_Param_Different_Key_Returns_Different_Value()
    {
        using var result1 = await test.Client.GetAsync("/api/cache-get-value-with-param/?key=keyA");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);
        response1.Should().StartWith("keyA_");

        using var result2 = await test.Client.GetAsync("/api/cache-get-value-with-param/?key=keyB");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.Should().StartWith("keyB_");

        response1.Should().NotBe(response2, "different cache keys should return different values");
    }

    [Fact]
    public async Task Test_Cache_With_Two_Params_Same_Keys_Returns_Same_Value()
    {
        using var result1 = await test.Client.GetAsync("/api/cache-get-value-with-two-params/?key1=a&key2=b");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);
        response1.Should().StartWith("a_b_");

        using var result2 = await test.Client.GetAsync("/api/cache-get-value-with-two-params/?key1=a&key2=b");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().Be(response2, "same cache keys should return same value");
    }

    [Fact]
    public async Task Test_Cache_With_Two_Params_Different_Keys_Returns_Different_Value()
    {
        using var result1 = await test.Client.GetAsync("/api/cache-get-value-with-two-params/?key1=a&key2=b");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);

        using var result2 = await test.Client.GetAsync("/api/cache-get-value-with-two-params/?key1=a&key2=c");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().NotBe(response2, "different key2 should return different value");
    }

    [Fact]
    public async Task Test_Cache_Partial_Key_Ignores_Non_Cached_Param()
    {
        using var result1 = await test.Client.GetAsync("/api/cache-get-value-partial-key/?key1=same&key2=different1");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);

        using var result2 = await test.Client.GetAsync("/api/cache-get-value-partial-key/?key1=same&key2=different2");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().Be(response2, "only key1 is part of cache key, so key2 changes should not affect cache");
    }

    [Fact]
    public async Task Test_Cache_Post_With_Body_Param()
    {
        using var content1 = new StringContent("{\"input\":\"hello\"}", Encoding.UTF8, "application/json");
        using var result1 = await test.Client.PostAsync("/api/cache-post-value/", content1);
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);
        response1.Should().StartWith("hello_");

        using var content2 = new StringContent("{\"input\":\"hello\"}", Encoding.UTF8, "application/json");
        using var result2 = await test.Client.PostAsync("/api/cache-post-value/", content2);
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().Be(response2, "same input should return cached value");
    }

    [Fact]
    public async Task Test_Cache_Post_Different_Body_Returns_Different_Value()
    {
        using var content1 = new StringContent("{\"input\":\"value1\"}", Encoding.UTF8, "application/json");
        using var result1 = await test.Client.PostAsync("/api/cache-post-value/", content1);
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);

        using var content2 = new StringContent("{\"input\":\"value2\"}", Encoding.UTF8, "application/json");
        using var result2 = await test.Client.PostAsync("/api/cache-post-value/", content2);
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().NotBe(response2, "different input should return different value");
    }

    [Fact]
    public async Task Test_Cache_Expires_Returns_Different_Value_After_Expiration()
    {
        using var result1 = await test.Client.GetAsync("/api/cache-expires-test/");
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);

        await Task.Delay(1500);

        using var result2 = await test.Client.GetAsync("/api/cache-expires-test/");
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().NotBe(response2, "cache should expire after 1 second");
    }

    [Fact]
    public async Task Test_No_Cache_Returns_Different_Values()
    {
        using var result1 = await test.Client.PostAsync("/api/cache-no-cache-test/", null);
        var response1 = await result1.Content.ReadAsStringAsync();
        result1?.StatusCode.Should().Be(HttpStatusCode.OK);

        using var result2 = await test.Client.PostAsync("/api/cache-no-cache-test/", null);
        var response2 = await result2.Content.ReadAsStringAsync();
        result2?.StatusCode.Should().Be(HttpStatusCode.OK);

        response1.Should().NotBe(response2, "non-cached endpoint should return different random values");
    }
}
