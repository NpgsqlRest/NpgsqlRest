using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class NestedBlockCommentTests
{
    [Fact]
    public void NestedBlockComment_OuterAndInnerExtracted()
    {
        var sql = "/* outer /* inner */ outer end */ SELECT 1";
        var result = SqlFileParser.Parse(sql);
        result.Comment.Should().Contain("outer");
        result.Comment.Should().Contain("inner");
        result.Statements.Should().ContainSingle().Which.Should().Contain("SELECT 1");
    }

    [Fact]
    public void NestedBlockComment_DoesNotPrematurelyClose()
    {
        // The first */ closes the inner, the second */ closes the outer.
        // "SELECT 1" should be a statement, not part of the comment.
        var sql = "/* outer /* inner */ still comment */ SELECT 1";
        var result = SqlFileParser.Parse(sql);
        result.Statements.Should().ContainSingle().Which.Should().Contain("SELECT 1");
    }

    [Fact]
    public void DeeplyNestedBlockComment_HandledCorrectly()
    {
        var sql = "/* a /* b /* c */ b */ a */ SELECT 1";
        var result = SqlFileParser.Parse(sql);
        result.Statements.Should().ContainSingle().Which.Should().Contain("SELECT 1");
    }
}
