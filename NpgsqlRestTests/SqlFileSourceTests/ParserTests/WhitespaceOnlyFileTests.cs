using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class WhitespaceOnlyFileTests
{
    [Fact]
    public void WhitespaceOnly_NoStatements()
    {
        var result = SqlFileParser.Parse("   \n  \t  \n  ");
        result.Statements.Should().BeEmpty();
    }

    [Fact]
    public void NewlinesOnly_NoStatements()
    {
        var result = SqlFileParser.Parse("\n\n\n");
        result.Statements.Should().BeEmpty();
    }

    [Fact]
    public void TabsAndSpaces_EmptyComment()
    {
        var result = SqlFileParser.Parse("   \t   ");
        result.Comment.Should().BeEmpty();
    }

    [Fact]
    public void WhitespaceOnly_NoErrors()
    {
        var result = SqlFileParser.Parse("   \n  ");
        result.Errors.Should().BeEmpty();
    }
}
