using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class MultiStatementValidTests
{
    [Fact]
    public void TwoStatements_NoErrors()
    {
        var result = SqlFileParser.Parse("SELECT 1; SELECT 2");
        result.Errors.Should().BeEmpty();
        result.Statements.Should().HaveCount(2);
    }

    [Fact]
    public void ThreeStatements_NoErrors()
    {
        var result = SqlFileParser.Parse("SELECT 1; SELECT 2; SELECT 3");
        result.Errors.Should().BeEmpty();
        result.Statements.Should().HaveCount(3);
    }

    [Fact]
    public void SingleStatement_NoErrors()
    {
        var result = SqlFileParser.Parse("SELECT 1");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void SingleStatementWithSemicolon_NoErrors()
    {
        var result = SqlFileParser.Parse("SELECT 1;");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void MultiStatement_IsNotSingleCommand()
    {
        var result = SqlFileParser.Parse("SELECT 1; SELECT 2");
        result.Statements.Count.Should().BeGreaterThan(1);
    }
}
