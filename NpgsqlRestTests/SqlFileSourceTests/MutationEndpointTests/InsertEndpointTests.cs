namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("SqlFileSourceFixture")]
public class InsertEndpointTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task InsertTest_NewRow_ReturnsInsertedData()
    {
        // Insert a new row with a high ID to avoid conflicts
        var json = new StringContent("""{"id": 100, "name": "new_item"}""", Encoding.UTF8, "application/json");
        using var response = await test.Client.PutAsync("/api/insert-test", json);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(1);
        doc.RootElement[0].GetProperty("id").GetInt32().Should().Be(100);
        doc.RootElement[0].GetProperty("name").GetString().Should().Be("new_item");

        // Cleanup: delete the inserted row
        using var cleanup = await test.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, "/api/delete-test?id=100"));
    }
}
