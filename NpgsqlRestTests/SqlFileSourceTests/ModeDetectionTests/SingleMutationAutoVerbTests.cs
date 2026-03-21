using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class SingleMutationAutoVerbTests
{
    [Fact]
    public void InsertStatement_AutoVerbIsPut()
    {
        var result = SqlFileParser.Parse("INSERT INTO t VALUES (1)");
        result.HasInsert.Should().BeTrue();
        result.AutoHttpMethod.Should().Be(Method.PUT);
    }

    [Fact]
    public void UpdateStatement_AutoVerbIsPost()
    {
        var result = SqlFileParser.Parse("UPDATE t SET x = 1");
        result.HasUpdate.Should().BeTrue();
        result.AutoHttpMethod.Should().Be(Method.POST);
    }

    [Fact]
    public void DeleteStatement_AutoVerbIsDelete()
    {
        var result = SqlFileParser.Parse("DELETE FROM t WHERE id = 1");
        result.HasDelete.Should().BeTrue();
        result.AutoHttpMethod.Should().Be(Method.DELETE);
    }

    [Fact]
    public void InsertCaseInsensitive_Detected()
    {
        var result = SqlFileParser.Parse("insert into t values (1)");
        result.HasInsert.Should().BeTrue();
    }
}
