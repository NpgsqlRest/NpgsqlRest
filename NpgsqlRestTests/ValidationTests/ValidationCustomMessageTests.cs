namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ValidationCustomMessageTests()
    {
        script.Append(@"
create function validate_custom_message_test(_code text)
returns text
language plpgsql
as
$$
begin
    return _code;
end;
$$;

comment on function validate_custom_message_test(text) is '
HTTP POST
validate _code using product_code
';
");
    }
}

[Collection("TestFixture")]
public class ValidationCustomMessageTests(TestFixture test)
{
    [Fact]
    public async Task Test_validate_custom_message_shows_custom_error()
    {
        // product_code rule should have a custom message configured
        using var content = new StringContent("{\"code\": \"invalid\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-custom-message-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await result.Content.ReadAsStringAsync();
        // Should contain the custom message from product_code validation rule
        response.Should().Contain("Product code");
    }

    [Fact]
    public async Task Test_validate_custom_message_passes_for_valid_product_code()
    {
        // Valid product code format: XXX-0000
        using var content = new StringContent("{\"code\": \"ABC-1234\"}", Encoding.UTF8, "application/json");
        using var result = await test.Client.PostAsync("/api/validate-custom-message-test/", content);

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await result.Content.ReadAsStringAsync();
        response.Should().Be("ABC-1234");
    }
}
