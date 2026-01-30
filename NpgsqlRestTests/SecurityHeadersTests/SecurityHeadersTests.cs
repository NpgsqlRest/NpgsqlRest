namespace NpgsqlRestTests.SecurityHeadersTests;

/// <summary>
/// Integration tests for Security Headers middleware.
/// These tests verify that:
/// - X-Content-Type-Options header is set correctly
/// - X-Frame-Options header is set correctly
/// - Referrer-Policy header is set correctly
/// - Content-Security-Policy header is set when configured
/// - Cross-Origin headers (COOP, COEP, CORP) work correctly
/// </summary>
[Collection("SecurityHeadersTestFixture")]
public class SecurityHeadersTests(SecurityHeadersTestFixture test)
{
    /// <summary>
    /// Test that X-Content-Type-Options header is present and set correctly.
    /// This header prevents MIME-type sniffing attacks.
    /// </summary>
    [Fact]
    public async Task Should_Return_XContentTypeOptions_Header()
    {
        // Act
        using var response = await test.Client.GetAsync("/test");

        // Assert
        response.Headers.TryGetValues("X-Content-Type-Options", out var values);
        values.Should().NotBeNull("X-Content-Type-Options header should be present");
        values!.Should().Contain(SecurityHeadersTestFixture.ExpectedXContentTypeOptions);
    }

    /// <summary>
    /// Test that X-Frame-Options header is present and set correctly.
    /// This header prevents clickjacking attacks.
    /// </summary>
    [Fact]
    public async Task Should_Return_XFrameOptions_Header()
    {
        // Act
        using var response = await test.Client.GetAsync("/test");

        // Assert
        response.Headers.TryGetValues("X-Frame-Options", out var values);
        values.Should().NotBeNull("X-Frame-Options header should be present");
        values!.Should().Contain(SecurityHeadersTestFixture.ExpectedXFrameOptions);
    }

    /// <summary>
    /// Test that Referrer-Policy header is present and set correctly.
    /// This header controls referrer information sent with requests.
    /// </summary>
    [Fact]
    public async Task Should_Return_ReferrerPolicy_Header()
    {
        // Act
        using var response = await test.Client.GetAsync("/test");

        // Assert
        response.Headers.TryGetValues("Referrer-Policy", out var values);
        values.Should().NotBeNull("Referrer-Policy header should be present");
        values!.Should().Contain(SecurityHeadersTestFixture.ExpectedReferrerPolicy);
    }

    /// <summary>
    /// Test that Content-Security-Policy header is present when configured.
    /// This header helps prevent XSS and other injection attacks.
    /// </summary>
    [Fact]
    public async Task Should_Return_ContentSecurityPolicy_Header()
    {
        // Act
        using var response = await test.Client.GetAsync("/test");

        // Assert - CSP is a response header, not content header
        response.Headers.TryGetValues("Content-Security-Policy", out var values);
        values.Should().NotBeNull("Content-Security-Policy header should be present");
        values!.Should().Contain(SecurityHeadersTestFixture.ExpectedContentSecurityPolicy);
    }

    /// <summary>
    /// Test that Permissions-Policy header is present when configured.
    /// This header controls browser feature access.
    /// </summary>
    [Fact]
    public async Task Should_Return_PermissionsPolicy_Header()
    {
        // Act
        using var response = await test.Client.GetAsync("/test");

        // Assert
        response.Headers.TryGetValues("Permissions-Policy", out var values);
        values.Should().NotBeNull("Permissions-Policy header should be present");
        values!.Should().Contain(SecurityHeadersTestFixture.ExpectedPermissionsPolicy);
    }

    /// <summary>
    /// Test that Cross-Origin-Opener-Policy header is present when configured.
    /// This header controls document sharing with cross-origin popups.
    /// </summary>
    [Fact]
    public async Task Should_Return_CrossOriginOpenerPolicy_Header()
    {
        // Act
        using var response = await test.Client.GetAsync("/test");

        // Assert
        response.Headers.TryGetValues("Cross-Origin-Opener-Policy", out var values);
        values.Should().NotBeNull("Cross-Origin-Opener-Policy header should be present");
        values!.Should().Contain(SecurityHeadersTestFixture.ExpectedCrossOriginOpenerPolicy);
    }

    /// <summary>
    /// Test that Cross-Origin-Embedder-Policy header is present when configured.
    /// This header controls cross-origin resource loading.
    /// </summary>
    [Fact]
    public async Task Should_Return_CrossOriginEmbedderPolicy_Header()
    {
        // Act
        using var response = await test.Client.GetAsync("/test");

        // Assert
        response.Headers.TryGetValues("Cross-Origin-Embedder-Policy", out var values);
        values.Should().NotBeNull("Cross-Origin-Embedder-Policy header should be present");
        values!.Should().Contain(SecurityHeadersTestFixture.ExpectedCrossOriginEmbedderPolicy);
    }

    /// <summary>
    /// Test that Cross-Origin-Resource-Policy header is present when configured.
    /// This header controls how resources are shared cross-origin.
    /// </summary>
    [Fact]
    public async Task Should_Return_CrossOriginResourcePolicy_Header()
    {
        // Act
        using var response = await test.Client.GetAsync("/test");

        // Assert
        response.Headers.TryGetValues("Cross-Origin-Resource-Policy", out var values);
        values.Should().NotBeNull("Cross-Origin-Resource-Policy header should be present");
        values!.Should().Contain(SecurityHeadersTestFixture.ExpectedCrossOriginResourcePolicy);
    }

    /// <summary>
    /// Test that security headers are present on API endpoints as well.
    /// </summary>
    [Fact]
    public async Task Should_Return_Security_Headers_On_Api_Endpoints()
    {
        // Act - use an actual NpgsqlRest endpoint
        using var response = await test.Client.GetAsync("/api/hello-world-html/");

        // Assert - verify key headers are present regardless of endpoint
        response.Headers.TryGetValues("X-Content-Type-Options", out var xctValues);
        xctValues.Should().NotBeNull("X-Content-Type-Options should be present on API endpoints");

        response.Headers.TryGetValues("Referrer-Policy", out var rpValues);
        rpValues.Should().NotBeNull("Referrer-Policy should be present on API endpoints");
    }

    /// <summary>
    /// Test that all security headers are present in a single request.
    /// </summary>
    [Fact]
    public async Task Should_Return_All_Security_Headers_Together()
    {
        // Act
        using var response = await test.Client.GetAsync("/test");

        // Assert - verify all headers are present
        response.Headers.TryGetValues("X-Content-Type-Options", out var xct);
        response.Headers.TryGetValues("X-Frame-Options", out var xfo);
        response.Headers.TryGetValues("Referrer-Policy", out var rp);
        response.Headers.TryGetValues("Permissions-Policy", out var pp);
        response.Headers.TryGetValues("Cross-Origin-Opener-Policy", out var coop);
        response.Headers.TryGetValues("Cross-Origin-Embedder-Policy", out var coep);
        response.Headers.TryGetValues("Cross-Origin-Resource-Policy", out var corp);

        xct.Should().NotBeNull();
        xfo.Should().NotBeNull();
        rp.Should().NotBeNull();
        pp.Should().NotBeNull();
        coop.Should().NotBeNull();
        coep.Should().NotBeNull();
        corp.Should().NotBeNull();
    }
}
