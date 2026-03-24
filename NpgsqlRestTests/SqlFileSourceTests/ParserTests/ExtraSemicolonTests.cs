using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class ExtraSemicolonTests
{
    [Fact]
    public void DoubleSemicolon_NoEmptyStatements()
    {
        var result = SqlFileParser.Parse("SELECT 1;; SELECT 2");
        result.Statements.Should().HaveCount(2);
        result.Statements[0].Should().Be("SELECT 1");
        result.Statements[1].Should().Be("SELECT 2");
    }

    [Fact]
    public void TripleSemicolon_NoEmptyStatements()
    {
        var result = SqlFileParser.Parse("SELECT 1;;; SELECT 2");
        result.Statements.Should().HaveCount(2);
    }

    [Fact]
    public void SemicolonWithBlankLines_NoEmptyStatements()
    {
        var sql = """
            SELECT 1;

            SELECT 2;;

            SELECT 3
            """;
        var result = SqlFileParser.Parse(sql);
        result.Statements.Should().HaveCount(3);
        result.Statements[0].Should().Be("SELECT 1");
        result.Statements[1].Should().Be("SELECT 2");
        result.Statements[2].Should().Be("SELECT 3");
    }

    [Fact]
    public void OnlySemicolons_NoStatements()
    {
        var result = SqlFileParser.Parse(";;;");
        result.Statements.Should().BeEmpty();
    }

    [Fact]
    public void SemicolonBeforeFirstStatement_Ignored()
    {
        var result = SqlFileParser.Parse("; SELECT 1");
        result.Statements.Should().HaveCount(1);
        result.Statements[0].Should().Be("SELECT 1");
    }

    [Fact]
    public void CommandThenDoubleSemicolonThenCommand()
    {
        var result = SqlFileParser.Parse("SELECT 1;; SELECT 2;; SELECT 3");
        result.Statements.Should().HaveCount(3);
    }

    [Fact]
    public void InlineCommentsAfterSemicolon_StatementsClean()
    {
        var sql = """
            SELECT 1; -- @result1 cmd1
            SELECT 2; -- @result2 cmd2
            SELECT 3; -- @result3 cmd3
            """;
        var result = SqlFileParser.Parse(sql);
        result.Statements.Should().HaveCount(3);
        result.Statements[0].Should().Be("SELECT 1");
        result.Statements[1].Should().Be("SELECT 2");
        result.Statements[2].Should().Be("SELECT 3");
    }

    [Fact]
    public void InlineCommentsAfterSemicolon_AnnotationsExtracted()
    {
        var sql = """
            SELECT 1; -- @result1 cmd1
            SELECT 2; -- @result2 cmd2
            SELECT 3; -- @result3 cmd3
            """;
        var result = SqlFileParser.Parse(sql);
        result.Comment.Should().Contain("@result1 cmd1");
        result.Comment.Should().Contain("@result2 cmd2");
        result.Comment.Should().Contain("@result3 cmd3");
    }

    [Fact]
    public void InlineCommentsAfterSemicolon_ResultNamesResolved()
    {
        var sql = """
            SELECT 1; -- @result1 cmd1
            SELECT 2; -- @result2 cmd2
            SELECT 3; -- @result3 cmd3
            """;
        var result = SqlFileParser.Parse(sql);
        result.ResultNames.Should().ContainKey(1);
        result.ResultNames[1].Should().Be("cmd1");
        result.ResultNames[2].Should().Be("cmd2");
        result.ResultNames[3].Should().Be("cmd3");
    }
}
