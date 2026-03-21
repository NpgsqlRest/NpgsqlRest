using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class StatementSplittingSingleTests
{
    [Fact]
    public void SingleStatementWithSemicolon_ProducesOneStatement()
    {
        var result = SqlFileParser.Parse("SELECT 1;");
        result.Statements.Should().ContainSingle().Which.Should().Be("SELECT 1");
    }

    [Fact]
    public void SingleStatementWithoutSemicolon_ProducesOneStatement()
    {
        var result = SqlFileParser.Parse("SELECT 1");
        result.Statements.Should().ContainSingle().Which.Should().Be("SELECT 1");
    }

    [Fact]
    public void SingleStatementWithLeadingWhitespace_Trimmed()
    {
        var result = SqlFileParser.Parse("   SELECT 1   ");
        result.Statements.Should().ContainSingle().Which.Should().Be("SELECT 1");
    }

    [Fact]
    public void SingleStatement_NoErrors()
    {
        var result = SqlFileParser.Parse("SELECT 1");
        result.Errors.Should().BeEmpty();
    }
}
