namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ParamRenameMultipleParamsTests()
    {
        script.Append(@"
create function case_param_rename_multiple(int, text)
returns json
language plpgsql
as
$$
begin
    return json_build_object('a', $1, 'b', $2);
end;
$$;

comment on function case_param_rename_multiple(int, text) is '
HTTP GET
param $1 first_param
param $2 second_param
';
");
    }
}

[Collection("TestFixture")]
public class ParamRenameMultipleParamsTests(TestFixture test)
{
    [Fact]
    public async Task Test_Multiple_Renamed_Params_Work()
    {
        using var result = await test.Client.GetAsync("/api/case-param-rename-multiple/?first_param=10&second_param=hello");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Contain("\"a\" : 10");
        response.Should().Contain("\"b\" : \"hello\"");
    }
}
