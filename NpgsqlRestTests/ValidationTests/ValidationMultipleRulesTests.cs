namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ValidationMultipleRulesTests()
    {
        script.Append(@"
create function validate_multiple_rules_test(_email text, _username text)
returns json
language plpgsql
as
$$
begin
    return json_build_object('email', _email, 'username', _username);
end;
$$;

-- Using CSV format: required, email on same line
comment on function validate_multiple_rules_test(text, text) is '
HTTP POST
validate _email using required, email
validate _username using required
';
");
    }
}

[Collection("TestFixture")]
public class ValidationMultipleRulesTests(TestFixture test)
{
    [Fact]
    public async Task Test_validate_multiple_rules_returns_bad_request_for_null()
    {
        // Null email should fail required validation
        using var content = new StringContent("{\"email\": null, \"username\": \"testuser\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-multiple-rules-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("required");
    }

    [Fact]
    public async Task Test_validate_multiple_rules_returns_bad_request_for_empty_string()
    {
        // Empty email should fail required validation
        using var content = new StringContent("{\"email\": \"\", \"username\": \"testuser\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-multiple-rules-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("required");
    }

    [Fact]
    public async Task Test_validate_multiple_rules_returns_bad_request_for_invalid_email_format()
    {
        // Non-empty but invalid email should fail regex validation
        using var content = new StringContent("{\"email\": \"invalid\", \"username\": \"testuser\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-multiple-rules-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("email");
    }

    [Fact]
    public async Task Test_validate_multiple_rules_returns_bad_request_for_empty_username()
    {
        // Valid email but empty username should fail required validation
        using var content = new StringContent("{\"email\": \"user@example.com\", \"username\": \"\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-multiple-rules-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("required");
    }

    [Fact]
    public async Task Test_validate_multiple_rules_passes_when_all_valid()
    {
        using var content = new StringContent("{\"email\": \"user@example.com\", \"username\": \"testuser\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-multiple-rules-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("user@example.com");
        response.Should().Contain("testuser");
    }
}
