namespace NpgsqlRestTests.CorsTests;

/// <summary>
/// Integration tests for CORS (Cross-Origin Resource Sharing) configuration.
/// These tests verify that:
/// - Preflight OPTIONS requests return correct CORS headers
/// - AllowedOrigins configuration restricts correctly
/// - AllowCredentials header is present when configured
/// - Methods/Headers filtering works as expected
/// </summary>
[Collection("CorsTestFixture")]
public class CorsTests(CorsTestFixture test)
{
    /// <summary>
    /// Test that preflight OPTIONS request returns correct CORS headers for allowed origin.
    /// </summary>
    [Fact]
    public async Task Preflight_Request_Should_Return_Cors_Headers_For_Allowed_Origin()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/hello-world/");
        request.Headers.Add("Origin", CorsTestFixture.AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "Content-Type");

        // Act
        using var response = await test.Client.SendAsync(request);

        // Assert - Preflight should return 204 No Content or 200 OK
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.OK);

        // Verify CORS headers
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowOriginValues);
        allowOriginValues.Should().NotBeNull();
        allowOriginValues!.Should().Contain(CorsTestFixture.AllowedOrigin,
            "Access-Control-Allow-Origin should match the allowed origin");

        response.Headers.TryGetValues("Access-Control-Allow-Credentials", out var allowCredentialsValues);
        allowCredentialsValues.Should().NotBeNull();
        allowCredentialsValues!.Should().Contain("true",
            "Access-Control-Allow-Credentials should be true when configured");
    }

    /// <summary>
    /// Test that preflight request does not return CORS headers for disallowed origin.
    /// </summary>
    [Fact]
    public async Task Preflight_Request_Should_Not_Return_Cors_Headers_For_Disallowed_Origin()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/hello-world/");
        request.Headers.Add("Origin", CorsTestFixture.DisallowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");

        // Act
        using var response = await test.Client.SendAsync(request);

        // Assert - Should not have Access-Control-Allow-Origin for disallowed origin
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowOriginValues);

        if (allowOriginValues != null)
        {
            allowOriginValues.Should().NotContain(CorsTestFixture.DisallowedOrigin,
                "Disallowed origin should not be in Access-Control-Allow-Origin");
        }
    }

    /// <summary>
    /// Test that actual request returns CORS headers for allowed origin.
    /// Uses hello-world-html endpoint from the test database.
    /// </summary>
    [Fact]
    public async Task Actual_Request_Should_Return_Cors_Headers_For_Allowed_Origin()
    {
        // Arrange - use an endpoint that actually exists in the test database
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/hello-world-html/");
        request.Headers.Add("Origin", CorsTestFixture.AllowedOrigin);

        // Act
        using var response = await test.Client.SendAsync(request);

        // Assert - even if endpoint returns error, CORS headers should be present
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowOriginValues);
        allowOriginValues.Should().NotBeNull(
            "CORS headers should be present on responses when Origin header is sent");
        allowOriginValues!.Should().Contain(CorsTestFixture.AllowedOrigin);
    }

    /// <summary>
    /// Test that Access-Control-Allow-Methods header contains configured methods.
    /// </summary>
    [Fact]
    public async Task Preflight_Should_Return_Allowed_Methods()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/hello-world/");
        request.Headers.Add("Origin", CorsTestFixture.AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "PUT");

        // Act
        using var response = await test.Client.SendAsync(request);

        // Assert
        response.Headers.TryGetValues("Access-Control-Allow-Methods", out var allowMethodsValues);
        allowMethodsValues.Should().NotBeNull();

        var methods = string.Join(",", allowMethodsValues!);
        methods.Should().ContainAny("GET", "POST", "PUT", "DELETE",
            "Access-Control-Allow-Methods should contain configured methods");
    }

    /// <summary>
    /// Test that Access-Control-Allow-Headers header contains configured headers.
    /// </summary>
    [Fact]
    public async Task Preflight_Should_Return_Allowed_Headers()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/hello-world/");
        request.Headers.Add("Origin", CorsTestFixture.AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "Content-Type, X-Custom-Header");

        // Act
        using var response = await test.Client.SendAsync(request);

        // Assert
        response.Headers.TryGetValues("Access-Control-Allow-Headers", out var allowHeadersValues);
        allowHeadersValues.Should().NotBeNull();

        var headers = string.Join(",", allowHeadersValues!).ToLowerInvariant();
        headers.Should().Contain("content-type",
            "Access-Control-Allow-Headers should contain Content-Type");
    }

    /// <summary>
    /// Test that Access-Control-Max-Age header is present for preflight caching.
    /// </summary>
    [Fact]
    public async Task Preflight_Should_Return_Max_Age_Header()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/hello-world/");
        request.Headers.Add("Origin", CorsTestFixture.AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");

        // Act
        using var response = await test.Client.SendAsync(request);

        // Assert
        response.Headers.TryGetValues("Access-Control-Max-Age", out var maxAgeValues);
        maxAgeValues.Should().NotBeNull(
            "Access-Control-Max-Age should be present for preflight caching");

        if (int.TryParse(maxAgeValues!.First(), out var maxAge))
        {
            maxAge.Should().BeGreaterThan(0,
                "Access-Control-Max-Age should be a positive number");
        }
    }

    /// <summary>
    /// Test that request without Origin header doesn't return CORS headers.
    /// </summary>
    [Fact]
    public async Task Request_Without_Origin_Should_Not_Return_Cors_Headers()
    {
        // Arrange - no Origin header, use an existing endpoint
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/hello-world-html/");

        // Act
        using var response = await test.Client.SendAsync(request);

        // Assert - without Origin, no CORS headers should be returned
        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowOriginValues);
        allowOriginValues.Should().BeNull(
            "Access-Control-Allow-Origin should not be present without Origin request header");
    }

    /// <summary>
    /// Test that preflight for disallowed method is rejected.
    /// </summary>
    [Fact]
    public async Task Preflight_For_Disallowed_Method_Should_Not_Allow()
    {
        // Arrange - PATCH is not in our allowed methods list
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/hello-world/");
        request.Headers.Add("Origin", CorsTestFixture.AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "PATCH");

        // Act
        using var response = await test.Client.SendAsync(request);

        // Assert - Either no CORS headers or PATCH not in allowed methods
        response.Headers.TryGetValues("Access-Control-Allow-Methods", out var allowMethodsValues);

        if (allowMethodsValues != null)
        {
            var methods = string.Join(",", allowMethodsValues).ToUpperInvariant();
            methods.Should().NotContain("PATCH",
                "PATCH should not be in allowed methods when not configured");
        }
    }
}
