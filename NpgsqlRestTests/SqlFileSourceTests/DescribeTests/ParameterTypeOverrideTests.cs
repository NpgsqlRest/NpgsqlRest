using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class ParameterTypeOverrideTests
{
    [Fact]
    public void FindMaxParamIndex_SingleParam_ReturnsOne()
    {
        var sql = "select * from t where id = $1";
        SqlFileDescriber.FindMaxParamIndex(sql).Should().Be(1);
    }

    [Fact]
    public void FindMaxParamIndex_SkippedParam_ReturnsMax()
    {
        var sql = "select * from t where id = $1 and name = $3";
        SqlFileDescriber.FindMaxParamIndex(sql).Should().Be(3);
    }

    [Fact]
    public void FindMaxParamIndex_NoParams_ReturnsZero()
    {
        var sql = "select 1";
        SqlFileDescriber.FindMaxParamIndex(sql).Should().Be(0);
    }

    [Fact]
    public void FindMaxParamIndex_ParamInsideStringLiteral_StillCounted()
    {
        // The regex is simple and does not parse string boundaries,
        // so $1 inside a string literal is still detected.
        var sql = "select '$1' as literal";
        SqlFileDescriber.FindMaxParamIndex(sql).Should().Be(1);
    }
}
