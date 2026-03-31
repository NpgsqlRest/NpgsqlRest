using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class MultiCommandParserTests
{
    [Fact]
    public void PositionalResult_InHeader_NamesFirstCommand()
    {
        var sql = """
            -- @result validate
            select 1;
            select 2;
            """;
        var result = SqlFileParser.Parse(sql);
        result.Statements.Should().HaveCount(2);
        result.PositionalResultNames.Should().ContainKey(0).WhoseValue.Should().Be("validate");
        result.PositionalResultNames.Should().NotContainKey(1);
    }

    [Fact]
    public void PositionalResult_BetweenStatements_NamesNextCommand()
    {
        var sql = """
            select 1;
            -- @result verify
            select 2;
            """;
        var result = SqlFileParser.Parse(sql);
        result.PositionalResultNames.Should().NotContainKey(0);
        result.PositionalResultNames.Should().ContainKey(1).WhoseValue.Should().Be("verify");
    }

    [Fact]
    public void PositionalResult_IsForm_Works()
    {
        var sql = """
            -- @result is lookup
            select 1;
            -- @result is verify
            select 2;
            """;
        var result = SqlFileParser.Parse(sql);
        result.PositionalResultNames.Should().ContainKey(0).WhoseValue.Should().Be("lookup");
        result.PositionalResultNames.Should().ContainKey(1).WhoseValue.Should().Be("verify");
    }

    [Fact]
    public void NoAnnotations_EmptyPositionalResultNames()
    {
        var result = SqlFileParser.Parse("select 1; select 2");
        result.PositionalResultNames.Should().BeEmpty();
    }

    [Fact]
    public void SingleStatement_NoPositionalResultNames()
    {
        var result = SqlFileParser.Parse("select 1");
        result.PositionalResultNames.Should().BeEmpty();
    }

    [Fact]
    public void PositionalResult_WithoutAtPrefix_Works()
    {
        var sql = """
            -- result step1
            select 1;
            select 2;
            """;
        var result = SqlFileParser.Parse(sql);
        result.PositionalResultNames.Should().ContainKey(0).WhoseValue.Should().Be("step1");
    }

    [Fact]
    public void Skip_InHeader_MarksFirstCommand()
    {
        var sql = """
            -- @skip
            begin;
            select 1;
            """;
        var result = SqlFileParser.Parse(sql);
        result.SkipCommands.Should().Contain(0);
        result.SkipCommands.Should().NotContain(1);
    }

    [Fact]
    public void Skip_BetweenStatements_MarksNextCommand()
    {
        var sql = """
            select 1;
            -- @skip
            do $$ begin perform 1; end; $$;
            select 2;
            """;
        var result = SqlFileParser.Parse(sql);
        result.SkipCommands.Should().NotContain(0);
        result.SkipCommands.Should().Contain(1);
        result.SkipCommands.Should().NotContain(2);
    }

    [Fact]
    public void SkipResult_Alias_Works()
    {
        var sql = """
            -- @skip_result
            begin;
            select 1;
            """;
        var result = SqlFileParser.Parse(sql);
        result.SkipCommands.Should().Contain(0);
    }

    [Fact]
    public void NoResult_Alias_Works()
    {
        var sql = """
            -- @no_result
            begin;
            select 1;
            """;
        var result = SqlFileParser.Parse(sql);
        result.SkipCommands.Should().Contain(0);
    }
}
