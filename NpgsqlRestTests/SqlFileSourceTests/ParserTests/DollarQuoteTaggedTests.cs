using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class DollarQuoteTaggedTests
{
    [Fact]
    public void TaggedDollarQuote_PreservedInStatement()
    {
        var result = SqlFileParser.Parse("SELECT $tag$hello$tag$");
        result.Statements.Should().ContainSingle().Which.Should().Contain("$tag$hello$tag$");
    }

    [Fact]
    public void TaggedDollarQuote_NotTreatedAsComment()
    {
        var result = SqlFileParser.Parse("SELECT $body$some content$body$");
        result.Comment.Should().BeEmpty();
    }

    [Fact]
    public void DifferentTags_EachClosedByMatchingTag()
    {
        var sql = "SELECT $a$content a$a$, $b$content b$b$";
        var result = SqlFileParser.Parse(sql);
        result.Statements.Should().ContainSingle().Which.Should().Contain("$a$content a$a$");
        result.Statements[0].Should().Contain("$b$content b$b$");
    }
}
