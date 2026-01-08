namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ValidationConvertedNameTests()
    {
        script.Append(@"
-- Test using converted parameter name (without underscore prefix) in validation annotation
create function validate_converted_name_test(_user_email text)
returns text
language plpgsql
as
$$
begin
    return _user_email;
end;
$$;

-- Using converted name 'userEmail' instead of original '_user_email'
comment on function validate_converted_name_test(text) is '
HTTP POST
validate userEmail using not_empty
validate userEmail using email
';

-- Test mixing original and converted names in same function
create function validate_mixed_names_test(_first_name text, _last_email text)
returns json
language plpgsql
as
$$
begin
    return json_build_object('firstName', _first_name, 'lastEmail', _last_email);
end;
$$;

-- Mix of original name (_first_name) and converted name (lastEmail)
comment on function validate_mixed_names_test(text, text) is '
HTTP POST
validate _first_name using not_empty
validate lastEmail using email
';

-- Test message format placeholders with converted names
create function validate_message_format_test(_my_param text)
returns text
language plpgsql
as
$$
begin
    return _my_param;
end;
$$;

comment on function validate_message_format_test(text) is '
HTTP POST
validate myParam using format_test
';
");
    }
}

[Collection("TestFixture")]
public class ValidationConvertedNameTests(TestFixture test)
{
    [Fact]
    public async Task Test_validate_using_converted_name_returns_bad_request_when_empty()
    {
        // The annotation uses 'userEmail' (converted name) but we send 'userEmail' in JSON
        using var content = new StringContent("{\"userEmail\": \"\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-converted-name-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("email");
    }

    [Fact]
    public async Task Test_validate_using_converted_name_returns_bad_request_for_invalid_email()
    {
        using var content = new StringContent("{\"userEmail\": \"not-an-email\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-converted-name-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("email");
    }

    [Fact]
    public async Task Test_validate_using_converted_name_passes_for_valid_email()
    {
        using var content = new StringContent("{\"userEmail\": \"user@example.com\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-converted-name-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Be("user@example.com");
    }

    [Fact]
    public async Task Test_validate_mixed_names_original_name_validation()
    {
        // _first_name validation using original name in annotation
        using var content = new StringContent("{\"firstName\": \"\", \"lastEmail\": \"user@example.com\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-mixed-names-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("first");
    }

    [Fact]
    public async Task Test_validate_mixed_names_converted_name_validation()
    {
        // lastEmail validation using converted name in annotation
        using var content = new StringContent("{\"firstName\": \"John\", \"lastEmail\": \"invalid-email\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-mixed-names-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("email");
    }

    [Fact]
    public async Task Test_validate_mixed_names_passes_when_all_valid()
    {
        using var content = new StringContent("{\"firstName\": \"John\", \"lastEmail\": \"john@example.com\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-mixed-names-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("John");
        response.Should().Contain("john@example.com");
    }

    [Fact]
    public async Task Test_validate_message_format_contains_all_placeholders()
    {
        // The format_test rule (NotNull type) has message: "Original: {0}, Converted: {1}, Rule: {2}"
        // Send null to trigger the validation failure
        using var content = new StringContent("{\"myParam\": null}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-message-format-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        // Should contain original name
        response.Should().Contain("_my_param");
        // Should contain converted name
        response.Should().Contain("myParam");
        // Should contain rule name
        response.Should().Contain("format_test");
    }
}
