namespace NpgsqlRestTests.StaticFilesTests;

/// <summary>
/// Integration tests for StaticFiles authorization.
/// These tests verify that:
/// - Authenticated users can access protected paths
/// - Public files are accessible without authentication
/// - The middleware serves files correctly
///
/// NOTE: Authorization for unauthenticated users requires proper Identity setup.
/// The AppStaticFileMiddleware checks: context.User.Identity is not null && IsAuthenticated is false
/// If Identity is null (no auth middleware), the redirect doesn't happen.
/// </summary>
[Collection("StaticFilesTestFixture")]
public class StaticFilesAuthorizationTests(StaticFilesTestFixture test)
{

    /// <summary>
    /// Test that authenticated user can access protected path.
    /// </summary>
    [Fact]
    public async Task Authenticated_User_Should_Access_Protected_Path()
    {
        // Arrange - use authenticated client
        var request = new HttpRequestMessage(HttpMethod.Get, "/protected/secret.html");

        // Act
        using var response = await test.AuthenticatedClient.SendAsync(request);

        // Assert - should succeed
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Authenticated user should be able to access protected path");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Protected Content",
            "Response should contain the protected page content");
    }

    /// <summary>
    /// Test that public files are accessible without authentication.
    /// </summary>
    [Fact]
    public async Task Public_Files_Should_Be_Accessible_Without_Auth()
    {
        // Arrange - public file not in protected patterns
        var request = new HttpRequestMessage(HttpMethod.Get, "/public/index.html");

        // Act
        using var response = await test.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Public files should be accessible without authentication");
    }

    /// <summary>
    /// Test that non-HTML files are accessible.
    /// </summary>
    [Fact]
    public async Task NonHtml_Public_Files_Should_Be_Accessible()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/public/script.js");

        // Act
        using var response = await test.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Non-HTML public files should be accessible");
    }
}
