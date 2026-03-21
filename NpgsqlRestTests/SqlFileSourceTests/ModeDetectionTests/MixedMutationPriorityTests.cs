using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class MixedMutationPriorityTests
{
    [Fact]
    public void DeleteAndInsert_AutoVerbIsDelete()
    {
        var result = SqlFileParser.Parse("DELETE FROM t WHERE id = 1; INSERT INTO t VALUES (1)");
        result.HasDelete.Should().BeTrue();
        result.HasInsert.Should().BeTrue();
        result.AutoHttpMethod.Should().Be(Method.DELETE);
    }

    [Fact]
    public void UpdateAndInsert_AutoVerbIsPost()
    {
        var result = SqlFileParser.Parse("UPDATE t SET x = 1; INSERT INTO t VALUES (1)");
        result.HasUpdate.Should().BeTrue();
        result.HasInsert.Should().BeTrue();
        result.AutoHttpMethod.Should().Be(Method.POST);
    }

    [Fact]
    public void AllThreeMutations_AutoVerbIsDelete()
    {
        var result = SqlFileParser.Parse("INSERT INTO t VALUES (1); UPDATE t SET x = 1; DELETE FROM t");
        result.HasInsert.Should().BeTrue();
        result.HasUpdate.Should().BeTrue();
        result.HasDelete.Should().BeTrue();
        result.AutoHttpMethod.Should().Be(Method.DELETE);
    }
}
