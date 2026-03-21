using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class StatementSplittingTrailingSemiTests
{
    [Fact]
    public void TrailingSemicolon_NoEmptyStatement()
    {
        var result = SqlFileParser.Parse("SELECT 1;");
        result.Statements.Should().ContainSingle();
    }

    [Fact]
    public void TrailingSemicolonWithWhitespace_NoEmptyStatement()
    {
        var result = SqlFileParser.Parse("SELECT 1;   \n  ");
        result.Statements.Should().ContainSingle();
    }

    [Fact]
    public void MultipleSemicolonsAtEnd_NoExtraStatements()
    {
        // "SELECT 1;" produces one statement, then ";;" is two empty splits
        var result = SqlFileParser.Parse("SELECT 1;;;");
        result.Statements.Should().ContainSingle();
    }
}
