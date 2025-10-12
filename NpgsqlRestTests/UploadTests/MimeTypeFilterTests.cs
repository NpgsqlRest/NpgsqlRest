using NpgsqlRest.UploadHandlers.Handlers;

namespace NpgsqlRestTests.UploadTests;

public class UploadHandler : BaseUploadHandler
{
    protected override IEnumerable<string> GetParameters()
    {
        throw new NotImplementedException();
    }
    
    public bool CheckMimeTypes(string contentType, string[]? includedMimeTypePatterns, string[]? excludedMimeTypePatterns)
    {
        IncludedMimeTypePatterns = includedMimeTypePatterns;
        ExcludedMimeTypePatterns = excludedMimeTypePatterns;
        return CheckMimeTypes(contentType);
    }
}

public class MimeTypeFilterTests
{
    [Fact]
    public void CheckMimeTypes_NullIncludeAndExclude_ReturnsTrue()
    {
        // Arrange
        string contentType = "image/jpeg";

        // Act
        var result = new UploadHandler().CheckMimeTypes(contentType, null, null);

        // Assert
        result.Should().BeTrue("because null patterns should not restrict mime types");
    }

    [Fact]
    public void CheckMimeTypes_EmptyIncludeAndExclude_ReturnsTrue()
    {
        // Arrange
        string contentType = "image/jpeg";
        string[] includedPatterns = [];
        string[] excludedPatterns = [];

        // Act
        var result = new UploadHandler().CheckMimeTypes(contentType, includedPatterns, excludedPatterns);

        // Assert
        result.Should().BeTrue("because empty pattern arrays should not restrict mime types");
    }

    [Fact]
    public void CheckMimeTypes_MatchesIncludedPattern_ReturnsTrue()
    {
        // Arrange
        string contentType = "image/jpeg";
        string[] includedPatterns = ["image/*"];

        // Act
        var result = new UploadHandler().CheckMimeTypes(contentType, includedPatterns, null);

        // Assert
        result.Should().BeTrue("because the mime type matches the included pattern");
    }

    [Fact]
    public void CheckMimeTypes_MatchesOneOfMultipleIncludedPatterns_ReturnsTrue()
    {
        // Arrange
        string contentType = "image/jpeg";
        string[] includedPatterns = ["application/*", "image/*", "text/*"];

        // Act
        var result = new UploadHandler().CheckMimeTypes(contentType, includedPatterns, null);

        // Assert
        result.Should().BeTrue("because the mime type matches at least one of the included patterns");
    }

    [Fact]
    public void CheckMimeTypes_DoesNotMatchAnyIncludedPattern_ReturnsFalse()
    {
        // Arrange
        string contentType = "image/jpeg";
        string[] includedPatterns = ["application/*", "text/*"];

        // Act
        var result = new UploadHandler().CheckMimeTypes(contentType, includedPatterns, null);

        // Assert
        result.Should().BeFalse("because the mime type does not match any included pattern");
    }

    [Fact]
    public void CheckMimeTypes_MatchesExcludedPattern_ReturnsFalse()
    {
        // Arrange
        string contentType = "image/jpeg";
        string[] excludedPatterns = ["image/*"];

        // Act
        var result = new UploadHandler().CheckMimeTypes(contentType, null, excludedPatterns);

        // Assert
        result.Should().BeFalse("because the mime type matches an excluded pattern");
    }

    [Fact]
    public void CheckMimeTypes_MatchesOneOfMultipleExcludedPatterns_ReturnsFalse()
    {
        // Arrange
        string contentType = "image/jpeg";
        string[] excludedPatterns = ["application/*", "image/*", "text/*"];

        // Act
        var result = new UploadHandler().CheckMimeTypes(contentType, null, excludedPatterns);

        // Assert
        result.Should().BeFalse("because the mime type matches at least one excluded pattern");
    }

    [Fact]
    public void CheckMimeTypes_DoesNotMatchAnyExcludedPattern_ReturnsTrue()
    {
        // Arrange
        string contentType = "image/jpeg";
        string[] excludedPatterns = ["application/*", "text/*"];

        // Act
        var result = new UploadHandler().CheckMimeTypes(contentType, null, excludedPatterns);

        // Assert
        result.Should().BeTrue("because the mime type does not match any excluded pattern");
    }

    [Fact]
    public void CheckMimeTypes_MatchesIncludedButAlsoExcludedPattern_ReturnsFalse()
    {
        // Arrange
        string contentType = "image/jpeg";
        string[] includedPatterns = ["image/*"];
        string[] excludedPatterns = ["image/jpeg"];

        // Act
        var result = new UploadHandler().CheckMimeTypes(contentType, includedPatterns, excludedPatterns);

        // Assert
        result.Should().BeFalse("because the exclusion pattern takes precedence over inclusion");
    }

    [Fact]
    public void CheckMimeTypes_MatchesIncludedAndNoExcludedPattern_ReturnsTrue()
    {
        // Arrange
        string contentType = "image/jpeg";
        string[] includedPatterns = ["image/*"];
        string[] excludedPatterns = ["application/*"];

        // Act
        var result = new UploadHandler().CheckMimeTypes(contentType, includedPatterns, excludedPatterns);

        // Assert
        result.Should().BeTrue("because it matches an included pattern and no excluded patterns");
    }

    [Fact]
    public void CheckMimeTypes_ExactPatternMatching_WorksCorrectly()
    {
        // Arrange
        string contentType = "image/jpeg";
        string[] includedPatterns = ["image/jpeg"];

        // Act
        var result = new UploadHandler().CheckMimeTypes(contentType, includedPatterns, null);

        // Assert
        result.Should().BeTrue("because exact pattern matching should work");
    }

    [Fact]
    public void CheckMimeTypes_CaseInsensitiveMatching_DependsOnParserImplementation()
    {
        // Note: This test assumes Parser.IsPatternMatch handles case sensitivity as needed
        // Arrange
        string contentType = "IMAGE/JPEG";
        string[] includedPatterns = ["image/*"];

        // Act
        var result = new UploadHandler().CheckMimeTypes(contentType, includedPatterns, null);

        // Assert - The result depends on Parser.IsPatternMatch implementation
        // This test documents the expected behavior rather than asserting it
        // Change this assertion based on your actual Parser implementation
        result.Should().BeTrue("if Parser.IsPatternMatch is case-insensitive");
    }

    [Fact]
    public void CheckMimeTypes_EmptyContentType_HandledConsistently()
    {
        // Arrange
        string contentType = "";
        string[] includedPatterns = ["image/*"];

        // Act
        var result = new UploadHandler().CheckMimeTypes(contentType, includedPatterns, null);

        // Assert
        result.Should().BeFalse("because an empty content type should not match any pattern");
    }

    [Fact]
    public void CheckMimeTypes_NullContentType_HandledGracefully()
    {
        // Arrange
        string contentType = null!;
        string[] includedPatterns = ["image/*"];

        // Act & Assert
        // This test checks if the method handles null ContentType without throwing
        // Actual behavior depends on Parser.IsPatternMatch implementation
        Action act = () => new UploadHandler().CheckMimeTypes(contentType, includedPatterns, includedPatterns);
        act.Should().NotThrow("because the method should handle null content types gracefully");
    }
}
