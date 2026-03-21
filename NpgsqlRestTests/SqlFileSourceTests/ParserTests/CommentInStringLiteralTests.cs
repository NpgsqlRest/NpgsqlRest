using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class CommentInStringLiteralTests
{
    [Fact]
    public void DashDashInsideSingleQuote_NotExtractedAsComment()
    {
        var result = SqlFileParser.Parse("SELECT '-- not a comment'");
        result.Comment.Should().BeEmpty();
        result.Statements.Should().ContainSingle().Which.Should().Contain("-- not a comment");
    }

    [Fact]
    public void BlockCommentInsideSingleQuote_NotExtractedAsComment()
    {
        var result = SqlFileParser.Parse("SELECT '/* not a comment */'");
        result.Comment.Should().BeEmpty();
        result.Statements.Should().ContainSingle().Which.Should().Contain("/* not a comment */");
    }

    [Fact]
    public void MixedRealAndStringComment_OnlyRealExtracted()
    {
        var sql = "-- real comment\nSELECT '-- fake comment'";
        var result = SqlFileParser.Parse(sql);
        result.Comment.Should().Contain("real comment");
        result.Comment.Should().NotContain("fake comment");
        result.Statements.Should().ContainSingle().Which.Should().Contain("-- fake comment");
    }
}
