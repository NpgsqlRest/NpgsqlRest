namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ParamRenameIsFormWithTypeTests()
    {
        script.Append(@"
create function case_param_rename_is_type(int)
returns int
language sql
as 'select $1';

comment on function case_param_rename_is_type(int) is '
HTTP GET
param $1 is my_typed_value integer
';
");
    }
}

[Collection("TestFixture")]
public class ParamRenameIsFormWithTypeTests(TestFixture test)
{
    [Fact]
    public async Task Test_Renamed_And_Retyped_With_Is_Form_Works()
    {
        using var result = await test.Client.GetAsync("/api/case-param-rename-is-type/?my_typed_value=88");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("88");
    }
}
