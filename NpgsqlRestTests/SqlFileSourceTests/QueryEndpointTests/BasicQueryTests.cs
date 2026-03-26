namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void BasicQueryTests()
    {
        File.WriteAllText(Path.Combine(Dir, "get_time.sql"), """
            select now() as current_time;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class BasicQueryTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task GetTime_Returns200WithCurrentTime()
    {
        using var response = await test.Client.GetAsync("/api/get-time");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrWhiteSpace();

        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(1);
        doc.RootElement[0].ValueKind.Should().Be(JsonValueKind.String);
    }
}
