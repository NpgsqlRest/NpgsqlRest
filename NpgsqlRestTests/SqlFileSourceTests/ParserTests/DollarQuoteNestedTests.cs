using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class DollarQuoteNestedTests
{
    [Fact]
    public void InnerDollarQuoteInsideOuter_PreservedCorrectly()
    {
        // $outer$ ... $inner$...$inner$ ... $outer$
        var sql = "SELECT $outer$before $inner$inside$inner$ after$outer$";
        var result = SqlFileParser.Parse(sql);
        result.Statements.Should().ContainSingle();
        result.Statements[0].Should().Contain("$inner$inside$inner$");
    }

    [Fact]
    public void InnerAnonymousDollarQuoteInsideTagged_PreservedCorrectly()
    {
        // $tag$ ... $$ ... $$ ... $tag$ — the $$ inside is just content
        var sql = "SELECT $tag$before $$inner$$ after$tag$";
        var result = SqlFileParser.Parse(sql);
        result.Statements.Should().ContainSingle();
    }

    [Fact]
    public void NestedDollarQuote_NoErrors()
    {
        var sql = "SELECT $outer$a $inner$b$inner$ c$outer$";
        var result = SqlFileParser.Parse(sql);
        result.Errors.Should().BeEmpty();
    }
}
