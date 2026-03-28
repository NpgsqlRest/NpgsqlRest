using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("SqlFileProxyFixture")]
public class SqlFileProxyPassthroughTests(SqlFileProxyFixture test)
{
    [Fact]
    public async Task SqlFile_ProxyPassthrough_ReturnsUpstreamResponse()
    {
        test.Server.Reset();
        test.Server
            .Given(Request.Create().WithPath("/api/sf-proxy-passthrough").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"message\":\"hello from proxy\"}"));

        using var response = await test.Client.GetAsync("/api/sf-proxy-passthrough");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Contain("hello from proxy");
    }

    [Fact]
    public async Task SqlFile_ProxyPassthrough_ForwardsStatusCode()
    {
        test.Server.Reset();
        test.Server
            .Given(Request.Create().WithPath("/api/sf-proxy-passthrough").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(404)
                .WithBody("not found"));

        using var response = await test.Client.GetAsync("/api/sf-proxy-passthrough");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        content.Should().Be("not found");
    }

    [Fact]
    public async Task SqlFile_ProxyPassthrough_ForwardsContentType()
    {
        test.Server.Reset();
        test.Server
            .Given(Request.Create().WithPath("/api/sf-proxy-passthrough").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "text/xml")
                .WithBody("<data/>"));

        using var response = await test.Client.GetAsync("/api/sf-proxy-passthrough");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.ToString().Should().Contain("text/xml");
        content.Should().Be("<data/>");
    }

    [Fact]
    public async Task SqlFile_ProxyPassthrough_ForwardsErrorResponse()
    {
        test.Server.Reset();
        test.Server
            .Given(Request.Create().WithPath("/api/sf-proxy-passthrough").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithBody("internal server error"));

        using var response = await test.Client.GetAsync("/api/sf-proxy-passthrough");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        content.Should().Be("internal server error");
    }

    [Fact]
    public async Task SqlFile_ProxyMethodOverride_UsesSpecifiedMethod()
    {
        test.Server.Reset();
        test.Server
            .Given(Request.Create().WithPath("/api/sf-proxy-method-override").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("posted"));

        using var response = await test.Client.GetAsync("/api/sf-proxy-method-override");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("posted");
    }

    [Fact]
    public async Task SqlFile_ProxyExplicitHost_UsesSpecifiedHost()
    {
        test.Server.Reset();
        test.Server
            .Given(Request.Create().WithPath("/api/sf-proxy-explicit-host").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("explicit host ok"));

        using var response = await test.Client.GetAsync("/api/sf-proxy-explicit-host");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("explicit host ok");
    }
}
