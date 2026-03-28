namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("SqlFileAdvancedFixture")]
public class SqlFileValidationTests(SqlFileAdvancedFixture test)
{
    [Fact]
    public async Task SqlFile_ValidateNotEmpty_EmptyStringReturnsBadRequest()
    {
        using var content = new StringContent("{\"email\": \"\"}", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/sf-validate-not-empty", content);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"Response: {body}");
        body.Should().Contain("empty");
    }

    [Fact]
    public async Task SqlFile_ValidateNotEmpty_ValidValueReturnsOk()
    {
        using var content = new StringContent("{\"email\": \"test@example.com\"}", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/sf-validate-not-empty", content);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");
        body.Should().Contain("test@example.com");
    }

    [Fact]
    public async Task SqlFile_ValidateNotEmpty_NullPassesValidation()
    {
        // not_empty allows null (use not_null or required to reject null)
        using var content = new StringContent("{\"email\": null}", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/sf-validate-not-empty", content);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");
        body.Should().Contain("was null");
    }

    [Fact]
    public async Task SqlFile_ValidateRequired_NullReturnsBadRequest()
    {
        using var content = new StringContent("{\"name\": null}", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/sf-validate-required", content);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"Response: {body}");
        body.Should().Contain("required");
    }

    [Fact]
    public async Task SqlFile_ValidateRequired_EmptyReturnsBadRequest()
    {
        using var content = new StringContent("{\"name\": \"\"}", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/sf-validate-required", content);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"Response: {body}");
        body.Should().Contain("required");
    }

    [Fact]
    public async Task SqlFile_ValidateRequired_ValidValueReturnsOk()
    {
        using var content = new StringContent("{\"name\": \"Alice\"}", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/sf-validate-required", content);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");
        body.Should().Contain("Hello Alice");
    }

    [Fact]
    public async Task SqlFile_ValidateEmail_InvalidFormatReturnsBadRequest()
    {
        using var content = new StringContent("{\"email\": \"not-an-email\"}", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/sf-validate-email", content);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"Response: {body}");
        body.Should().Contain("email");
    }

    [Fact]
    public async Task SqlFile_ValidateEmail_ValidFormatReturnsOk()
    {
        using var content = new StringContent("{\"email\": \"user@example.com\"}", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/sf-validate-email", content);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");
        body.Should().Contain("user@example.com");
    }
}
