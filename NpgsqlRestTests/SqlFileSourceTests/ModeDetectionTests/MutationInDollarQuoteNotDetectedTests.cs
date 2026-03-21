using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class MutationInDollarQuoteNotDetectedTests
{
    [Fact]
    public void InsertInDollarQuote_NotDetected()
    {
        var result = SqlFileParser.Parse("SELECT $$INSERT INTO t$$");
        result.HasInsert.Should().BeFalse();
    }

    [Fact]
    public void UpdateInDollarQuote_NotDetected()
    {
        var result = SqlFileParser.Parse("SELECT $$UPDATE t SET x = 1$$");
        result.HasUpdate.Should().BeFalse();
    }

    [Fact]
    public void DeleteInTaggedDollarQuote_NotDetected()
    {
        var result = SqlFileParser.Parse("SELECT $fn$DELETE FROM t$fn$");
        result.HasDelete.Should().BeFalse();
    }

    [Fact]
    public void MutationInDollarQuote_AutoVerbStaysGet()
    {
        var result = SqlFileParser.Parse("SELECT $$INSERT UPDATE DELETE$$");
        result.AutoHttpMethod.Should().Be(Method.GET);
    }
}
