namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ParamRenameSimpleFormTests()
    {
        script.Append(@"
create function case_param_rename_simple(int)
returns int
language sql
as 'select $1';

comment on function case_param_rename_simple(int) is '
HTTP GET
param $1 my_value
';

create function case_param_rename_simple_named(_p1 int)
returns int
language sql
as 'select _p1';

comment on function case_param_rename_simple_named(int) is '
HTTP GET
param _p1 my_named_value
';
");
    }
}

[Collection("TestFixture")]
public class ParamRenameSimpleFormTests(TestFixture test)
{
    [Fact]
    public async Task Test_Renamed_Positional_Param_Works_Via_QueryString()
    {
        using var result = await test.Client.GetAsync("/api/case-param-rename-simple/?my_value=42");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("42");
    }

    [Fact]
    public async Task Test_Renamed_Positional_Param_Old_Name_No_Longer_Works()
    {
        // $1 is no longer in ParamsHash, so it should not bind — expect non-OK or empty response
        using var result = await test.Client.GetAsync("/api/case-param-rename-simple/?$1=42");
        var response = await result.Content.ReadAsStringAsync();
        response.Should().NotBe("42");
    }

    [Fact]
    public async Task Test_Renamed_Named_Param_Works_Via_QueryString()
    {
        using var result = await test.Client.GetAsync("/api/case-param-rename-simple-named/?my_named_value=55");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("55");
    }
}
