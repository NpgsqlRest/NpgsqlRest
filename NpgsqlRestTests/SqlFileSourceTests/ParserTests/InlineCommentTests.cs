using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class InlineCommentTests
{
    [Fact]
    public void InlineCommentAfterSemicolon_CommentExtractedStatementClean()
    {
        var result = SqlFileParser.Parse("SELECT 1; -- inline comment");
        result.Comment.Should().Contain("inline comment");
        result.Statements.Should().ContainSingle().Which.Should().Be("SELECT 1");
    }

    [Fact]
    public void InlineCommentBeforeSemicolon_CommentExtractedStatementClean()
    {
        // Comment comes before the semicolon-less end
        var result = SqlFileParser.Parse("SELECT 1 -- inline comment");
        result.Comment.Should().Contain("inline comment");
        result.Statements.Should().ContainSingle().Which.Should().Contain("SELECT 1");
    }

    [Fact]
    public void MultipleInlineComments_AllExtracted()
    {
        var sql = """
            SELECT 1; -- first
            SELECT 2 -- second
            """;
        var result = SqlFileParser.Parse(sql, CommentScope.All);
        result.Comment.Should().Contain("first");
        result.Comment.Should().Contain("second");
    }
}
