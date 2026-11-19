namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ScalarUtf8Tests()
    {
        script.Append("""
create function scalar_utf8_large_json(_items int) returns json language sql stable as $$
select json_agg(json_build_object('id', i, 'name', 'stavka ' || i || ' žćč 😀')) from generate_series(1, _items) i $$;

create function scalar_utf8_large_text(_length int) returns text language sql stable as $$
select repeat('0123456789', _length / 10) $$;

create function scalar_utf8_text() returns text language sql stable as $$
select 'Ünïcödé "quoted" \ žćč 😀' $$;

create function scalar_utf8_text_array() returns text[] language sql stable as $$
select array['a b', 'žćč', 'q"uote', 'back\slash', null, '😀'] $$;

create function scalar_utf8_int_array() returns int[] language sql stable as $$
select array[1, 2, null, 4] $$;

create function scalar_utf8_null() returns text language sql stable as $$
select null::text $$;
""");
    }
}

/// <summary>
/// A routine returning a single value writes it to the response as UTF-8 bytes taken straight from the reader
/// (values over 64 KB are streamed in pieces). The response must be exactly the value.
/// </summary>
[Collection("TestFixture")]
public class ScalarUtf8Tests(TestFixture test)
{
    [Theory]
    [InlineData(10)]      // small: buffered, written once
    [InlineData(1500)]    // just over 64 KB: streamed
    [InlineData(20000)]   // ~1 MB: many pieces
    public async Task Large_json_value_is_complete(int items)
    {
        using var response = await test.Client.GetAsync($"/api/scalar-utf8-large-json/?items={items}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var expected = "[" + string.Join(", ", Enumerable.Range(1, items).Select(i => $"{{\"id\" : {i}, \"name\" : \"stavka {i} žćč 😀\"}}")) + "]";
        (await response.Content.ReadAsStringAsync()).Should().Be(expected);
    }

    [Theory]
    [InlineData(65530)]   // just under the 64 KB limit
    [InlineData(65540)]   // just over
    [InlineData(300000)]
    public async Task Large_text_value_is_complete(int length)
    {
        using var response = await test.Client.GetAsync($"/api/scalar-utf8-large-text/?length={length}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        var body = await response.Content.ReadAsStringAsync();
        body.Length.Should().Be(length);
        body.Should().Be(string.Concat(Enumerable.Repeat("0123456789", length / 10)));
    }

    [Fact]
    public async Task Unicode_text_value_is_returned_as_it_is()
    {
        using var response = await test.Client.GetAsync("/api/scalar-utf8-text/");
        (await response.Content.ReadAsStringAsync()).Should().Be("Ünïcödé \"quoted\" \\ žćč 😀");
    }

    [Fact]
    public async Task Text_array_value_is_a_json_array()
    {
        using var response = await test.Client.GetAsync("/api/scalar-utf8-text-array/");
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        (await response.Content.ReadAsStringAsync()).Should().Be("[\"a b\",\"žćč\",\"q\\\"uote\",\"back\\\\slash\",null,\"\\uD83D\\uDE00\"]");
    }

    [Fact]
    public async Task Int_array_value_is_a_json_array()
    {
        using var response = await test.Client.GetAsync("/api/scalar-utf8-int-array/");
        (await response.Content.ReadAsStringAsync()).Should().Be("[1,2,null,4]");
    }

    [Fact]
    public async Task Null_value_is_an_empty_response()
    {
        using var response = await test.Client.GetAsync("/api/scalar-utf8-null/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("");
    }
}
