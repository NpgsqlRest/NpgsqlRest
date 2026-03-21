namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ParamRenameNamedParamWithTypeTests()
    {
        script.Append(@"
create function case_param_rename_named_type(_old text)
returns text
language sql
as 'select _old';

comment on function case_param_rename_named_type(text) is '
HTTP GET
param _old new_name text
';
");
    }
}

[Collection("TestFixture")]
public class ParamRenameNamedParamWithTypeTests(TestFixture test)
{
    [Fact]
    public async Task Test_Renamed_Named_Param_With_Type_Works()
    {
        using var result = await test.Client.GetAsync("/api/case-param-rename-named-type/?new_name=hello");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("hello");
    }
}
