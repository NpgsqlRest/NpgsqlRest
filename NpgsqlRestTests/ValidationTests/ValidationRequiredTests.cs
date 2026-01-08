namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ValidationRequiredTests()
    {
        script.Append(@"
create function validate_required_test(_email text)
returns text
language plpgsql
as
$$
begin
    return coalesce(_email, 'was null');
end;
$$;

comment on function validate_required_test(text) is '
HTTP POST
validate _email using required
';
");
    }
}

[Collection("TestFixture")]
public class ValidationRequiredTests(TestFixture test)
{
    [Fact]
    public async Task Test_validate_required_fails_for_null()
    {
        using var content = new StringContent("{\"email\": null}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-required-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("required");
    }

    [Fact]
    public async Task Test_validate_required_fails_for_empty_string()
    {
        using var content = new StringContent("{\"email\": \"\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-required-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("required");
    }

    [Fact]
    public async Task Test_validate_required_passes_for_valid_value()
    {
        using var content = new StringContent("{\"email\": \"test@example.com\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-required-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Be("test@example.com");
    }

    [Fact]
    public async Task Test_validate_required_passes_for_whitespace()
    {
        // Whitespace is not empty, so it should pass required validation
        using var content = new StringContent("{\"email\": \"   \"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-required-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Be("   ");
    }
}
