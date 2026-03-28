using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests.SqlFileSourceTests;

/// <summary>
/// Tests that proxy response parameter detection works regardless of annotation order.
/// Previously, 'param' annotations had to come BEFORE 'proxy' annotation because
/// DetectProxyResponseParameters ran during HandleProxy before param renames.
/// </summary>
[Collection("SqlFileProxyFixture")]
public class SqlFileProxyAnnotationOrderTests(SqlFileProxyFixture test)
{
    [Fact]
    public async Task SqlFile_ProxyAfterParam_TransformBody_Works()
    {
        // This is the existing working case: param before proxy
        test.Server.Reset();
        test.Server
            .Given(Request.Create().WithPath("/api/sf-proxy-transform-body").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("body from upstream"));

        using var response = await test.Client.GetAsync("/api/sf-proxy-transform-body");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        // This test just confirms the baseline (param before proxy) still works
        content.Should().Contain("Received: body from upstream");
    }

    [Fact]
    public async Task SqlFile_ProxyBeforeParam_TransformBody_Works()
    {
        // Limitation #1: proxy annotation comes BEFORE param annotation
        test.Server.Reset();
        test.Server
            .Given(Request.Create().WithPath("/api/sf-proxy-order-body").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("body from upstream"));

        using var response = await test.Client.GetAsync("/api/sf-proxy-order-body");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Contain("Got: body from upstream");
    }

    [Fact]
    public async Task SqlFile_ProxyBeforeParam_TransformAll_Works()
    {
        // Limitation #1: proxy annotation comes BEFORE multiple param annotations
        test.Server.Reset();
        test.Server
            .Given(Request.Create().WithPath("/api/sf-proxy-order-all").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("ordered response"));

        using var response = await test.Client.GetAsync("/api/sf-proxy-order-all");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        using var doc = JsonDocument.Parse(content);
        var arr = doc.RootElement;
        arr.ValueKind.Should().Be(JsonValueKind.Array);
        arr.GetArrayLength().Should().Be(1);

        var row = arr[0];
        row.GetProperty("statusCode").GetInt32().Should().Be(200);
        row.GetProperty("body").GetString().Should().Be("ordered response");
        row.GetProperty("success").GetBoolean().Should().BeTrue();
    }
}
