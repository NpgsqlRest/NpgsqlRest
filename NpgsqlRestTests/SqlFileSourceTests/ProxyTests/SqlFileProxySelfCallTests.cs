namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("SqlFileProxyFixture")]
public class SqlFileProxySelfCallTests(SqlFileProxyFixture test)
{
    [Fact]
    public async Task SqlFile_SelfTarget_ReturnsDirectly()
    {
        using var response = await test.Client.GetAsync("/api/sf-self-target");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Contain("Hello from SQL file");
    }

    [Fact]
    public async Task SqlFile_ProxySelfPassthrough_ReturnsTargetResponse()
    {
        using var response = await test.Client.GetAsync("/api/sf-proxy-self-passthrough");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Contain("Hello from SQL file");
    }

    [Fact]
    public async Task SqlFile_ProxySelfTransform_ReceivesStatusBodySuccess()
    {
        using var response = await test.Client.GetAsync("/api/sf-proxy-self-transform");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        using var doc = JsonDocument.Parse(content);
        var arr = doc.RootElement;
        arr.ValueKind.Should().Be(JsonValueKind.Array);
        arr.GetArrayLength().Should().Be(1);

        var row = arr[0];
        row.GetProperty("statusCode").GetInt32().Should().Be(200);
        row.GetProperty("body").GetString().Should().Contain("Hello from SQL file");
        row.GetProperty("success").GetBoolean().Should().BeTrue();
    }
}
