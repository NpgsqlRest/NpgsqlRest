using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("SqlFileSourceFixture")]
public class MultiMixedEndpointTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task MultiMixed_VoidCommandOmitted_AnnotatedNamesUsed()
    {
        // multi_mixed.sql: SELECT (annotated "lookup") + INSERT (void) + SELECT (annotated "verify")
        // The INSERT is void → omitted from response
        using var response = await test.Client.GetAsync("/api/multi-mixed?$1=1");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);

        // "lookup" — first SELECT
        doc.RootElement.TryGetProperty("lookup", out var lookup).Should().BeTrue(
            $"Expected 'lookup' key. Response: {content}");
        lookup.ValueKind.Should().Be(JsonValueKind.Array);
        lookup[0].GetProperty("name").GetString().Should().Be("test1");

        // "verify" — third SELECT (second is void INSERT, omitted)
        doc.RootElement.TryGetProperty("verify", out var verify).Should().BeTrue(
            $"Expected 'verify' key. Response: {content}");
        verify.ValueKind.Should().Be(JsonValueKind.Array);
        verify[0].GetProperty("total").GetInt64().Should().BeGreaterThanOrEqualTo(2);

        // The void INSERT command should NOT appear
        doc.RootElement.TryGetProperty("command2", out _).Should().BeFalse(
            "Void INSERT should be omitted from response");
    }
}
