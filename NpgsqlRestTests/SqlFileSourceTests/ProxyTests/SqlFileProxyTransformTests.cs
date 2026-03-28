using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("SqlFileProxyFixture")]
public class SqlFileProxyTransformTests(SqlFileProxyFixture test)
{
    [Fact]
    public async Task SqlFile_ProxyTransformBody_ReceivesUpstreamBodyInSql()
    {
        test.Server.Reset();
        test.Server
            .Given(Request.Create().WithPath("/api/sf-proxy-transform-body").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("proxy response body"));

        using var response = await test.Client.GetAsync("/api/sf-proxy-transform-body");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Contain("Received: proxy response body");
    }

    [Fact]
    public async Task SqlFile_ProxyTransformAll_ReceivesStatusBodyAndSuccess()
    {
        test.Server.Reset();
        test.Server
            .Given(Request.Create().WithPath("/api/sf-proxy-transform-all").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("full response body"));

        using var response = await test.Client.GetAsync("/api/sf-proxy-transform-all");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        using var doc = JsonDocument.Parse(content);
        var arr = doc.RootElement;
        arr.ValueKind.Should().Be(JsonValueKind.Array);
        arr.GetArrayLength().Should().Be(1);

        var row = arr[0];
        row.GetProperty("statusCode").GetInt32().Should().Be(200);
        row.GetProperty("body").GetString().Should().Be("full response body");
        row.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task SqlFile_ProxyTransformAll_FailedUpstream_ReceivesFailureInfo()
    {
        test.Server.Reset();
        test.Server
            .Given(Request.Create().WithPath("/api/sf-proxy-transform-all").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBody("server error"));

        using var response = await test.Client.GetAsync("/api/sf-proxy-transform-all");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        using var doc = JsonDocument.Parse(content);
        var row = doc.RootElement[0];
        row.GetProperty("statusCode").GetInt32().Should().Be(500);
        row.GetProperty("body").GetString().Should().Be("server error");
        row.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task SqlFile_ProxyWithUserParam_PassesBothUserAndProxyParams()
    {
        test.Server.Reset();
        test.Server
            .Given(Request.Create().WithPath("/api/sf-proxy-with-user-param").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("upstream data"));

        using var response = await test.Client.GetAsync("/api/sf-proxy-with-user-param?name=Alice");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Contain("Alice");
        content.Should().Contain("upstream data");
    }
}
