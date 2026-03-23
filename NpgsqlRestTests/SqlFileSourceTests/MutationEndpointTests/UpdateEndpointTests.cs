namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void UpdateEndpointTests()
    {
        File.WriteAllText(Path.Combine(Dir, "update_test.sql"), """
            -- @param $1 new_name
            -- @param $2 id
            update sql_describe_test set name = $1 where id = $2 returning id, name;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class UpdateEndpointTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task UpdateTest_ExistingRow_ReturnsUpdatedData()
    {
        var json = new StringContent("""{"new_name": "updated_test1", "id": 1}""", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/update-test", json);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(1);
        doc.RootElement[0].GetProperty("id").GetInt32().Should().Be(1);
        doc.RootElement[0].GetProperty("name").GetString().Should().Be("updated_test1");

        // Restore original value
        var restore = new StringContent("""{"new_name": "test1", "id": 1}""", Encoding.UTF8, "application/json");
        using var restoreResponse = await test.Client.PostAsync("/api/update-test", restore);
    }
}
