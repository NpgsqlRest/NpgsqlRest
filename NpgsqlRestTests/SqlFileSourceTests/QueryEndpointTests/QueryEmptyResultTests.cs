namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("SqlFileSourceFixture")]
public class QueryEmptyResultTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task CountTest_ReturnsRowCount()
    {
        using var response = await test.Client.GetAsync("/api/count-test");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(1);
        doc.RootElement[0].GetProperty("total").GetInt64().Should().BeGreaterThanOrEqualTo(2);
    }
}
