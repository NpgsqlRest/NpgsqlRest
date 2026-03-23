using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class MultiCommandParserTests
{
    [Fact]
    public void CommandNameAnnotation_ExtractedInOrder()
    {
        var sql = """
            -- @command_name step1
            SELECT 1;
            -- @command_name step2
            SELECT 2;
            SELECT 3;
            """;
        var result = SqlFileParser.Parse(sql);
        result.Statements.Should().HaveCount(3);
        result.CommandNames.Should().HaveCount(3);
        result.CommandNames[0].Should().Be("step1");
        result.CommandNames[1].Should().Be("step2");
        result.CommandNames[2].Should().BeNull(); // no annotation for 3rd
    }

    [Fact]
    public void NoAnnotations_AllCommandNamesNull()
    {
        var result = SqlFileParser.Parse("SELECT 1; SELECT 2");
        result.CommandNames.Should().HaveCount(2);
        result.CommandNames[0].Should().BeNull();
        result.CommandNames[1].Should().BeNull();
    }

    [Fact]
    public void SingleStatement_NoCommandNames()
    {
        var result = SqlFileParser.Parse("SELECT 1");
        result.CommandNames.Should().BeEmpty(); // not populated for single statements
    }
}
