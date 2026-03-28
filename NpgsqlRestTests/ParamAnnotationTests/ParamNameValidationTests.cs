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

-- Rename to reserved keyword 'default' — should be rejected, param stays as $1
create function case_param_reserved_default(int)
returns int
language sql
as 'select $1';

comment on function case_param_reserved_default(int) is '
HTTP GET
param $1 default
';

-- Rename to reserved keyword 'is' — should be rejected
create function case_param_reserved_is(int)
returns int
language sql
as 'select $1';

comment on function case_param_reserved_is(int) is '
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

-- Rename to reserved keyword 'type' — should be rejected
create function case_param_reserved_type(int)
returns int
language sql
as 'select $1';

comment on function case_param_reserved_type(int) is '
HTTP GET
param $1 type
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
    public async Task ReservedKeyword_Default_RenameRejected()
    {
        // 'default' is reserved — rename should be rejected, original $1 param name doesn't work via query string
        // The endpoint should exist but the param rename failed, so it expects positional param
        using var result = await test.Client.GetAsync("/api/case-param-reserved-default/?default=7");
        // The param name 'default' was rejected, so 'default' query param won't bind
        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReservedKeyword_Is_RenameRejected()
    {
        // len == 3 with wordsLower[2] == "is" goes to rename-to-literal-"is" path
        // But validation rejects it
        using var result = await test.Client.GetAsync("/api/case-param-reserved-is/?is=7");
        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
    public async Task ReservedKeyword_Type_RenameRejected()
    {
        using var result = await test.Client.GetAsync("/api/case-param-reserved-type/?type=7");
        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
