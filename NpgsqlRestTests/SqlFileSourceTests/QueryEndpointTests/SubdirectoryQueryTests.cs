namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void SubdirectoryQueryTests()
    {
        File.WriteAllText(Path.Combine(SubDir, "sub_query.sql"), """
            select 42 as answer;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class SubdirectoryQueryTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task SubQuery_InSubdirectory_Returns42()
    {
        using var response = await test.Client.GetAsync("/api/sub-query");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(1);
        doc.RootElement[0].GetInt32().Should().Be(42);
    }
}
