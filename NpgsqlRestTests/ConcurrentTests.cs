#pragma warning disable CS8602 // Dereference of a possibly null reference.
namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ConcurrentTests()
    {
        script.Append("""
        create function get_case_concurrent1(
            _int_field int
        ) 
        returns table (
            int_field int
        )
        language sql
        as 
        $_$
        select _int_field;
        $_$;
        
        create function get_case_concurrent2(
            _text_field text,
            _int_field int
        )
        returns table (
            text_field text,
            int_field int
        )
        language sql
        as
        $_$
        select _text_field, _int_field;
        $_$;
        
        create function get_case_concurrent3(
            _bool_field bool,
            _int_field int,
            _text_field text
        )
        returns table (
            bool_field bool,
            int_field int,
            text_field text
        )
        language sql
        as
        $_$
        select _bool_field, _int_field, _text_field;
        $_$;
        
        create function get_case_concurrent4(
            _timestamp_field timestamp,
            _bool_field bool,
            _text_field text,
            _int_field int
        )
        returns table (
            timestamp_field timestamp,
            bool_field bool,
            text_field text,
            int_field int
        )
        language sql
        as
        $_$
        select _timestamp_field, _bool_field, _text_field, _int_field;
        $_$;
        
        create function get_case_concurrent5(
            _int_array int[],
            _timestamp_field timestamp,
            _int_field int,
            _bool_field bool,
            _text_field text
        )
        returns table (
            int_array int[],
            timestamp_field timestamp,
            int_field int,
            bool_field bool,
            text_field text
        )
        language sql
        as
        $_$
        select _int_array, _timestamp_field, _int_field, _bool_field, _text_field;
        $_$;
        
        create function get_case_concurrent6(
            _text_array text[],
            _int_field int,
            _bool_field bool,
            _int_array int[],
            _text_field text,
            _timestamp_field timestamp
        )
        returns table (
            text_array text[],
            int_field int,
            bool_field bool,
            int_array int[],
            text_field text,
            timestamp_field timestamp
        )
        language sql
        as
        $_$
        select _text_array, _int_field, _bool_field, _int_array, _text_field, _timestamp_field;
        $_$;
        
        create function get_case_concurrent7(
            _bool_array bool[],
            _text_field text,
            _timestamp_field timestamp,
            _int_array int[],
            _int_field int,
            _text_array text[],
            _bool_field bool
        )
        returns table (
            bool_array bool[],
            text_field text,
            timestamp_field timestamp,
            int_array int[],
            int_field int,
            text_array text[],
            bool_field bool
        )
        language sql
        as
        $_$
        select _bool_array, _text_field, _timestamp_field, _int_array, _int_field, _text_array, _bool_field;
        $_$;
        
        create function get_case_concurrent8(
            _timestamp_array timestamp[],
            _bool_field bool,
            _int_array int[],
            _text_field text,
            _bool_array bool[],
            _timestamp_field timestamp,
            _int_field int,
            _text_array text[]
        )
        returns table (
            timestamp_array timestamp[],
            bool_field bool,
            int_array int[],
            text_field text,
            bool_array bool[],
            timestamp_field timestamp,
            int_field int,
            text_array text[]
        )
        language sql
        as
        $_$
        select _timestamp_array, _bool_field, _int_array, _text_field, _bool_array, _timestamp_field, _int_field, _text_array;
        $_$;
        """);
    }
}

[Collection("TestFixture")]
public class ConcurrentTests(TestFixture test)
{
    [Fact]
    public async Task Test_case_concurrent()
    {
        var tasks = new[]
        {
            Test_get_case_concurrent1(),
            Test_get_case_concurrent2(),
            Test_get_case_concurrent3(),
            Test_get_case_concurrent4(),
            Test_get_case_concurrent5(),
            Test_get_case_concurrent6(),
            Test_get_case_concurrent7(),
            Test_get_case_concurrent8()
        };

        await Task.WhenAll(tasks);
    }

    private async Task Test_get_case_concurrent1()
    {
        var query = new QueryBuilder { { "intField", "123" } };
        using var result = await test.Client.GetAsync($"/api/get-case-concurrent1/{query}");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType.MediaType.Should().Be("application/json");

        var array = JsonNode.Parse(response).AsArray();
        array.Count.Should().Be(1);
        array[0]["intField"].ToJsonString().Should().Be("123");
        array[0].AsObject().Count.Should().Be(1);
    }

    private async Task Test_get_case_concurrent2()
    {
        var query = new QueryBuilder { { "textField", "test" }, { "intField", "123" } };
        using var result = await test.Client.GetAsync($"/api/get-case-concurrent2/{query}");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType.MediaType.Should().Be("application/json");

        var array = JsonNode.Parse(response).AsArray();
        array.Count.Should().Be(1);
        array[0]["intField"].ToJsonString().Should().Be("123");
        array[0]["textField"].ToJsonString().Should().Be("\"test\"");
        array[0].AsObject().Count.Should().Be(2);
    }

    private async Task Test_get_case_concurrent3()
    {
        var query = new QueryBuilder { { "boolField", "true" }, { "intField", "123" }, { "textField", "test" } };
        using var result = await test.Client.GetAsync($"/api/get-case-concurrent3/{query}");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType.MediaType.Should().Be("application/json");

        var array = JsonNode.Parse(response).AsArray();
        array.Count.Should().Be(1);
        array[0]["intField"].ToJsonString().Should().Be("123");
        array[0]["textField"].ToJsonString().Should().Be("\"test\"");
        array[0]["boolField"].ToJsonString().Should().Be("true");
        array[0].AsObject().Count.Should().Be(3);
    }

    private async Task Test_get_case_concurrent4()
    {
        var query = new QueryBuilder { { "timestampField", "2024-01-01T12:00:00" }, { "boolField", "true" }, { "textField", "test" }, { "intField", "123" } };
        using var result = await test.Client.GetAsync($"/api/get-case-concurrent4/{query}");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType.MediaType.Should().Be("application/json");

        var array = JsonNode.Parse(response).AsArray();
        array.Count.Should().Be(1);
        array[0]["intField"].ToJsonString().Should().Be("123");
        array[0]["textField"].ToJsonString().Should().Be("\"test\"");
        array[0]["boolField"].ToJsonString().Should().Be("true");
        array[0]["timestampField"].ToJsonString().Should().Be("\"2024-01-01T12:00:00\"");
        array[0].AsObject().Count.Should().Be(4);
    }

    private async Task Test_get_case_concurrent5()
    {
        var query = new QueryBuilder { { "intArray", "1" }, { "intArray", "2" }, { "intArray", "3" }, { "timestampField", "2024-01-01T12:00:00" }, { "intField", "123" }, { "boolField", "true" }, { "textField", "test" } };
        using var result = await test.Client.GetAsync($"/api/get-case-concurrent5/{query}");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType.MediaType.Should().Be("application/json");

        var array = JsonNode.Parse(response).AsArray();
        array.Count.Should().Be(1);
        array[0]["intField"].ToJsonString().Should().Be("123");
        array[0]["textField"].ToJsonString().Should().Be("\"test\"");
        array[0]["boolField"].ToJsonString().Should().Be("true");
        array[0]["timestampField"].ToJsonString().Should().Be("\"2024-01-01T12:00:00\"");
        array[0]["intArray"].ToJsonString().Should().Be("[1,2,3]");
        array[0].AsObject().Count.Should().Be(5);
    }

    private async Task Test_get_case_concurrent6()
    {
        var query = new QueryBuilder { { "textArray", "a" }, { "textArray", "b" }, { "textArray", "c" }, { "intField", "123" }, { "boolField", "true" }, { "intArray", "1" }, { "intArray", "2" }, { "intArray", "3" }, { "textField", "test" }, { "timestampField", "2024-01-01T12:00:00" } };
        using var result = await test.Client.GetAsync($"/api/get-case-concurrent6/{query}");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType.MediaType.Should().Be("application/json");

        var array = JsonNode.Parse(response).AsArray();
        array.Count.Should().Be(1);
        array[0]["intField"].ToJsonString().Should().Be("123");
        array[0]["textField"].ToJsonString().Should().Be("\"test\"");
        array[0]["boolField"].ToJsonString().Should().Be("true");
        array[0]["timestampField"].ToJsonString().Should().Be("\"2024-01-01T12:00:00\"");
        array[0]["intArray"].ToJsonString().Should().Be("[1,2,3]");
        array[0]["textArray"].ToJsonString().Should().Be("[\"a\",\"b\",\"c\"]");
        array[0].AsObject().Count.Should().Be(6);
    }

    private async Task Test_get_case_concurrent7()
    {
        var query = new QueryBuilder { { "boolArray", "true" }, { "boolArray", "false" }, { "boolArray", "true" }, { "textField", "test" }, { "timestampField", "2024-01-01T12:00:00" }, { "intArray", "1" }, { "intArray", "2" }, { "intArray", "3" }, { "intField", "123" }, { "textArray", "a" }, { "textArray", "b" }, { "textArray", "c" }, { "boolField", "true" } };
        using var result = await test.Client.GetAsync($"/api/get-case-concurrent7/{query}");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType.MediaType.Should().Be("application/json");

        var array = JsonNode.Parse(response).AsArray();
        array.Count.Should().Be(1);
        array[0]["intField"].ToJsonString().Should().Be("123");
        array[0]["textField"].ToJsonString().Should().Be("\"test\"");
        array[0]["boolField"].ToJsonString().Should().Be("true");
        array[0]["timestampField"].ToJsonString().Should().Be("\"2024-01-01T12:00:00\"");
        array[0]["intArray"].ToJsonString().Should().Be("[1,2,3]");
        array[0]["textArray"].ToJsonString().Should().Be("[\"a\",\"b\",\"c\"]");
        array[0]["boolArray"].ToJsonString().Should().Be("[true,false,true]");
        array[0].AsObject().Count.Should().Be(7);
    }

    private async Task Test_get_case_concurrent8()
    {
        var query = new QueryBuilder { { "timestampArray", "2024-01-01T12:00:00" }, { "timestampArray", "2024-01-02T12:00:00" }, { "boolField", "true" }, { "intArray", "1" }, { "intArray", "2" }, { "intArray", "3" }, { "textField", "test" }, { "boolArray", "true" }, { "boolArray", "false" }, { "boolArray", "true" }, { "timestampField", "2024-01-01T12:00:00" }, { "intField", "123" }, { "textArray", "a" }, { "textArray", "b" }, { "textArray", "c" } };
        using var result = await test.Client.GetAsync($"/api/get-case-concurrent8/{query}");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Content.Headers.ContentType.MediaType.Should().Be("application/json");

        var array = JsonNode.Parse(response).AsArray();
        array.Count.Should().Be(1);
        array[0]["intField"].ToJsonString().Should().Be("123");
        array[0]["textField"].ToJsonString().Should().Be("\"test\"");
        array[0]["boolField"].ToJsonString().Should().Be("true");
        array[0]["timestampField"].ToJsonString().Should().Be("\"2024-01-01T12:00:00\"");
        array[0]["intArray"].ToJsonString().Should().Be("[1,2,3]");
        array[0]["textArray"].ToJsonString().Should().Be("[\"a\",\"b\",\"c\"]");
        array[0]["boolArray"].ToJsonString().Should().Be("[true,false,true]");
        array[0]["timestampArray"].ToJsonString().Should().Be("[\"2024-01-01T12:00:00\",\"2024-01-02T12:00:00\"]");
        array[0].AsObject().Count.Should().Be(8);
    }
}
