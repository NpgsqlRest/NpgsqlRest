namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ValidationCsvFormatTests()
    {
        script.Append(@"
-- Test CSV format with spaces: required, email
create function validate_csv_with_spaces(_email text)
returns text
language plpgsql
as
$$
begin
    return _email;
end;
$$;

comment on function validate_csv_with_spaces(text) is '
HTTP POST
validate _email using required, email
';

-- Test CSV format without spaces: required,email
create function validate_csv_no_spaces(_email text)
returns text
language plpgsql
as
$$
begin
    return _email;
end;
$$;

comment on function validate_csv_no_spaces(text) is '
HTTP POST
validate _email using required,email
';

-- Test CSV format with three rules
create function validate_csv_three_rules(_code text)
returns text
language plpgsql
as
$$
begin
    return _code;
end;
$$;

comment on function validate_csv_three_rules(text) is '
HTTP POST
validate _code using required, not_empty, product_code
';
");
    }
}

[Collection("TestFixture")]
public class ValidationCsvFormatTests(TestFixture test)
{
    [Fact]
    public async Task Test_csv_with_spaces_fails_required()
    {
        using var content = new StringContent("{\"email\": null}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-csv-with-spaces/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("required");
    }

    [Fact]
    public async Task Test_csv_with_spaces_fails_email_regex()
    {
        using var content = new StringContent("{\"email\": \"not-an-email\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-csv-with-spaces/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("email");
    }

    [Fact]
    public async Task Test_csv_with_spaces_passes()
    {
        using var content = new StringContent("{\"email\": \"user@example.com\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-csv-with-spaces/", content);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Be("user@example.com");
    }

    [Fact]
    public async Task Test_csv_no_spaces_fails_required()
    {
        using var content = new StringContent("{\"email\": null}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-csv-no-spaces/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("required");
    }

    [Fact]
    public async Task Test_csv_no_spaces_fails_email_regex()
    {
        using var content = new StringContent("{\"email\": \"not-an-email\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-csv-no-spaces/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("email");
    }

    [Fact]
    public async Task Test_csv_no_spaces_passes()
    {
        using var content = new StringContent("{\"email\": \"user@example.com\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-csv-no-spaces/", content);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Be("user@example.com");
    }

    [Fact]
    public async Task Test_csv_three_rules_fails_required()
    {
        using var content = new StringContent("{\"code\": null}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-csv-three-rules/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("required");
    }

    [Fact]
    public async Task Test_csv_three_rules_fails_not_empty()
    {
        using var content = new StringContent("{\"code\": \"\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-csv-three-rules/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        // Required or not_empty - both would catch empty string
        response.Should().Contain("required");
    }

    [Fact]
    public async Task Test_csv_three_rules_fails_product_code()
    {
        using var content = new StringContent("{\"code\": \"invalid\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-csv-three-rules/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("Product code");
    }

    [Fact]
    public async Task Test_csv_three_rules_passes()
    {
        using var content = new StringContent("{\"code\": \"ABC-1234\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-csv-three-rules/", content);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Be("ABC-1234");
    }
}
