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
    public void Parse_SkipInHeader_MarksCommandZero()
    {
        var result = SqlFileParser.Parse("""
            -- @skip
            begin;
            select 1;
            """.AsSpan());

        result.Statements.Count.Should().Be(2);
        result.SkipCommands.Should().Contain(0);
        result.SkipCommands.Should().NotContain(1);
    }

    [Fact]
    public void Parse_SkipBetweenStatements_MarksCorrectCommand()
    {
        var result = SqlFileParser.Parse("""
            select 1;
            -- @skip
            begin;
            select 2;
            """.AsSpan());

        result.Statements.Count.Should().Be(3);
        result.SkipCommands.Should().NotContain(0);
        result.SkipCommands.Should().Contain(1);
        result.SkipCommands.Should().NotContain(2);
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

    [Fact]
    public void Parse_InlineResult_AppliesToSameStatement()
    {
        // Comment on same line after ; applies to the just-completed statement
        var result = SqlFileParser.Parse("""
            select 1; -- @result first
            select 2; -- @result second
            select 3;
            """.AsSpan());

        result.Statements.Count.Should().Be(3);
        result.PositionalResultNames.Should().ContainKey(0).WhoseValue.Should().Be("first");
        result.PositionalResultNames.Should().ContainKey(1).WhoseValue.Should().Be("second");
        result.PositionalResultNames.Should().NotContainKey(2);
    }

    [Fact]
    public void Parse_InlineSingle_AppliesToSameStatement()
    {
        var result = SqlFileParser.Parse("""
            select 1; -- @single
            select 2;
            """.AsSpan());

        result.Statements.Count.Should().Be(2);
        result.SingleCommands.Should().Contain(0);
        result.SingleCommands.Should().NotContain(1);
    }

    [Fact]
    public void Parse_InlineSkip_AppliesToSameStatement()
    {
        var result = SqlFileParser.Parse("""
            begin; -- @skip
            select 1;
            """.AsSpan());

        result.Statements.Count.Should().Be(2);
        result.SkipCommands.Should().Contain(0);
        result.SkipCommands.Should().NotContain(1);
    }

    [Fact]
    public void Parse_MixedInlineAndPositional_CorrectAssociation()
    {
        // Inline applies to same statement, between-lines applies to next
        var result = SqlFileParser.Parse("""
            select 1; -- @result first
            -- @result second
            select 2;
            """.AsSpan());

        result.Statements.Count.Should().Be(2);
        result.PositionalResultNames.Should().ContainKey(0).WhoseValue.Should().Be("first");
        result.PositionalResultNames.Should().ContainKey(1).WhoseValue.Should().Be("second");
    }

    [Fact]
    public void Parse_InlineResultOnLastStatement()
    {
        var result = SqlFileParser.Parse("""
            select 1;
            select 2; -- @result last
            """.AsSpan());

        result.Statements.Count.Should().Be(2);
        result.PositionalResultNames.Should().NotContainKey(0);
        result.PositionalResultNames.Should().ContainKey(1).WhoseValue.Should().Be("last");
    }

    [Fact]
    public void Parse_BlockCommentInlineSameLine_AppliesToSameStatement()
    {
        var result = SqlFileParser.Parse("""
            select 1; /* @result first */
            select 2;
            """.AsSpan());

        result.Statements.Count.Should().Be(2);
        result.PositionalResultNames.Should().ContainKey(0).WhoseValue.Should().Be("first");
        result.PositionalResultNames.Should().NotContainKey(1);
    }

    [Fact]
    public void Parse_BlockCommentBetweenStatements_AppliesToNext()
    {
        var result = SqlFileParser.Parse("""
            select 1;
            /* @result second */
            select 2;
            """.AsSpan());

        result.Statements.Count.Should().Be(2);
        result.PositionalResultNames.Should().NotContainKey(0);
        result.PositionalResultNames.Should().ContainKey(1).WhoseValue.Should().Be("second");
    }

    [Fact]
    public void Parse_SkipAndResultConflict_BothApply()
    {
        // @skip and @result on same statement — skip takes priority in rendering,
        // but parser records both (conflict resolution is at rendering level)
        var result = SqlFileParser.Parse("""
            -- @skip
            -- @result skipped_name
            begin;
            select 1;
            """.AsSpan());

        result.Statements.Count.Should().Be(2);
        result.SkipCommands.Should().Contain(0);
        result.PositionalResultNames.Should().ContainKey(0).WhoseValue.Should().Be("skipped_name");
    }

    [Fact]
    public void Parse_ResultWithNoName_Ignored()
    {
        var result = SqlFileParser.Parse("""
            -- @result
            select 1;
            select 2;
            """.AsSpan());

        result.Statements.Count.Should().Be(2);
        result.PositionalResultNames.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ResultAndSingleOnSameStatement()
    {
        var result = SqlFileParser.Parse("""
            -- @result user
            -- @single
            select id, name from users;
            select 1;
            """.AsSpan());

        result.Statements.Count.Should().Be(2);
        result.PositionalResultNames.Should().ContainKey(0).WhoseValue.Should().Be("user");
        result.SingleCommands.Should().Contain(0);
    }

    [Fact]
    public void Parse_InlineBlockCommentSkip()
    {
        var result = SqlFileParser.Parse("""
            begin; /* @skip */
            select 1;
            """.AsSpan());

        result.Statements.Count.Should().Be(2);
        result.SkipCommands.Should().Contain(0);
        result.SkipCommands.Should().NotContain(1);
    }
}
