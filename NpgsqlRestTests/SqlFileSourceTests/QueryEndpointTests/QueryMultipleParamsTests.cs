namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void QueryMultipleParamsTests()
    {
        File.WriteAllText(Path.Combine(Dir, "search_test.sql"), """
            -- @param $1 name_filter
            -- @param $2 active_filter
            select id, name, active from sql_describe_test where name like $1 and active = $2;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class QueryMultipleParamsTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task SearchTest_MatchingFilter_ReturnsFilteredRows()
    {
        using var response = await test.Client.GetAsync("/api/search-test?name_filter=%25test%25&active_filter=true");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);

        // Only active rows matching the name filter should be returned
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            row.GetProperty("active").GetBoolean().Should().BeTrue();
            row.GetProperty("name").GetString().Should().Contain("test");
        }
    }

    [Fact]
    public async Task SearchTest_NoMatch_ReturnsEmptyArray()
    {
        using var response = await test.Client.GetAsync("/api/search-test?name_filter=nonexistent&active_filter=true");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("[]");
    }
}
