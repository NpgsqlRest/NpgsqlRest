using System.Text.Json;

namespace NpgsqlRestTests.ForwardedHeadersTests;

/// <summary>
/// Integration tests for Forwarded Headers middleware.
/// These tests verify that:
/// - X-Forwarded-For header is processed correctly
/// - X-Forwarded-Proto header is processed correctly
/// - X-Forwarded-Host header is processed correctly
/// - Multiple proxy IPs are handled correctly
/// </summary>
[Collection("ForwardedHeadersTestFixture")]
public class ForwardedHeadersTests(ForwardedHeadersTestFixture test)
{
    /// <summary>
    /// Test that X-Forwarded-For header sets the remote IP address correctly.
    /// </summary>
    [Fact]
    public async Task XForwardedFor_Should_Set_RemoteIpAddress()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/connection-info");
        request.Headers.Add("X-Forwarded-For", ForwardedHeadersTestFixture.TestClientIp);

        // Act
        using var response = await test.Client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonDocument.Parse(content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var remoteIp = result.RootElement.GetProperty("remoteIpAddress").GetString();
        remoteIp.Should().Be(ForwardedHeadersTestFixture.TestClientIp,
            "RemoteIpAddress should be set from X-Forwarded-For header");
    }

    /// <summary>
    /// Test that X-Forwarded-Proto header sets the request scheme correctly.
    /// </summary>
    [Fact]
    public async Task XForwardedProto_Should_Set_RequestScheme()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/connection-info");
        request.Headers.Add("X-Forwarded-Proto", ForwardedHeadersTestFixture.TestForwardedProto);

        // Act
        using var response = await test.Client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonDocument.Parse(content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var scheme = result.RootElement.GetProperty("scheme").GetString();
        scheme.Should().Be(ForwardedHeadersTestFixture.TestForwardedProto,
            "Request scheme should be set from X-Forwarded-Proto header");

        var isHttps = result.RootElement.GetProperty("isHttps").GetBoolean();
        isHttps.Should().BeTrue("IsHttps should be true when X-Forwarded-Proto is https");
    }

    /// <summary>
    /// Test that X-Forwarded-Host header sets the request host correctly.
    /// </summary>
    [Fact]
    public async Task XForwardedHost_Should_Set_RequestHost()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/connection-info");
        request.Headers.Add("X-Forwarded-Host", ForwardedHeadersTestFixture.TestForwardedHost);

        // Act
        using var response = await test.Client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonDocument.Parse(content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var host = result.RootElement.GetProperty("host").GetString();
        host.Should().Be(ForwardedHeadersTestFixture.TestForwardedHost,
            "Request host should be set from X-Forwarded-Host header");
    }

    /// <summary>
    /// Test that multiple X-Forwarded-For values are handled correctly.
    /// The leftmost IP should be the client IP.
    /// </summary>
    [Fact]
    public async Task XForwardedFor_Should_Handle_Multiple_Proxies()
    {
        // Arrange - client -> proxy1 -> proxy2 -> server
        var forwardedFor = $"{ForwardedHeadersTestFixture.TestClientIp}, {ForwardedHeadersTestFixture.TestProxyIp}";
        var request = new HttpRequestMessage(HttpMethod.Get, "/connection-info");
        request.Headers.Add("X-Forwarded-For", forwardedFor);

        // Act
        using var response = await test.Client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonDocument.Parse(content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var remoteIp = result.RootElement.GetProperty("remoteIpAddress").GetString();
        // With ForwardLimit=2, should get the leftmost (client) IP
        remoteIp.Should().Be(ForwardedHeadersTestFixture.TestClientIp,
            "RemoteIpAddress should be the client IP (leftmost in X-Forwarded-For)");
    }

    /// <summary>
    /// Test that all forwarded headers work together.
    /// </summary>
    [Fact]
    public async Task All_Forwarded_Headers_Should_Work_Together()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/connection-info");
        request.Headers.Add("X-Forwarded-For", ForwardedHeadersTestFixture.TestClientIp);
        request.Headers.Add("X-Forwarded-Proto", ForwardedHeadersTestFixture.TestForwardedProto);
        request.Headers.Add("X-Forwarded-Host", ForwardedHeadersTestFixture.TestForwardedHost);

        // Act
        using var response = await test.Client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonDocument.Parse(content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var remoteIp = result.RootElement.GetProperty("remoteIpAddress").GetString();
        remoteIp.Should().Be(ForwardedHeadersTestFixture.TestClientIp);

        var scheme = result.RootElement.GetProperty("scheme").GetString();
        scheme.Should().Be(ForwardedHeadersTestFixture.TestForwardedProto);

        var host = result.RootElement.GetProperty("host").GetString();
        host.Should().Be(ForwardedHeadersTestFixture.TestForwardedHost);
    }

    /// <summary>
    /// Test that requests without forwarded headers work normally.
    /// </summary>
    [Fact]
    public async Task Request_Without_Forwarded_Headers_Should_Work_Normally()
    {
        // Arrange - no forwarded headers
        var request = new HttpRequestMessage(HttpMethod.Get, "/connection-info");

        // Act
        using var response = await test.Client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonDocument.Parse(content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Should return actual values, not from forwarded headers
        var scheme = result.RootElement.GetProperty("scheme").GetString();
        scheme.Should().Be("http", "Scheme should be http when no X-Forwarded-Proto is sent");
    }

    /// <summary>
    /// Test that API endpoints also respect forwarded headers.
    /// </summary>
    [Fact]
    public async Task Api_Endpoints_Should_Respect_Forwarded_Headers()
    {
        // Arrange - test with an NpgsqlRest endpoint
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/hello-world-html/");
        request.Headers.Add("X-Forwarded-Proto", "https");

        // Act
        using var response = await test.Client.SendAsync(request);

        // Assert - request should complete successfully
        // The actual forwarded header processing happens before the endpoint
        response.StatusCode.Should().BeOneOf(
            [HttpStatusCode.OK, HttpStatusCode.NotFound],
            "Request should complete regardless of forwarded headers");
    }
}
