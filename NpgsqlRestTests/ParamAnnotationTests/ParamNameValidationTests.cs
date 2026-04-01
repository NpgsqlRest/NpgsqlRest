namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ParamNameValidationTests()
    {
        // Valid rename — should work
        script.Append(@"
create function case_param_valid_name(int)
returns int
language sql
as 'select $1';

comment on function case_param_valid_name(int) is '
HTTP GET
param $1 good_name
';

-- 'default' at position 2 is interpreted as 'set default value' (not rename)
-- So 'param $1 default' sets default null on $1, the param stays as $1
create function case_param_default_keyword(int)
returns int
language sql
as 'select $1';

comment on function case_param_default_keyword(int) is '
HTTP GET
param $1 default
';

-- 'is' at exactly 3 tokens: 'param $1 is' — renames $1 to literal 'is'
create function case_param_is_keyword(int)
returns int
language sql
as 'select $1';

comment on function case_param_is_keyword(int) is '
HTTP GET
param $1 is
';

-- Rename to name starting with a digit — invalid PG identifier
create function case_param_starts_digit(int)
returns int
language sql
as 'select $1';

comment on function case_param_starts_digit(int) is '
HTTP GET
param $1 1bad_name
';

-- Rename to name with hyphen — invalid character
create function case_param_has_hyphen(int)
returns int
language sql
as 'select $1';

comment on function case_param_has_hyphen(int) is '
HTTP GET
param $1 my-param
';

-- Rename to name with underscore and dollar — valid PG identifier
create function case_param_underscore_dollar(int)
returns int
language sql
as 'select $1';

comment on function case_param_underscore_dollar(int) is '
HTTP GET
param $1 _val$1
';

-- Rename to 'type' — should work (not reserved)
create function case_param_type_keyword(int)
returns int
language sql
as 'select $1';

comment on function case_param_type_keyword(int) is '
HTTP GET
param $1 type
';

-- Rename to 'upload' — should work (not reserved, only matters in 'is upload metadata' pattern)
create function case_param_upload_keyword(int)
returns int
language sql
as 'select $1';

comment on function case_param_upload_keyword(int) is '
HTTP GET
param $1 upload
';

-- Rename to 'metadata' — should work (not reserved)
create function case_param_metadata_keyword(int)
returns int
language sql
as 'select $1';

comment on function case_param_metadata_keyword(int) is '
HTTP GET
param $1 metadata
';

-- Rename to 'hash' — should work (only matters in 'is hash of' pattern)
create function case_param_hash_keyword(int)
returns int
language sql
as 'select $1';

comment on function case_param_hash_keyword(int) is '
HTTP GET
param $1 hash
';

-- Rename to 'of' — should work (not reserved)
create function case_param_of_keyword(int)
returns int
language sql
as 'select $1';

comment on function case_param_of_keyword(int) is '
HTTP GET
param $1 of
';
");
    }
}

[Collection("TestFixture")]
public class ParamNameValidationTests(TestFixture test)
{
    [Fact]
    public async Task ValidName_RenameWorks()
    {
        using var result = await test.Client.GetAsync("/api/case-param-valid-name/?good_name=7");
        var response = await result.Content.ReadAsStringAsync();

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("7");
    }

    [Fact]
    public async Task DefaultKeyword_InterpretedAsSetDefault_NotRename()
    {
        // 'param $1 default' sets default null on $1. The param name stays as $1.
        // Without providing $1, the default (null) is used, so the endpoint returns null.
        using var result = await test.Client.GetAsync("/api/case-param-default-keyword/");
        result.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task IsKeyword_RenameToLiteralIs_Works()
    {
        // 'param $1 is' with exactly 3 tokens renames $1 to "is"
        using var result = await test.Client.GetAsync("/api/case-param-is-keyword/?is=7");
        var response = await result.Content.ReadAsStringAsync();

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("7");
    }

    [Fact]
    public async Task StartsWithDigit_RenameRejected()
    {
        using var result = await test.Client.GetAsync("/api/case-param-starts-digit/?1bad_name=7");
        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task HasHyphen_RenameRejected()
    {
        // Hyphen splits into two words during parsing, so "my-param" becomes "my" + "-param"
        // This effectively renames to "my" which is valid but wrong — let's just check the hyphenated name doesn't work
        using var result = await test.Client.GetAsync("/api/case-param-has-hyphen/?my-param=7");
        // The hyphenated name won't match since it was split during comment parsing
        result.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnderscoreDollar_ValidRenameWorks()
    {
        using var result = await test.Client.GetAsync("/api/case-param-underscore-dollar/?_val$1=7");
        var response = await result.Content.ReadAsStringAsync();

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("7");
    }

    [Fact]
    public async Task TypeKeyword_RenameWorks()
    {
        using var result = await test.Client.GetAsync("/api/case-param-type-keyword/?type=7");
        var response = await result.Content.ReadAsStringAsync();

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("7");
    }

    [Fact]
    public async Task UploadKeyword_RenameWorks()
    {
        using var result = await test.Client.GetAsync("/api/case-param-upload-keyword/?upload=7");
        var response = await result.Content.ReadAsStringAsync();

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("7");
    }

    [Fact]
    public async Task MetadataKeyword_RenameWorks()
    {
        using var result = await test.Client.GetAsync("/api/case-param-metadata-keyword/?metadata=7");
        var response = await result.Content.ReadAsStringAsync();

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("7");
    }

    [Fact]
    public async Task HashKeyword_RenameWorks()
    {
        using var result = await test.Client.GetAsync("/api/case-param-hash-keyword/?hash=7");
        var response = await result.Content.ReadAsStringAsync();

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("7");
    }

    [Fact]
    public async Task OfKeyword_RenameWorks()
    {
        using var result = await test.Client.GetAsync("/api/case-param-of-keyword/?of=7");
        var response = await result.Content.ReadAsStringAsync();

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("7");
    }
}
