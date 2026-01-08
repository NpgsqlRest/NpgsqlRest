namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ValidationNotNullTests()
    {
        script.Append(@"
create function validate_not_null_test(_email text)
returns text
language plpgsql
as
$$
begin
    return coalesce(_email, 'was null');
end;
$$;

comment on function validate_not_null_test(text) is '
HTTP POST
validate _email using not_null
';
");
    }
}

[Collection("TestFixture")]
public class ValidationNotNullTests(TestFixture test)
{
    [Fact]
    public async Task Test_validate_not_null_returns_bad_request_when_null()
    {
        // Send explicit null value for the parameter
        using var content = new StringContent("{\"email\": null}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-not-null-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("null");
    }

    [Fact]
    public async Task Test_validate_not_null_passes_for_empty_string()
    {
        // NotNull should pass for empty string - it only checks for null
        using var content = new StringContent("{\"email\": \"\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-not-null-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Be("");
    }

    [Fact]
    public async Task Test_validate_not_null_passes_for_valid_value()
    {
        using var content = new StringContent("{\"email\": \"test@example.com\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-not-null-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Be("test@example.com");
    }
}
