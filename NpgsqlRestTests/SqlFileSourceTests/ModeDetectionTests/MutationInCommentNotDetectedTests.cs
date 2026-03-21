using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class MutationInCommentNotDetectedTests
{
    [Fact]
    public void InsertInLineComment_NotDetected()
    {
        var sql = "-- INSERT INTO t\nSELECT 1";
        var result = SqlFileParser.Parse(sql);
        result.HasInsert.Should().BeFalse();
    }

    [Fact]
    public void UpdateInBlockComment_NotDetected()
    {
        var result = SqlFileParser.Parse("/* UPDATE t SET x = 1 */ SELECT 1");
        result.HasUpdate.Should().BeFalse();
    }

    [Fact]
    public void DeleteInLineComment_NotDetected()
    {
        var sql = "-- DELETE FROM t\nSELECT 1";
        var result = SqlFileParser.Parse(sql);
        result.HasDelete.Should().BeFalse();
    }

    [Fact]
    public void MutationInComment_AutoVerbStaysGet()
    {
        var sql = "-- INSERT UPDATE DELETE\nSELECT 1";
        var result = SqlFileParser.Parse(sql);
        result.AutoHttpMethod.Should().Be(Method.GET);
    }
}
