namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ParamRenameOnCustomTypeTests()
    {
        // Test rename on functions with custom composite type parameters
        script.Append(@"
create type custom_rename_type as (val1 text, val2 int);

create function case_param_rename_custom(_p custom_rename_type)
returns text
language sql
as 'select _p.val1';

comment on function case_param_rename_custom(custom_rename_type) is '
HTTP
param _p my_custom_param
';

create function case_param_rename_custom_mixed(_id int, _data custom_rename_type)
returns json
language plpgsql
as
$$
begin
    return json_build_object('id', _id, 'val', _data.val1);
end;
$$;

comment on function case_param_rename_custom_mixed(int, custom_rename_type) is '
HTTP
param _id my_id
param _data my_data
';
");
    }
}

[Collection("TestFixture")]
public class ParamRenameOnCustomTypeTests(TestFixture test)
{
    [Fact]
    public async Task Test_Renamed_Custom_Type_Param_Works()
    {
        using var content = new StringContent(
            "{\"my_custom_param\": {\"val1\": \"hello\", \"val2\": 42}}",
            Encoding.UTF8,
            "application/json");
        using var result = await test.Client.PostAsync("/api/case-param-rename-custom/", content);
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Contain("hello");
    }

    [Fact]
    public async Task Test_Renamed_Mixed_Custom_Type_Params_Work()
    {
        using var content = new StringContent(
            "{\"my_id\": 99, \"my_data\": {\"val1\": \"world\", \"val2\": 7}}",
            Encoding.UTF8,
            "application/json");
        using var result = await test.Client.PostAsync("/api/case-param-rename-custom-mixed/", content);
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        // The renamed params bind correctly — id is 99 and data contains "world"
        response.Should().Contain("\"id\" : 99");
        response.Should().Contain("world");
    }
}
