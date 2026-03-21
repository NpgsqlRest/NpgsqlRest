using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class QueryModeDetectionTests
{
    [Fact]
    public void SimpleSelect_NoMutationFlags()
    {
        var result = SqlFileParser.Parse("SELECT 1");
        result.HasInsert.Should().BeFalse();
        result.HasUpdate.Should().BeFalse();
        result.HasDelete.Should().BeFalse();
    }

    [Fact]
    public void SimpleSelect_AutoVerbIsGet()
    {
        var result = SqlFileParser.Parse("SELECT * FROM users WHERE id = 1");
        result.AutoHttpMethod.Should().Be(Method.GET);
    }

    [Fact]
    public void SelectWithSubquery_StillGet()
    {
        var result = SqlFileParser.Parse("SELECT * FROM (SELECT 1 AS x) sub");
        result.AutoHttpMethod.Should().Be(Method.GET);
    }

    [Fact]
    public void SelectWithJoin_StillGet()
    {
        var result = SqlFileParser.Parse("SELECT a.id, b.name FROM a JOIN b ON a.id = b.id");
        result.AutoHttpMethod.Should().Be(Method.GET);
        result.IsDoBlock.Should().BeFalse();
    }
}
