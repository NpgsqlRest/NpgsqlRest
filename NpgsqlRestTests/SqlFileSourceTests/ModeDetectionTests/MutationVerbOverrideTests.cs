using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class MutationVerbOverrideTests
{
    [Fact]
    public void NoMutations_AutoVerbIsGet()
    {
        var result = new SqlFileParseResult();
        result.AutoHttpMethod.Should().Be(Method.GET);
    }

    [Fact]
    public void OnlyInsert_AutoVerbIsPut()
    {
        var result = new SqlFileParseResult { HasInsert = true };
        result.AutoHttpMethod.Should().Be(Method.PUT);
    }

    [Fact]
    public void OnlyUpdate_AutoVerbIsPost()
    {
        var result = new SqlFileParseResult { HasUpdate = true };
        result.AutoHttpMethod.Should().Be(Method.POST);
    }

    [Fact]
    public void OnlyDelete_AutoVerbIsDelete()
    {
        var result = new SqlFileParseResult { HasDelete = true };
        result.AutoHttpMethod.Should().Be(Method.DELETE);
    }

    [Fact]
    public void IsDoBlock_AutoVerbIsPost()
    {
        var result = new SqlFileParseResult { IsDoBlock = true };
        result.AutoHttpMethod.Should().Be(Method.POST);
    }

    [Fact]
    public void DoBlockOverridesDelete_AutoVerbIsPost()
    {
        var result = new SqlFileParseResult { IsDoBlock = true, HasDelete = true };
        result.AutoHttpMethod.Should().Be(Method.POST);
    }
}
