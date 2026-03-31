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
            SELECT 1; -- @result cmd1
            SELECT 2; -- @result cmd2
            SELECT 3; -- @result cmd3
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
            SELECT 1; -- @result cmd1
            SELECT 2; -- @result cmd2
            SELECT 3; -- @result cmd3
            """;
        var result = SqlFileParser.Parse(sql);
        result.Comment.Should().Contain("@result cmd1");
        result.Comment.Should().Contain("@result cmd2");
        result.Comment.Should().Contain("@result cmd3");
    }

    [Fact]
    public void InlineCommentsAfterSemicolon_PositionalResultNamesResolved()
    {
        // Comments on same line after ; apply to the SAME statement (the one just completed)
        var sql = """
            SELECT 1; -- @result first
            SELECT 2; -- @result second
            SELECT 3;
            """;
        var result = SqlFileParser.Parse(sql);
        // "first" is on same line as SELECT 1's ;, so it applies to command index 0
        result.PositionalResultNames.Should().ContainKey(0).WhoseValue.Should().Be("first");
        // "second" is on same line as SELECT 2's ;, so it applies to command index 1
        result.PositionalResultNames.Should().ContainKey(1).WhoseValue.Should().Be("second");
        // SELECT 3 has no annotation
        result.PositionalResultNames.Should().NotContainKey(2);
    }
}
