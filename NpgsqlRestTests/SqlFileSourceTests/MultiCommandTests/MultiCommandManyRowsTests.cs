namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void MultiCommandManyRowsTests()
    {
        File.WriteAllText(Path.Combine(Dir, "multi_many_rows.sql"), """
            -- @param $1 count
            select i as id, 'name ' || i as name, array['a' || i, 'b"' || i] as tags from generate_series(1, $1::int) i;
            select $1::int as requested;
            """);
    }
}

/// <summary>
/// Multi-statement SQL files with statements that return more rows than BufferRows (25 by default): every row must be
/// in the response exactly once. Rows already sent used to stay in the row buffer, so from the 26th row on the
/// response repeated rows and was not valid JSON.
/// </summary>
[Collection("SqlFileSourceFixture")]
public class MultiCommandManyRowsTests(SqlFileSourceTestFixture test)
{
    private static string Expected(int count) =>
        "{\"result1\":[" +
        string.Join(",", Enumerable.Range(1, count).Select(i => $"{{\"id\":{i},\"name\":\"name {i}\",\"tags\":[\"a{i}\",\"b\\\"{i}\"]}}")) +
        $"],\"result2\":[{count}]}}"; // a single unnamed-set column is an array of values

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(60)]
    [InlineData(1000)]
    public async Task Every_row_is_returned_once(int count)
    {
        using var response = await test.Client.GetAsync($"/api/multi-many-rows?count={count}");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be(Expected(count));
    }
}
