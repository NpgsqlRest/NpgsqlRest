using NpgsqlRest.SqlFileSource;
using NpgsqlRestTests;

namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("TestFixture")]
public class NullableParameterTests
{
    [Fact]
    public void Select_WithCoalesce_ParamStillDetected()
    {
        using var conn = Database.CreateConnection();
        conn.Open();

        var sql = "select * from sql_describe_test where name = coalesce($1, 'default')";
        var paramCount = SqlFileDescriber.FindMaxParamIndex(sql);
        var result = SqlFileDescriber.Describe(conn, sql, paramCount);

        result.HasError.Should().BeFalse();
        paramCount.Should().Be(1);
        result.ParameterTypes.Should().NotBeNull();
        result.ParameterTypes!.Length.Should().Be(1);
    }

    [Fact]
    public void Select_WithMultipleCoalesceParams_CorrectCount()
    {
        using var conn = Database.CreateConnection();
        conn.Open();

        var sql = "select * from sql_describe_test where name = coalesce($1, 'default') and active = coalesce($2, true)";
        var paramCount = SqlFileDescriber.FindMaxParamIndex(sql);
        var result = SqlFileDescriber.Describe(conn, sql, paramCount);

        result.HasError.Should().BeFalse();
        paramCount.Should().Be(2);
        result.ParameterTypes.Should().NotBeNull();
        result.ParameterTypes!.Length.Should().Be(2);
    }

    [Fact]
    public void Select_WithNullIfParam_ParamDetected()
    {
        using var conn = Database.CreateConnection();
        conn.Open();

        var sql = "select nullif($1, '') as val";
        var paramCount = SqlFileDescriber.FindMaxParamIndex(sql);
        var result = SqlFileDescriber.Describe(conn, sql, paramCount);

        result.HasError.Should().BeFalse();
        paramCount.Should().Be(1);
        result.ParameterTypes.Should().NotBeNull();
        result.ParameterTypes!.Length.Should().Be(1);
    }
}
