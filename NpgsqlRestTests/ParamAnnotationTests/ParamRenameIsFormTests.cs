namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ParamRenameIsFormTests()
    {
        script.Append(@"
create function case_param_rename_is_form(int)
returns int
language sql
as 'select $1';

comment on function case_param_rename_is_form(int) is '
HTTP GET
param $1 is my_is_value
';
");
    }
}

[Collection("TestFixture")]
public class ParamRenameIsFormTests(TestFixture test)
{
    [Fact]
    public async Task Test_Renamed_With_Is_Form_Works()
    {
        using var result = await test.Client.GetAsync("/api/case-param-rename-is-form/?my_is_value=77");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("77");
    }
}
