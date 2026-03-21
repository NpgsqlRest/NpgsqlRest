using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class CommentOnlyFileTests
{
    [Fact]
    public void LineCommentsOnly_NoStatements()
    {
        var sql = """
            -- line one
            -- line two
            """;
        var result = SqlFileParser.Parse(sql);
        result.Statements.Should().BeEmpty();
    }

    [Fact]
    public void LineCommentsOnly_CommentsExtracted()
    {
        var sql = """
            -- line one
            -- line two
            """;
        var result = SqlFileParser.Parse(sql);
        result.Comment.Should().Contain("line one");
        result.Comment.Should().Contain("line two");
    }

    [Fact]
    public void BlockCommentOnly_NoStatements()
    {
        var result = SqlFileParser.Parse("/* just comments */");
        result.Statements.Should().BeEmpty();
    }

    [Fact]
    public void BlockCommentOnly_CommentExtracted()
    {
        var result = SqlFileParser.Parse("/* just comments */");
        result.Comment.Should().Contain("just comments");
    }
}
