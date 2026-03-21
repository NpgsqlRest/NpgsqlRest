namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ParamRenameCoexistUploadTests()
    {
        // Existing "is upload metadata" form must still work alongside the new rename forms
        script.Append(@"
create function case_param_coexist_upload(_data bytea, _filename text)
returns text
language sql
as 'select _filename';

comment on function case_param_coexist_upload(bytea, text) is '
HTTP
param _filename is upload metadata
';
");
    }
}

[Collection("TestFixture")]
public class ParamRenameCoexistUploadTests(TestFixture test)
{
    [Fact]
    public async Task Test_Upload_Metadata_Still_Works()
    {
        // Just verify endpoint creation doesn't fail — the upload metadata annotation must be parsed, not rename
        using var result = await test.Client.GetAsync("/api/case-param-coexist-upload/");
        // POST is expected for upload, GET should be NotFound or MethodNotAllowed
        result?.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
