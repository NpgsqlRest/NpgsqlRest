namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ParamRenameNamedParamIsFormTests()
    {
        script.Append(@"
create function case_param_rename_named_is(_x int)
returns int
language sql
as 'select _x';

comment on function case_param_rename_named_is(int) is '
HTTP GET
param _x is renamed_x
';
");
    }
}

[Collection("TestFixture")]
public class ParamRenameNamedParamIsFormTests(TestFixture test)
{
    [Fact]
    public async Task Test_Renamed_Named_Param_Is_Form_Works()
    {
        using var result = await test.Client.GetAsync("/api/case-param-rename-named-is/?renamed_x=44");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("44");
    }
}
