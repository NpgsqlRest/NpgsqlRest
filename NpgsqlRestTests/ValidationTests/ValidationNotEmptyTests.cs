namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ValidationNotEmptyTests()
    {
        script.Append(@"
create function validate_not_empty_test(_email text)
returns text
language plpgsql
as
$$
begin
    return coalesce(_email, 'was null');
end;
$$;

comment on function validate_not_empty_test(text) is '
HTTP POST
validate _email using not_empty
';
");
    }
}

[Collection("TestFixture")]
public class ValidationNotEmptyTests(TestFixture test)
{
    [Fact]
    public async Task Test_validate_not_empty_returns_bad_request_when_empty()
    {
        using var content = new StringContent("{\"email\": \"\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-not-empty-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Contain("empty");
    }

    [Fact]
    public async Task Test_validate_not_empty_passes_for_null()
    {
        // NotEmpty only checks for empty string - null values pass
        // Use not_null rule if you need to validate for null
        using var content = new StringContent("{\"email\": null}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-not-empty-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Be("was null");
    }

    [Fact]
    public async Task Test_validate_not_empty_passes_when_value_provided()
    {
        using var content = new StringContent("{\"email\": \"test@example.com\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-not-empty-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Be("test@example.com");
    }
}
