using Npgsql;
using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("TestFixture")]
public class MultiCommandDescribeTests
{
    [Fact]
    public async Task BatchReader_NextResult_IteratesAllCommands()
    {
        using var conn = Database.CreateConnection();
        await conn.OpenAsync();

        await using var batch = new NpgsqlBatch(conn);
        batch.BatchCommands.Add(new NpgsqlBatchCommand("select 1 as a"));
        batch.BatchCommands.Add(new NpgsqlBatchCommand("select 2 as b")); // non-query no-op
        batch.BatchCommands.Add(new NpgsqlBatchCommand("select 3 as c"));

        await using var reader = await batch.ExecuteReaderAsync();

        // First result
        reader.FieldCount.Should().Be(1);
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetInt32(0).Should().Be(1);

        // NextResult → second
        (await reader.NextResultAsync()).Should().BeTrue();
        reader.FieldCount.Should().Be(1);
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetInt32(0).Should().Be(2);

        // NextResult → third
        (await reader.NextResultAsync()).Should().BeTrue();
        reader.FieldCount.Should().Be(1);
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetInt32(0).Should().Be(3);

        // No more
        (await reader.NextResultAsync()).Should().BeFalse();
    }

    [Fact]
    public void ThreeStatements_EachDescribedCorrectly()
    {
        using var conn = Database.CreateConnection();
        conn.Open();

        var stmts = new[]
        {
            "select name from sql_describe_test where id = $1",
            "insert into sql_describe_test (id, name) values ($1 + 1000, 'multi_test')",
            "select count(*) as total from sql_describe_test"
        };

        foreach (var stmt in stmts)
        {
            var paramCount = SqlFileDescriber.FindMaxParamIndex(stmt);
            var result = SqlFileDescriber.Describe(conn, stmt, paramCount);
            result.HasError.Should().BeFalse($"Describe failed for '{stmt}': {result.Error}");
        }

        // Third statement should have 1 column
        var third = SqlFileDescriber.Describe(conn, stmts[2], 0);
        third.Columns!.Length.Should().Be(1);
        third.Columns[0].Name.Should().Be("total");
    }
}
