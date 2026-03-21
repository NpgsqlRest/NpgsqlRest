namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ParamRenameWithTypeSimpleFormTests()
    {
        script.Append(@"
create function case_param_rename_with_type(int)
returns int
language sql
as 'select $1';

comment on function case_param_rename_with_type(int) is '
HTTP GET
param $1 my_int_value integer
';
");
    }
}

[Collection("TestFixture")]
public class ParamRenameWithTypeSimpleFormTests(TestFixture test)
{
    [Fact]
    public async Task Test_Renamed_And_Retyped_Param_Works()
    {
        using var result = await test.Client.GetAsync("/api/case-param-rename-with-type/?my_int_value=123");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("123");
    }
}
