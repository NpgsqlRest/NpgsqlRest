using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class MultiCommandParserTests
{
    [Fact]
    public void ResultAnnotation_SimpleForm_Extracted()
    {
        var sql = """
            -- @result1 validate
            -- @result2 process
            select 1;
            select 2;
            select 3;
            """;
        var result = SqlFileParser.Parse(sql);
        result.Statements.Should().HaveCount(3);
        result.ResultNames.Should().ContainKey(1);
        result.ResultNames[1].Should().Be("validate");
        result.ResultNames[2].Should().Be("process");
        result.ResultNames.Should().NotContainKey(3);
    }

    [Fact]
    public void ResultAnnotation_IsForm_Extracted()
    {
        var sql = """
            -- @result1 is lookup
            -- @result3 is verify
            select 1;
            update foo set x = 1;
            select 2;
            """;
        var result = SqlFileParser.Parse(sql);
        result.ResultNames[1].Should().Be("lookup");
        result.ResultNames.Should().NotContainKey(2);
        result.ResultNames[3].Should().Be("verify");
    }

    [Fact]
    public void NoAnnotations_EmptyResultNames()
    {
        var result = SqlFileParser.Parse("select 1; select 2");
        result.ResultNames.Should().BeEmpty();
    }

    [Fact]
    public void SingleStatement_NoResultNames()
    {
        var result = SqlFileParser.Parse("select 1");
        result.ResultNames.Should().BeEmpty();
    }

    [Fact]
    public void ResultAnnotation_WithAtPrefix_Works()
    {
        var sql = """
            -- @result1 step1
            select 1;
            select 2;
            """;
        var result = SqlFileParser.Parse(sql);
        result.ResultNames[1].Should().Be("step1");
    }

    [Fact]
    public void ResultAnnotation_WithoutAtPrefix_Works()
    {
        var sql = """
            -- result1 step1
            select 1;
            select 2;
            """;
        var result = SqlFileParser.Parse(sql);
        result.ResultNames[1].Should().Be("step1");
    }
}
