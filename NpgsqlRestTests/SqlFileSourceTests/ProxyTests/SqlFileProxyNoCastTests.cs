using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests.SqlFileSourceTests;

/// <summary>
/// Tests that proxy response parameters produce correctly typed JSON output
/// when using @param type annotations WITHOUT explicit SQL casts.
/// Previously, users had to write $1::integer in SQL even after annotating with 'param $1 _proxy_status_code integer'.
/// </summary>
[Collection("SqlFileProxyFixture")]
public class SqlFileProxyNoCastTests(SqlFileProxyFixture test)
{
    [Fact]
    public async Task SqlFile_ProxyNoCast_StatusCodeIsNumber()
    {
        test.Server.Reset();
        test.Server
            .Given(Request.Create().WithPath("/api/sf-proxy-no-cast").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("test body"));

        using var response = await test.Client.GetAsync("/api/sf-proxy-no-cast");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        using var doc = JsonDocument.Parse(content);
        var row = doc.RootElement[0];

        // Status code should be a JSON number, not a string
        row.GetProperty("statusCode").ValueKind.Should().Be(JsonValueKind.Number,
            "status_code should be serialized as a number when param annotation specifies integer type");
        row.GetProperty("statusCode").GetInt32().Should().Be(200);
    }

    [Fact]
    public async Task SqlFile_ProxyNoCast_SuccessIsBoolean()
    {
        test.Server.Reset();
        test.Server
            .Given(Request.Create().WithPath("/api/sf-proxy-no-cast").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("test body"));

        using var response = await test.Client.GetAsync("/api/sf-proxy-no-cast");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        using var doc = JsonDocument.Parse(content);
        var row = doc.RootElement[0];

        // Success should be a JSON boolean, not a string
        row.GetProperty("success").ValueKind.Should().Be(JsonValueKind.True,
            "success should be serialized as a boolean when param annotation specifies boolean type");
        row.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task SqlFile_ProxyNoCast_BodyIsString()
    {
        test.Server.Reset();
        test.Server
            .Given(Request.Create().WithPath("/api/sf-proxy-no-cast").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("test body"));

        using var response = await test.Client.GetAsync("/api/sf-proxy-no-cast");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        using var doc = JsonDocument.Parse(content);
        var row = doc.RootElement[0];

        row.GetProperty("body").GetString().Should().Be("test body");
    }

    [Fact]
    public async Task SqlFile_ProxyNoCast_FailedUpstream_AllTypesCorrect()
    {
        test.Server.Reset();
        test.Server
            .Given(Request.Create().WithPath("/api/sf-proxy-no-cast").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBody("error body"));

        using var response = await test.Client.GetAsync("/api/sf-proxy-no-cast");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        using var doc = JsonDocument.Parse(content);
        var row = doc.RootElement[0];

        row.GetProperty("statusCode").ValueKind.Should().Be(JsonValueKind.Number);
        row.GetProperty("statusCode").GetInt32().Should().Be(500);
        row.GetProperty("body").GetString().Should().Be("error body");
        row.GetProperty("success").ValueKind.Should().Be(JsonValueKind.False);
        row.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task SqlFile_ProxySelfNoCast_AllTypesCorrect()
    {
        using var response = await test.Client.GetAsync("/api/sf-proxy-self-no-cast");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        using var doc = JsonDocument.Parse(content);
        var row = doc.RootElement[0];

        row.GetProperty("statusCode").ValueKind.Should().Be(JsonValueKind.Number);
        row.GetProperty("statusCode").GetInt32().Should().Be(200);
        row.GetProperty("body").GetString().Should().Contain("Hello from SQL file");
        row.GetProperty("success").ValueKind.Should().Be(JsonValueKind.True);
        row.GetProperty("success").GetBoolean().Should().BeTrue();
    }
}
