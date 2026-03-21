namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ParamRenameNamedParamTests()
    {
        script.Append(@"
create function case_param_rename_named_param(_old_name int)
returns int
language sql
as 'select _old_name';

comment on function case_param_rename_named_param(int) is '
HTTP GET
param _old_name better_name
';
");
    }
}

[Collection("TestFixture")]
public class ParamRenameNamedParamTests(TestFixture test)
{
    [Fact]
    public async Task Test_Renamed_Named_Param_Works()
    {
        using var result = await test.Client.GetAsync("/api/case-param-rename-named-param/?better_name=33");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("33");
    }

    [Fact]
    public async Task Test_Renamed_Named_Param_Old_Name_No_Longer_Works()
    {
        // The old converted name (oldName via NameConverter) should no longer bind
        using var result = await test.Client.GetAsync("/api/case-param-rename-named-param/?oldName=33");
        var response = await result.Content.ReadAsStringAsync();
        response.Should().NotBe("33");
    }
}
