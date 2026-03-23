namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void VerbOverrideTests()
    {
        File.WriteAllText(Path.Combine(Dir, "annotated_query.sql"), """
            -- HTTP GET
            -- @param $1 from_date
            -- @param $2 to_date
            select id, name, created_at from sql_describe_test where created_at between $1 and $2;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class VerbOverrideTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task AnnotatedQuery_HttpGetOverride_RespondsToGet()
    {
        using var response = await test.Client.GetAsync(
            "/api/annotated-query?from_date=2023-01-01T00:00:00Z&to_date=2025-01-01T00:00:00Z");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

        // Should return rows within the date range
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            row.TryGetProperty("id", out _).Should().BeTrue();
            row.TryGetProperty("name", out _).Should().BeTrue();
            row.TryGetProperty("createdAt", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task AnnotatedQuery_NarrowDateRange_ReturnsMatchingRows()
    {
        // Both test rows have created_at in 2024-01
        using var response = await test.Client.GetAsync(
            "/api/annotated-query?from_date=2024-01-01T00:00:00Z&to_date=2024-01-03T00:00:00Z");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.GetArrayLength().Should().Be(2);
    }
}
