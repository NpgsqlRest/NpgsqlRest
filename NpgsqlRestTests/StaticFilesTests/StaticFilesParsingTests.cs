namespace NpgsqlRestTests.StaticFilesTests;

/// <summary>
/// Integration tests for StaticFiles content parsing.
/// These tests verify that:
/// - Claims are replaced in HTML files for authenticated users
/// - Antiforgery token/field name are injected correctly
/// - Non-parsed files are served without modification
/// - Cache headers are set correctly for parsed content
/// - Unauthenticated users get NULL for claim placeholders
/// </summary>
[Collection("StaticFilesTestFixture")]
public class StaticFilesParsingTests(StaticFilesTestFixture test)
{
    /// <summary>
    /// Test that user claims are replaced in HTML content for authenticated users.
    /// </summary>
    [Fact]
    public async Task Authenticated_User_Should_See_Claims_Replaced()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/public/index.html");

        // Act
        using var response = await test.AuthenticatedClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        content.Should().Contain(StaticFilesTestFixture.TestUserId,
            "User ID claim should be replaced with actual value");
        content.Should().Contain(StaticFilesTestFixture.TestUserName,
            "User Name claim should be replaced with actual value");
        content.Should().Contain(StaticFilesTestFixture.TestUserRole,
            "User Roles claim should be replaced with actual value");

        // Should NOT contain the placeholder syntax
        content.Should().NotContain("{user_id}",
            "Claim placeholder should be replaced, not left as template");
        content.Should().NotContain("{user_name}",
            "Claim placeholder should be replaced, not left as template");
    }

    /// <summary>
    /// Test that unauthenticated users get NULL for claim placeholders.
    /// </summary>
    [Fact]
    public async Task Unauthenticated_User_Should_See_Null_For_Claims()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/public/index.html");

        // Act
        using var response = await test.Client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Claims should be replaced with NULL or empty for unauthenticated users
        content.Should().NotContain("{user_id}",
            "Claim placeholder should be replaced even for unauthenticated users");
        content.Should().NotContain("{user_name}",
            "Claim placeholder should be replaced even for unauthenticated users");
    }

    /// <summary>
    /// Test that antiforgery field name is injected correctly.
    /// </summary>
    [Fact]
    public async Task Antiforgery_Field_Name_Should_Be_Injected()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/public/index.html");

        // Act
        using var response = await test.Client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The antiforgery field name should be replaced
        content.Should().NotContain("{antiForgeryFieldName}",
            "Antiforgery field name placeholder should be replaced");

        // Should contain the actual configured field name
        content.Should().Contain("__TestAntiforgeryToken",
            "Antiforgery field name should be the configured value");
    }

    /// <summary>
    /// Test that antiforgery token is injected correctly.
    /// </summary>
    [Fact]
    public async Task Antiforgery_Token_Should_Be_Injected()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/public/index.html");

        // Act
        using var response = await test.Client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The antiforgery token placeholder should be replaced with an actual token
        content.Should().NotContain("{antiForgeryToken}",
            "Antiforgery token placeholder should be replaced");

        // The value should look like a token (non-empty, in the form input)
        content.Should().Contain("value=\"",
            "Form should have a value attribute with the token");
    }

    /// <summary>
    /// Test that non-HTML files are NOT parsed (served as-is).
    /// </summary>
    [Fact]
    public async Task NonHtml_Files_Should_Not_Be_Parsed()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/public/script.js");

        // Act
        using var response = await test.Client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/javascript");

        // Content should be the original JS content
        content.Should().Contain("console.log",
            "JavaScript file should be served without modification");
    }

    /// <summary>
    /// Test that cache headers are set for parsed content.
    /// </summary>
    [Fact]
    public async Task Parsed_Content_Should_Have_NoCache_Headers()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/public/index.html");

        // Act
        using var response = await test.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Check Cache-Control header
        response.Headers.TryGetValues("Cache-Control", out var cacheControlValues);
        if (cacheControlValues != null)
        {
            var cacheControl = string.Join(",", cacheControlValues).ToLowerInvariant();
            cacheControl.Should().ContainAny("no-store", "no-cache",
                "Parsed content should have no-cache or no-store header");
        }
    }

    /// <summary>
    /// Test that Content-Type is set correctly for HTML files.
    /// </summary>
    [Fact]
    public async Task Html_Files_Should_Have_Correct_ContentType()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/public/index.html");

        // Act
        using var response = await test.Client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html",
            "HTML files should have text/html content type");
    }

    /// <summary>
    /// Test that protected parsed content includes user claims for authenticated user.
    /// </summary>
    [Fact]
    public async Task Protected_Parsed_Content_Should_Include_User_Claims()
    {
        // Arrange - access protected page as authenticated user
        var request = new HttpRequestMessage(HttpMethod.Get, "/protected/secret.html");

        // Act
        using var response = await test.AuthenticatedClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // User name should be replaced
        content.Should().Contain(StaticFilesTestFixture.TestUserName,
            "Protected page should show authenticated user's name");
        content.Should().Contain(StaticFilesTestFixture.TestUserId,
            "Protected page should show authenticated user's ID");
    }
}
