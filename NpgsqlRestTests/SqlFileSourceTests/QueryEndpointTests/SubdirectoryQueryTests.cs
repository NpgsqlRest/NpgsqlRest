namespace NpgsqlRestTests.SqlFileSourceTests;

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
        doc.RootElement[0].GetProperty("answer").GetInt32().Should().Be(42);
    }
}
