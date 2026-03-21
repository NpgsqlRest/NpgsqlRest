using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class SemicolonInStringTests
{
    [Fact]
    public void SemicolonInsideSingleQuotedString_NotASplitPoint()
    {
        var result = SqlFileParser.Parse("SELECT 'hello;world'");
        result.Statements.Should().ContainSingle().Which.Should().Contain("hello;world");
    }

    [Fact]
    public void SemicolonInsideString_SingleStatement_NoErrors()
    {
        var result = SqlFileParser.Parse("SELECT 'a;b;c'");
        result.Statements.Should().ContainSingle();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void SemicolonInsideAndOutsideString_OnlySplitsOnOutside()
    {
        var result = SqlFileParser.Parse("SELECT 'a;b'; SELECT 2");
        result.Statements.Should().HaveCount(2);
        result.Statements[0].Should().Contain("a;b");
    }
}
