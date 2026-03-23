using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("SqlFileSourceFixture")]
public class MultiSelectEndpointTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task MultiSelect_ReturnsJsonObjectWithCommandKeys()
    {
        // multi_select.sql has two SELECTs, both return results
        using var response = await test.Client.GetAsync("/api/multi-select?$1=1");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().StartWith("{");
        content.Should().EndWith("}");

        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);

        // Should have command1 and command2 keys
        doc.RootElement.TryGetProperty("command1", out var cmd1).Should().BeTrue();
        doc.RootElement.TryGetProperty("command2", out var cmd2).Should().BeTrue();

        // command1: SELECT id, name WHERE id = 1
        cmd1.ValueKind.Should().Be(JsonValueKind.Array);
        cmd1.GetArrayLength().Should().Be(1);
        cmd1[0].GetProperty("id").GetInt32().Should().Be(1);

        // command2: SELECT count(*)
        cmd2.ValueKind.Should().Be(JsonValueKind.Array);
        cmd2.GetArrayLength().Should().Be(1);
        cmd2[0].GetProperty("total").GetInt64().Should().BeGreaterThanOrEqualTo(2);
    }
}
