namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ValidationRegexTests()
    {
        script.Append(@"
create function validate_regex_test(_email text)
returns text
language plpgsql
as
$$
begin
    return _email;
end;
$$;

comment on function validate_regex_test(text) is '
HTTP POST
validate _email using email
';
");
    }
}

[Collection("TestFixture")]
public class ValidationRegexTests(TestFixture test)
{
    [Fact]
    public async Task Test_validate_regex_returns_bad_request_for_invalid_email()
    {
        using var content = new StringContent("{\"email\": \"not-an-email\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-regex-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("email");
    }

    [Fact]
    public async Task Test_validate_regex_passes_for_valid_email()
    {
        using var content = new StringContent("{\"email\": \"user@example.com\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-regex-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Be("user@example.com");
    }
}
