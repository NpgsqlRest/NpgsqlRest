namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ParamRenameNonExistentParamTests()
    {
        // Renaming a param that doesn't exist should log a warning but not crash
        script.Append(@"
create function case_param_rename_nonexistent(int)
returns int
language sql
as 'select $1';

comment on function case_param_rename_nonexistent(int) is '
HTTP GET
param $99 nonexistent
';
");
    }
}

[Collection("TestFixture")]
public class ParamRenameNonExistentParamTests(TestFixture test)
{
    [Fact]
    public async Task Test_Nonexistent_Param_Rename_Does_Not_Crash()
    {
        // The endpoint should still be created — the annotation is silently ignored with a warning
        using var result = await test.Client.GetAsync("/api/case-param-rename-nonexistent/?$1=42");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("42");
    }
}
