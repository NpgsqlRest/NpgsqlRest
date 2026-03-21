using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class BlockCommentExtractionTests
{
    [Fact]
    public void SimpleBlockComment_ExtractedIntoComment()
    {
        var result = SqlFileParser.Parse("/* block comment */ SELECT 1");
        result.Comment.Should().Contain("block comment");
        result.Statements.Should().ContainSingle();
    }

    [Fact]
    public void MultiLineBlockComment_ExtractedIntoComment()
    {
        var sql = """
            /*
             * line one
             * line two
             */
            SELECT 1
            """;
        var result = SqlFileParser.Parse(sql);
        result.Comment.Should().Contain("line one");
        result.Comment.Should().Contain("line two");
    }

    [Fact]
    public void BlockCommentWithStars_FormattingPreserved()
    {
        var sql = "/* * star formatted * */ SELECT 1";
        var result = SqlFileParser.Parse(sql);
        result.Comment.Should().Contain("star formatted");
    }

    [Fact]
    public void BlockCommentOnly_NoStatements()
    {
        var result = SqlFileParser.Parse("/* just a comment */");
        result.Statements.Should().BeEmpty();
        result.Comment.Should().Contain("just a comment");
    }
}
