using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class SemicolonInDollarQuoteTests
{
    [Fact]
    public void SemicolonInsideDollarQuote_NotASplitPoint()
    {
        var result = SqlFileParser.Parse("SELECT $$hello;world$$");
        result.Statements.Should().ContainSingle().Which.Should().Contain("hello;world");
    }

    [Fact]
    public void SemicolonInsideTaggedDollarQuote_NotASplitPoint()
    {
        var result = SqlFileParser.Parse("SELECT $fn$a;b;c$fn$");
        result.Statements.Should().ContainSingle().Which.Should().Contain("a;b;c");
    }

    [Fact]
    public void SemicolonInsideDollarQuote_NoErrors()
    {
        var result = SqlFileParser.Parse("SELECT $$a;b$$");
        result.Errors.Should().BeEmpty();
    }
}
