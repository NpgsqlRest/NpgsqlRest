using System.Text.Json.Nodes;

namespace NpgsqlRestTests.CompressionTests;

/// <summary>
/// Tests for compression MIME type configuration.
/// These tests verify that the default MIME types include all necessary types
/// and that configuration defaults are consistent across the codebase.
/// </summary>
public class MimeTypeTests
{
    /// <summary>
    /// Verify that text/javascript is in the default compressible MIME types.
    /// This test would have caught the missing text/javascript MIME type bug.
    /// </summary>
    [Fact]
    public void Default_MimeTypes_Should_Include_TextJavaScript()
    {
        // Assert
        CompressionTestFixture.DefaultCompressibleMimeTypes
            .Should().Contain("text/javascript",
                "text/javascript must be in the default compressible MIME types - " +
                "static file providers often return this MIME type for .js files");
    }

    /// <summary>
    /// Verify that application/javascript is also in the default MIME types.
    /// Both text/javascript and application/javascript should be supported.
    /// </summary>
    [Fact]
    public void Default_MimeTypes_Should_Include_ApplicationJavaScript()
    {
        CompressionTestFixture.DefaultCompressibleMimeTypes
            .Should().Contain("application/javascript",
                "application/javascript must be in the default compressible MIME types");
    }

    /// <summary>
    /// Verify all common web content types are included in defaults.
    /// </summary>
    [Theory]
    [InlineData("text/html")]
    [InlineData("text/css")]
    [InlineData("text/plain")]
    [InlineData("application/json")]
    [InlineData("text/json")]
    [InlineData("application/xml")]
    [InlineData("text/xml")]
    [InlineData("image/svg+xml")]
    public void Default_MimeTypes_Should_Include_Common_Web_Types(string mimeType)
    {
        CompressionTestFixture.DefaultCompressibleMimeTypes
            .Should().Contain(mimeType,
                $"Common web content type {mimeType} should be compressible by default");
    }

    /// <summary>
    /// Verify font types are included (often large and benefit from compression).
    /// </summary>
    [Theory]
    [InlineData("font/woff")]
    [InlineData("font/woff2")]
    [InlineData("application/font-woff")]
    [InlineData("application/font-woff2")]
    public void Default_MimeTypes_Should_Include_Font_Types(string mimeType)
    {
        CompressionTestFixture.DefaultCompressibleMimeTypes
            .Should().Contain(mimeType,
                $"Font type {mimeType} should be compressible by default");
    }

    /// <summary>
    /// Test that verifies the MIME type list in CompressionTestFixture matches
    /// what we expect from the NpgsqlRestClient defaults.
    /// If this test fails, it means someone changed the defaults without updating tests.
    /// </summary>
    [Fact]
    public void Default_MimeTypes_Should_Have_Expected_Count()
    {
        // The expected MIME types as of the current implementation
        var expectedTypes = new[]
        {
            "text/plain",
            "text/css",
            "application/javascript",
            "text/javascript",
            "text/html",
            "application/xml",
            "text/xml",
            "application/json",
            "text/json",
            "image/svg+xml",
            "font/woff",
            "font/woff2",
            "application/font-woff",
            "application/font-woff2"
        };

        CompressionTestFixture.DefaultCompressibleMimeTypes
            .Should().BeEquivalentTo(expectedTypes,
                "Default MIME types should match expected list - update both fixture and this test if defaults change");
    }
}
