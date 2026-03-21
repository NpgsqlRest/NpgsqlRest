using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class HeaderCommentScopeTests
{
    [Fact]
    public void HeaderScope_CommentsBeforeFirstStatement_Collected()
    {
        var sql = """
            -- header comment
            SELECT 1
            """;
        var result = SqlFileParser.Parse(sql, CommentScope.Header);
        result.Comment.Should().Contain("header comment");
    }

    [Fact]
    public void HeaderScope_CommentsAfterFirstStatement_Ignored()
    {
        var sql = """
            SELECT 1;
            -- trailing comment
            SELECT 2
            """;
        var result = SqlFileParser.Parse(sql, CommentScope.Header);
        result.Comment.Should().NotContain("trailing comment");
    }

    [Fact]
    public void HeaderScope_OnlyHeaderCollected_TrailingIgnored()
    {
        var sql = """
            -- header
            SELECT 1;
            -- after
            """;
        var result = SqlFileParser.Parse(sql, CommentScope.Header);
        result.Comment.Should().Contain("header");
        result.Comment.Should().NotContain("after");
    }

    [Fact]
    public void HeaderScope_BlockCommentBeforeStatement_Collected()
    {
        var sql = """
            /* header block */
            SELECT 1
            """;
        var result = SqlFileParser.Parse(sql, CommentScope.Header);
        result.Comment.Should().Contain("header block");
    }

    [Fact]
    public void AllScope_CommentsAfterFirstStatement_Collected()
    {
        var sql = """
            SELECT 1;
            -- trailing comment
            SELECT 2
            """;
        var result = SqlFileParser.Parse(sql, CommentScope.All);
        result.Comment.Should().Contain("trailing comment");
    }
}
