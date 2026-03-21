using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class CommentsBetweenStatementsTests
{
    [Fact]
    public void LineCommentBetweenStatements_CollectedWithScopeAll()
    {
        var sql = """
            SELECT 1;
            -- annotation between
            SELECT 2
            """;
        var result = SqlFileParser.Parse(sql, CommentScope.All);
        result.Comment.Should().Contain("annotation between");
    }

    [Fact]
    public void BlockCommentBetweenStatements_CollectedWithScopeAll()
    {
        var sql = """
            SELECT 1;
            /* block between */
            SELECT 2
            """;
        var result = SqlFileParser.Parse(sql, CommentScope.All);
        result.Comment.Should().Contain("block between");
    }

    [Fact]
    public void MultipleCommentsBetweenStatements_AllCollected()
    {
        var sql = """
            SELECT 1;
            -- first
            -- second
            SELECT 2
            """;
        var result = SqlFileParser.Parse(sql, CommentScope.All);
        result.Comment.Should().Contain("first");
        result.Comment.Should().Contain("second");
    }
}
