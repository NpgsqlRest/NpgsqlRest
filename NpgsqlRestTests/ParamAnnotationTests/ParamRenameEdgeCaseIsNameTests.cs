namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ParamRenameEdgeCaseIsNameTests()
    {
        // Edge case: rename $1 to the literal name "is"
        script.Append(@"
create function case_param_rename_to_is(int)
returns int
language sql
as 'select $1';

comment on function case_param_rename_to_is(int) is '
HTTP GET
param $1 is
';
");
    }
}

[Collection("TestFixture")]
public class ParamRenameEdgeCaseIsNameTests(TestFixture test)
{
    [Fact]
    public async Task Test_Param_Renamed_To_Literal_Is_Works()
    {
        // "is" is no longer reserved — renaming to "is" works
        using var result = await test.Client.GetAsync("/api/case-param-rename-to-is/?is=99");
        var response = await result!.Content.ReadAsStringAsync();

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("99");
    }
}
