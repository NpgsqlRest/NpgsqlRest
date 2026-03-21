using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class CommentInDollarQuoteTests
{
    [Fact]
    public void DashDashInsideDollarQuote_NotExtractedAsComment()
    {
        var result = SqlFileParser.Parse("SELECT $$-- not a comment$$");
        result.Comment.Should().BeEmpty();
        result.Statements.Should().ContainSingle().Which.Should().Contain("-- not a comment");
    }

    [Fact]
    public void BlockCommentInsideDollarQuote_NotExtractedAsComment()
    {
        var result = SqlFileParser.Parse("SELECT $$/* not a comment */$$");
        result.Comment.Should().BeEmpty();
        result.Statements.Should().ContainSingle().Which.Should().Contain("/* not a comment */");
    }

    [Fact]
    public void TaggedDollarQuoteWithComment_NotExtracted()
    {
        var result = SqlFileParser.Parse("SELECT $fn$-- not a comment$fn$");
        result.Comment.Should().BeEmpty();
        result.Statements.Should().ContainSingle().Which.Should().Contain("-- not a comment");
    }
}
