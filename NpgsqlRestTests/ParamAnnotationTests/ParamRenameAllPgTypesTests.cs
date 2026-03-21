namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ParamRenameAllPgTypesTests()
    {
        // Test rename with type annotation on functions where the type matches the actual PG type
        // Retype is primarily useful for SQL file source; here we verify the annotation is parsed
        // and compatible types don't break execution
        script.Append(@"
create function case_param_retype_int(int)
returns int
language sql
as 'select $1';

comment on function case_param_retype_int(int) is '
HTTP GET
param $1 my_int integer
';

create function case_param_retype_text(text)
returns text
language sql
as 'select $1';

comment on function case_param_retype_text(text) is '
HTTP GET
param $1 my_text text
';

create function case_param_retype_bool(boolean)
returns boolean
language sql
as 'select $1';

comment on function case_param_retype_bool(boolean) is '
HTTP GET
param $1 my_bool boolean
';

create function case_param_retype_uuid(uuid)
returns uuid
language sql
as 'select $1';

comment on function case_param_retype_uuid(uuid) is '
HTTP GET
param $1 my_uuid uuid
';

create function case_param_retype_date(date)
returns date
language sql
as 'select $1';

comment on function case_param_retype_date(date) is '
HTTP GET
param $1 my_date date
';
");
    }
}

[Collection("TestFixture")]
public class ParamRenameAllPgTypesTests(TestFixture test)
{
    [Fact]
    public async Task Test_Retype_Integer()
    {
        using var result = await test.Client.GetAsync("/api/case-param-retype-int/?my_int=42");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("42");
    }

    [Fact]
    public async Task Test_Retype_Text()
    {
        using var result = await test.Client.GetAsync("/api/case-param-retype-text/?my_text=hello");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("hello");
    }

    [Fact]
    public async Task Test_Retype_Boolean()
    {
        using var result = await test.Client.GetAsync("/api/case-param-retype-bool/?my_bool=true");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("t");
    }

    [Fact]
    public async Task Test_Retype_Uuid()
    {
        using var result = await test.Client.GetAsync("/api/case-param-retype-uuid/?my_uuid=550e8400-e29b-41d4-a716-446655440000");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("550e8400-e29b-41d4-a716-446655440000");
    }

    [Fact]
    public async Task Test_Retype_Date()
    {
        using var result = await test.Client.GetAsync("/api/case-param-retype-date/?my_date=2024-01-15");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("2024-01-15");
    }
}
