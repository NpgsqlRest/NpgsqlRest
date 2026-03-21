using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class LineCommentExtractionTests
{
    [Fact]
    public void SingleLineComment_ExtractedIntoComment()
    {
        var result = SqlFileParser.Parse("-- this is a comment");
        result.Comment.Should().Be(" this is a comment");
    }

    [Fact]
    public void MultipleLineComments_ConcatenatedWithNewlines()
    {
        var sql = """
            -- first line
            -- second line
            SELECT 1
            """;
        var result = SqlFileParser.Parse(sql);
        result.Comment.Should().Contain("first line");
        result.Comment.Should().Contain("second line");
    }

    [Fact]
    public void InlineCommentAfterSql_ExtractedIntoComment()
    {
        var result = SqlFileParser.Parse("SELECT 1; -- inline");
        result.Comment.Should().Contain("inline");
        result.Statements.Should().HaveCount(1);
        result.Statements[0].Should().Be("SELECT 1");
    }

    [Fact]
    public void CommentAtFileStart_Extracted()
    {
        var sql = """
            -- header comment
            SELECT 42
            """;
        var result = SqlFileParser.Parse(sql);
        result.Comment.Should().Contain("header comment");
        result.Statements.Should().ContainSingle().Which.Should().Contain("SELECT 42");
    }

    [Fact]
    public void CommentBetweenStatements_ExtractedWithScopeAll()
    {
        var sql = """
            SELECT 1;
            -- middle comment
            SELECT 2
            """;
        var result = SqlFileParser.Parse(sql, CommentScope.All);
        result.Comment.Should().Contain("middle comment");
    }
}
