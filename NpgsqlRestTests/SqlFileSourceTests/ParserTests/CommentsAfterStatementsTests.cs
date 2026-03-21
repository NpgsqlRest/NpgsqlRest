using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class CommentsAfterStatementsTests
{
    [Fact]
    public void LineCommentAfterLastStatement_CollectedWithScopeAll()
    {
        var sql = """
            SELECT 1;
            -- trailing comment
            """;
        var result = SqlFileParser.Parse(sql, CommentScope.All);
        result.Comment.Should().Contain("trailing comment");
    }

    [Fact]
    public void BlockCommentAfterLastStatement_CollectedWithScopeAll()
    {
        var sql = "SELECT 1; /* trailing block */";
        var result = SqlFileParser.Parse(sql, CommentScope.All);
        result.Comment.Should().Contain("trailing block");
    }

    [Fact]
    public void InlineCommentOnLastLine_Collected()
    {
        var result = SqlFileParser.Parse("SELECT 1 -- end of line");
        // No semicolon, so "SELECT 1" is the statement, "-- end of line" is the comment
        result.Comment.Should().Contain("end of line");
        result.Statements.Should().ContainSingle();
    }
}
