using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("TestFixture")]
public class MutationDescribeTests
{
    [Fact]
    public void Insert_WithParams_InfersParameterTypes()
    {
        using var conn = Database.CreateConnection();
        conn.Open();

        var sql = "insert into sql_describe_test (id, name) values ($1, $2)";
        var paramCount = SqlFileDescriber.FindMaxParamIndex(sql);
        var result = SqlFileDescriber.Describe(conn, sql, paramCount);

        result.HasError.Should().BeFalse();
        paramCount.Should().Be(2);
        result.ParameterTypes.Should().NotBeNull();
        result.ParameterTypes!.Length.Should().Be(2);
    }

    [Fact]
    public void Update_WithWhereParam_InfersParameterTypes()
    {
        using var conn = Database.CreateConnection();
        conn.Open();

        var sql = "update sql_describe_test set name = $1 where id = $2";
        var paramCount = SqlFileDescriber.FindMaxParamIndex(sql);
        var result = SqlFileDescriber.Describe(conn, sql, paramCount);

        result.HasError.Should().BeFalse();
        paramCount.Should().Be(2);
        result.ParameterTypes.Should().NotBeNull();
        result.ParameterTypes!.Length.Should().Be(2);
    }

    [Fact]
    public void Delete_WithWhereParam_InfersParameterType()
    {
        using var conn = Database.CreateConnection();
        conn.Open();

        var sql = "delete from sql_describe_test where id = $1";
        var paramCount = SqlFileDescriber.FindMaxParamIndex(sql);
        var result = SqlFileDescriber.Describe(conn, sql, paramCount);

        result.HasError.Should().BeFalse();
        paramCount.Should().Be(1);
        result.ParameterTypes.Should().NotBeNull();
        result.ParameterTypes!.Length.Should().Be(1);
    }
}
