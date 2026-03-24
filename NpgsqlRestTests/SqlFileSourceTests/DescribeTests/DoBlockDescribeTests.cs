using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("TestFixture")]
public class DoBlockDescribeTests
{
    [Fact]
    public void DoBlock_FindMaxParamIndex_ReturnsZero()
    {
        var sql = "DO $$ BEGIN RAISE NOTICE 'hello'; END; $$";
        var paramCount = SqlFileDescriber.FindMaxParamIndex(sql);

        paramCount.Should().Be(0);
    }

    [Fact]
    public void DoBlock_Describe_ReturnsZeroColumns()
    {
        using var conn = Database.CreateConnection();
        conn.Open();

        var sql = "DO $$ BEGIN PERFORM 1; END; $$";
        var paramCount = SqlFileDescriber.FindMaxParamIndex(sql);
        var result = SqlFileDescriber.Describe(conn, sql, paramCount);

        result.HasError.Should().BeFalse();
        paramCount.Should().Be(0);
        result.Columns.Should().NotBeNull();
        result.Columns!.Length.Should().Be(0);
    }

    [Fact]
    public void DoBlock_WithInsert_ReturnsZeroColumns()
    {
        using var conn = Database.CreateConnection();
        conn.Open();

        var sql = @"DO $$ BEGIN INSERT INTO sql_describe_test (id, name) VALUES (999, 'do_test') ON CONFLICT DO NOTHING; END; $$";
        var paramCount = SqlFileDescriber.FindMaxParamIndex(sql);
        var result = SqlFileDescriber.Describe(conn, sql, paramCount);

        result.HasError.Should().BeFalse();
        paramCount.Should().Be(0);
        result.Columns.Should().NotBeNull();
        result.Columns!.Length.Should().Be(0);
    }
}
