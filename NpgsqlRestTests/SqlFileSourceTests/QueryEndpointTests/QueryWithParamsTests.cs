namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("SqlFileSourceFixture")]
public class QueryWithParamsTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task GetById_ExistingId_ReturnsMatchingRow()
    {
        using var response = await test.Client.GetAsync("/api/get-by-id?id=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(1);
        doc.RootElement[0].GetProperty("name").GetString().Should().Be("test1");
        doc.RootElement[0].GetProperty("active").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetById_NonExistentId_ReturnsEmptyArray()
    {
        using var response = await test.Client.GetAsync("/api/get-by-id?id=999");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("[]");
    }
}
