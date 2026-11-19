namespace NpgsqlRestTests;

public static partial class Database
{
    public static void BufferRowsTests()
    {
        script.Append("""
create function buffer_rows_zero() returns table (id int, name text) language sql as $$
select i, 'row ' || i from generate_series(1, 5) i $$;
comment on function buffer_rows_zero() is 'HTTP GET
buffer_rows 0';

create function buffer_rows_one() returns table (id int, name text) language sql as $$
select i, 'row ' || i from generate_series(1, 5) i $$;
comment on function buffer_rows_one() is 'HTTP GET
buffer_rows 1';

create function buffer_rows_two() returns table (id int, name text) language sql as $$
select i, 'row ' || i from generate_series(1, 5) i $$;
comment on function buffer_rows_two() is 'HTTP GET
buffer_rows 2';

-- fails at row 40 (division by zero), after more than the default 25 buffered rows.
-- stable + a single select: PostgreSQL inlines the function and streams its rows, so the first 39 rows really
-- arrive before the error (a non-inlined function would be materialized and fail before sending any row).
create function buffer_rows_fails_midway() returns table (id int, value int) language sql stable as $$
select i, 100 / (40 - i) from generate_series(1, 60) i $$;
comment on function buffer_rows_fails_midway() is 'HTTP GET';

create function buffer_rows_large(_count int) returns table (id int, name text, tags text[]) language sql as $$
select i, 'name ' || i, array['a' || i, 'b' || i] from generate_series(1, _count) i $$;
comment on function buffer_rows_large(int) is 'HTTP GET';
""");
    }
}

/// <summary>
/// buffer_rows controls how many rows are collected before they are written to the response: 0 = every row,
/// 1 = all rows at once, N = every N rows. The response is the same in every mode.
/// (0 used to fail with a division by zero.)
/// </summary>
[Collection("TestFixture")]
public class BufferRowsTests(TestFixture test)
{
    private const string Expected =
        "[{\"id\":1,\"name\":\"row 1\"},{\"id\":2,\"name\":\"row 2\"},{\"id\":3,\"name\":\"row 3\"},{\"id\":4,\"name\":\"row 4\"},{\"id\":5,\"name\":\"row 5\"}]";

    [Theory]
    [InlineData("/api/buffer-rows-zero/")]
    [InlineData("/api/buffer-rows-one/")]
    [InlineData("/api/buffer-rows-two/")]
    public async Task Rows_are_complete_for_every_buffer_setting(string path)
    {
        using var response = await test.Client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(Expected);
    }

    /// <summary>
    /// Rows are sent once 16 KB is collected (ResponseBufferBytes), not after every 25 rows. A query that fails before
    /// that has sent nothing yet, so the client gets the error - it used to get status 200 with the first 25 rows and
    /// a truncated body.
    /// </summary>
    [Fact]
    public async Task Query_failing_midway_returns_an_error_not_a_truncated_response()
    {
        using var response = await test.Client.GetAsync("/api/buffer-rows-fails-midway/");
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"title\":\"22012: division by zero\",\"status\":500,\"detail\":\"22012\"}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(3000)] // ~150 KB: sent in several pieces
    public async Task Large_and_small_responses_are_complete(int count)
    {
        using var response = await test.Client.GetAsync($"/api/buffer-rows-large/?count={count}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var expected = "[" + string.Join(",", Enumerable.Range(1, count).Select(i => $"{{\"id\":{i},\"name\":\"name {i}\",\"tags\":[\"a{i}\",\"b{i}\"]}}")) + "]";
        (await response.Content.ReadAsStringAsync()).Should().Be(expected);
    }

    [Fact]
    public async Task ResponseBufferBytes_zero_sends_every_batch_and_the_response_is_the_same()
    {
        var saved = NpgsqlRestOptions.Options.ResponseBufferBytes;
        NpgsqlRestOptions.Options.ResponseBufferBytes = 0;
        try
        {
            using var response = await test.Client.GetAsync("/api/buffer-rows-large/?count=120");
            var expected = "[" + string.Join(",", Enumerable.Range(1, 120).Select(i => $"{{\"id\":{i},\"name\":\"name {i}\",\"tags\":[\"a{i}\",\"b{i}\"]}}")) + "]";
            (await response.Content.ReadAsStringAsync()).Should().Be(expected);
        }
        finally
        {
            NpgsqlRestOptions.Options.ResponseBufferBytes = saved;
        }
    }
}
