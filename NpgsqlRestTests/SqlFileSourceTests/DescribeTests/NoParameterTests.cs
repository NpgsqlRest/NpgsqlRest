using NpgsqlRest.SqlFileSource;
using NpgsqlRestTests;

namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("TestFixture")]
public class NoParameterTests
{
    [Fact]
    public void SelectLiteral_ReturnsZeroParams()
    {
        using var conn = Database.CreateConnection();
        conn.Open();

        var sql = "select 1";
        var paramCount = SqlFileDescriber.FindMaxParamIndex(sql);
        var result = SqlFileDescriber.Describe(conn, sql, paramCount);

        result.HasError.Should().BeFalse();
        paramCount.Should().Be(0);
        result.ParameterTypes.Should().NotBeNull();
        result.ParameterTypes!.Length.Should().Be(0);
        result.Columns.Should().NotBeNull();
        result.Columns!.Length.Should().Be(1);
    }

    [Fact]
    public void SelectNow_ReturnsZeroParamsOneColumn()
    {
        using var conn = Database.CreateConnection();
        conn.Open();

        var sql = "select now()";
        var paramCount = SqlFileDescriber.FindMaxParamIndex(sql);
        var result = SqlFileDescriber.Describe(conn, sql, paramCount);

        result.HasError.Should().BeFalse();
        paramCount.Should().Be(0);
        result.Columns.Should().NotBeNull();
        result.Columns!.Length.Should().Be(1);
    }

    [Fact]
    public void SelectExpression_ReturnsCorrectResult()
    {
        using var conn = Database.CreateConnection();
        conn.Open();

        var sql = "select 2 + 3 as result, 'hello' as greeting";
        var paramCount = SqlFileDescriber.FindMaxParamIndex(sql);
        var result = SqlFileDescriber.Describe(conn, sql, paramCount);

        result.HasError.Should().BeFalse();
        paramCount.Should().Be(0);
        result.Columns.Should().NotBeNull();
        result.Columns!.Length.Should().Be(2);
        result.Columns[0].Name.Should().Be("result");
        result.Columns[1].Name.Should().Be("greeting");
    }
}
