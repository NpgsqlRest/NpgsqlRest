using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class DollarQuoteSimpleTests
{
    [Fact]
    public void SimpleDollarQuote_PreservedInStatement()
    {
        var result = SqlFileParser.Parse("SELECT $$hello world$$");
        result.Statements.Should().ContainSingle().Which.Should().Contain("$$hello world$$");
    }

    [Fact]
    public void DollarQuote_NotTreatedAsComment()
    {
        var result = SqlFileParser.Parse("SELECT $$content inside$$");
        result.Comment.Should().BeEmpty();
    }

    [Fact]
    public void EmptyDollarQuote_PreservedInStatement()
    {
        var result = SqlFileParser.Parse("SELECT $$$$");
        result.Statements.Should().ContainSingle().Which.Should().Contain("$$$$");
    }
}
