using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class StringEscapeTests
{
    [Fact]
    public void EscapedSingleQuote_DoesNotBreakParsing()
    {
        var result = SqlFileParser.Parse("SELECT 'it''s alive'");
        result.Statements.Should().ContainSingle().Which.Should().Contain("it''s alive");
    }

    [Fact]
    public void EscapedQuoteFollowedBySemicolon_SplitsCorrectly()
    {
        var result = SqlFileParser.Parse("SELECT 'it''s'; SELECT 2");
        result.Statements.Should().HaveCount(2);
        result.Statements[0].Should().Contain("it''s");
    }

    [Fact]
    public void MultipleEscapedQuotes_AllPreserved()
    {
        var result = SqlFileParser.Parse("SELECT 'a''b''c'");
        result.Statements.Should().ContainSingle().Which.Should().Contain("a''b''c");
    }

    [Fact]
    public void EmptyString_Preserved()
    {
        var result = SqlFileParser.Parse("SELECT ''");
        result.Statements.Should().ContainSingle().Which.Should().Contain("''");
    }
}
