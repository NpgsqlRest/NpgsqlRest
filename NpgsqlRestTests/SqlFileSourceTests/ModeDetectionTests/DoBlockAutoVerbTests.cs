using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class DoBlockAutoVerbTests
{
    [Fact]
    public void DoBlock_IsDoBlockTrue()
    {
        var result = SqlFileParser.Parse("DO $$ BEGIN NULL; END; $$");
        result.IsDoBlock.Should().BeTrue();
    }

    [Fact]
    public void DoBlock_AutoVerbIsPost()
    {
        var result = SqlFileParser.Parse("DO $$ BEGIN NULL; END; $$");
        result.AutoHttpMethod.Should().Be(Method.POST);
    }

    [Fact]
    public void DoBlockCaseInsensitive_Detected()
    {
        var result = SqlFileParser.Parse("do $$ BEGIN NULL; END; $$");
        result.IsDoBlock.Should().BeTrue();
    }

    [Fact]
    public void DoBlockWithTaggedDollarQuote_Detected()
    {
        var result = SqlFileParser.Parse("DO $body$ BEGIN NULL; END; $body$");
        result.IsDoBlock.Should().BeTrue();
        result.AutoHttpMethod.Should().Be(Method.POST);
    }
}
