using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class PerCommandAnnotationParserTests
{
    [Fact]
    public void Parse_SingleInHeader_MarksCommandZero()
    {
        var result = SqlFileParser.Parse("""
            -- HTTP GET
            -- @single
            select 1;
            select 2;
            """.AsSpan());

        result.Statements.Count.Should().Be(2);
        result.SingleCommands.Should().Contain(0);
        result.SingleCommands.Should().NotContain(1);
    }

    [Fact]
    public void Parse_SingleBetweenStatements_MarksCorrectCommand()
    {
        var result = SqlFileParser.Parse("""
            select 1;
            -- @single
            select 2;
            """.AsSpan());

        result.Statements.Count.Should().Be(2);
        result.SingleCommands.Should().NotContain(0);
        result.SingleCommands.Should().Contain(1);
    }

    [Fact]
    public void Parse_SingleOnBothCommands()
    {
        var result = SqlFileParser.Parse("""
            -- @single
            select 1;
            -- @single
            select 2;
            """.AsSpan());

        result.Statements.Count.Should().Be(2);
        result.SingleCommands.Should().Contain(0);
        result.SingleCommands.Should().Contain(1);
    }

    [Fact]
    public void Parse_PositionalResult_MarksCorrectCommand()
    {
        var result = SqlFileParser.Parse("""
            -- @result lookup
            select 1;
            -- @result details
            select 2;
            """.AsSpan());

        result.Statements.Count.Should().Be(2);
        result.PositionalResultNames.Should().ContainKey(0).WhoseValue.Should().Be("lookup");
        result.PositionalResultNames.Should().ContainKey(1).WhoseValue.Should().Be("details");
    }

    [Fact]
    public void Parse_PositionalResultIsSyntax()
    {
        var result = SqlFileParser.Parse("""
            -- @result is details
            select 1;
            select 2;
            """.AsSpan());

        result.Statements.Count.Should().Be(2);
        result.PositionalResultNames.Should().ContainKey(0).WhoseValue.Should().Be("details");
        result.PositionalResultNames.Should().NotContainKey(1);
    }

    [Fact]
    public void Parse_NumberedResultNotTreatedAsPositional()
    {
        var result = SqlFileParser.Parse("""
            select 1;
            -- @result2 named
            select 2;
            """.AsSpan());

        result.Statements.Count.Should().Be(2);
        // @result2 is numbered, not positional
        result.PositionalResultNames.Should().BeEmpty();
        result.ResultNames.Should().ContainKey(2).WhoseValue.Should().Be("named");
    }

    [Fact]
    public void Parse_ExactMultiSingleFirstContent()
    {
        // Exact content from multi_single_first.sql
        var result = SqlFileParser.Parse("""
            -- HTTP GET
            -- @param $1 id
            -- @single
            select id, name from sql_describe_test where id = $1;
            select id, name from sql_describe_test where id = $1;
            """.AsSpan());

        result.Statements.Count.Should().Be(2);
        result.SingleCommands.Should().Contain(0, "First command should be marked as single");
        result.SingleCommands.Should().NotContain(1, "Second command should NOT be marked as single");
    }

    [Fact]
    public void Parse_NoSingleOnSingleCommand_EmptySingleCommands()
    {
        var result = SqlFileParser.Parse("""
            select 1;
            """.AsSpan());

        result.Statements.Count.Should().Be(1);
        result.SingleCommands.Should().BeEmpty();
    }
}
