using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class RejectMultiStatementTests
{
    [Fact]
    public void TwoStatements_ErrorsNonEmpty()
    {
        var result = SqlFileParser.Parse("SELECT 1; SELECT 2");
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void ThreeStatements_ErrorsNonEmpty()
    {
        var result = SqlFileParser.Parse("SELECT 1; SELECT 2; SELECT 3");
        result.Errors.Should().NotBeEmpty();
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
    public void MultiStatementError_ContainsDescriptiveMessage()
    {
        var result = SqlFileParser.Parse("SELECT 1; SELECT 2");
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("Multi-statement");
    }
}
