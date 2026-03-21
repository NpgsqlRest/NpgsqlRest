namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ParamRenameCoexistHashTests()
    {
        // Existing "is hash of" form must still work alongside the new rename forms
        script.Append(@"
create function case_param_coexist_hash(_password text, _raw_password text)
returns text
language sql
as 'select _password';

comment on function case_param_coexist_hash(text, text) is '
HTTP
param _password is hash of _raw_password
';
");
    }
}

[Collection("TestFixture")]
public class ParamRenameCoexistHashTests(TestFixture test)
{
    [Fact]
    public async Task Test_Hash_Of_Still_Works()
    {
        using var content = new StringContent(
            "{\"password\": \"test\", \"rawPassword\": \"test\"}",
            Encoding.UTF8,
            "application/json");
        using var result = await test.Client.PostAsync("/api/case-param-coexist-hash/", content);
        // We just need to verify it doesn't fail — hash computation happens server-side
        result?.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
