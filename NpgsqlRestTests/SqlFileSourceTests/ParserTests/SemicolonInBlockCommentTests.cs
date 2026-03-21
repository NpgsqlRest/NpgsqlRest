using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class SemicolonInBlockCommentTests
{
    [Fact]
    public void SemicolonInsideBlockComment_NotASplitPoint()
    {
        var result = SqlFileParser.Parse("/* a;b */ SELECT 1");
        result.Statements.Should().ContainSingle();
    }

    [Fact]
    public void SemicolonInsideBlockComment_StatementNotAffected()
    {
        var result = SqlFileParser.Parse("/* drop;create */ SELECT 42");
        result.Statements.Should().ContainSingle().Which.Should().Contain("SELECT 42");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void SemicolonInLineComment_NotASplitPoint()
    {
        var sql = "-- a;b\nSELECT 1";
        var result = SqlFileParser.Parse(sql);
        result.Statements.Should().ContainSingle();
        result.Errors.Should().BeEmpty();
    }
}
