using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class StatementSplittingMultipleTests
{
    [Fact]
    public void TwoStatements_ProducesTwoStatementsAndError()
    {
        var result = SqlFileParser.Parse("SELECT 1; SELECT 2");
        result.Statements.Should().HaveCount(2);
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void TwoStatements_BothPreserved()
    {
        var result = SqlFileParser.Parse("SELECT 1; SELECT 2");
        result.Statements[0].Should().Be("SELECT 1");
        result.Statements[1].Should().Be("SELECT 2");
    }

    [Fact]
    public void ThreeStatements_AllSplit()
    {
        var result = SqlFileParser.Parse("SELECT 1; SELECT 2; SELECT 3");
        result.Statements.Should().HaveCount(3);
        result.Errors.Should().NotBeEmpty();
    }
}
