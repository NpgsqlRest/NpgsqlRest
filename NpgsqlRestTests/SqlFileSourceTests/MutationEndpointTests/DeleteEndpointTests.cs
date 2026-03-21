namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("SqlFileSourceFixture")]
public class DeleteEndpointTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task DeleteTest_InsertThenDelete_ReturnsDeletedId()
    {
        // First insert a row to delete (use unique ID 200 to avoid conflicts)
        var insertJson = new StringContent("""{"id": 200, "name": "to_delete"}""", Encoding.UTF8, "application/json");
        using var insertResponse = await test.Client.PutAsync("/api/insert-test", insertJson);
        insertResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Now delete it
        using var response = await test.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, "/api/delete-test?id=200"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(1);
        doc.RootElement[0].GetProperty("id").GetInt32().Should().Be(200);
    }

    [Fact]
    public async Task DeleteTest_NonExistentId_ReturnsEmptyArray()
    {
        using var response = await test.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, "/api/delete-test?id=999"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("[]");
    }
}
