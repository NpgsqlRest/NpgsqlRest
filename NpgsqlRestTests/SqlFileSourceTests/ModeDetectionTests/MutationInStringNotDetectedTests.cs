using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class MutationInStringNotDetectedTests
{
    [Fact]
    public void InsertInSingleQuotedString_NotDetected()
    {
        var result = SqlFileParser.Parse("SELECT 'INSERT INTO t'");
        result.HasInsert.Should().BeFalse();
    }

    [Fact]
    public void UpdateInSingleQuotedString_NotDetected()
    {
        var result = SqlFileParser.Parse("SELECT 'UPDATE t SET x = 1'");
        result.HasUpdate.Should().BeFalse();
    }

    [Fact]
    public void DeleteInSingleQuotedString_NotDetected()
    {
        var result = SqlFileParser.Parse("SELECT 'DELETE FROM t'");
        result.HasDelete.Should().BeFalse();
    }

    [Fact]
    public void MutationInString_AutoVerbStaysGet()
    {
        var result = SqlFileParser.Parse("SELECT 'INSERT UPDATE DELETE'");
        result.AutoHttpMethod.Should().Be(Method.GET);
    }
}
