namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ParamRenameOnFunctionTests()
    {
        // Rename works on a regular named-parameter function, using JSON body (POST)
        script.Append(@"
create function case_param_rename_on_func(_p1 int, _p2 text)
returns json
language plpgsql
as
$$
begin
    return json_build_object('num', _p1, 'str', _p2);
end;
$$;

comment on function case_param_rename_on_func(int, text) is '
HTTP
param _p1 my_number
param _p2 my_string
';
");
    }
}

[Collection("TestFixture")]
public class ParamRenameOnFunctionTests(TestFixture test)
{
    [Fact]
    public async Task Test_Renamed_Params_Work_Via_Json_Body()
    {
        using var content = new StringContent(
            "{\"my_number\": 7, \"my_string\": \"test\"}",
            Encoding.UTF8,
            "application/json");
        using var result = await test.Client.PostAsync("/api/case-param-rename-on-func/", content);
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Contain("\"num\" : 7");
        response.Should().Contain("\"str\" : \"test\"");
    }

    [Fact]
    public async Task Test_Old_Param_Names_No_Longer_Work_Via_Json_Body()
    {
        // Old converted names (p1, p2) should no longer bind
        using var content = new StringContent(
            "{\"p1\": 7, \"p2\": \"test\"}",
            Encoding.UTF8,
            "application/json");
        using var result = await test.Client.PostAsync("/api/case-param-rename-on-func/", content);
        var response = await result.Content.ReadAsStringAsync();
        // The old names don't match ParamsHash, so endpoint won't bind — expect non-OK or null values
        response.Should().NotContain("\"num\" : 7");
    }
}
