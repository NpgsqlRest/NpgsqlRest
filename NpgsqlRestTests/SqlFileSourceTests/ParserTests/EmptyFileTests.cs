using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class EmptyFileTests
{
    [Fact]
    public void EmptyString_NoStatements()
    {
        var result = SqlFileParser.Parse("");
        result.Statements.Should().BeEmpty();
    }

    [Fact]
    public void EmptyString_EmptyComment()
    {
        var result = SqlFileParser.Parse("");
        result.Comment.Should().BeEmpty();
    }

    [Fact]
    public void EmptyString_NoErrors()
    {
        var result = SqlFileParser.Parse("");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void EmptyString_NoMutations()
    {
        var result = SqlFileParser.Parse("");
        result.HasInsert.Should().BeFalse();
        result.HasUpdate.Should().BeFalse();
        result.HasDelete.Should().BeFalse();
        result.IsDoBlock.Should().BeFalse();
    }
}
