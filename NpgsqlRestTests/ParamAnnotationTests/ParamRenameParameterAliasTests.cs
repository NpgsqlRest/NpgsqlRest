namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ParamRenameParameterAliasTests()
    {
        // @parameter (long form) should work the same as @param
        script.Append(@"
create function case_parameter_alias(int)
returns int
language sql
as 'select $1';

comment on function case_parameter_alias(int) is '
HTTP GET
parameter $1 alias_value
';
");
    }
}

[Collection("TestFixture")]
public class ParamRenameParameterAliasTests(TestFixture test)
{
    [Fact]
    public async Task Test_Parameter_Long_Form_Works()
    {
        using var result = await test.Client.GetAsync("/api/case-parameter-alias/?alias_value=66");
        var response = await result.Content.ReadAsStringAsync();

        result?.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("66");
    }
}
