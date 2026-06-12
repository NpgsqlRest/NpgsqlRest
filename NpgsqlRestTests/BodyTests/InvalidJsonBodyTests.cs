#pragma warning disable CS8602 // Dereference of a possibly null reference.
namespace NpgsqlRestTests;

public static partial class Database
{
    public static void InvalidJsonBodyTests()
    {
        script.Append(@"
        create function invalid_json_body_test(
            _i int,
            _t text
        )
        returns text
        language sql as $$select _i || '-' || _t;$$;
");
    }
}

[Collection("TestFixture")]
public class InvalidJsonBodyTests(TestFixture test)
{
    [Fact]
    public async Task Test_invalid_json_body_returns_400()
    {
        string body = """
        {
            "i": 666,
            "t": "numberofthebeast"
        """; // truncated JSON - missing closing brace
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/invalid-json-body-test/", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Test_non_object_json_body_returns_400()
    {
        string body = """["not", "an", "object"]""";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/invalid-json-body-test/", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Test_valid_json_body_still_works()
    {
        string body = """
        {
            "i": 666,
            "t": "numberofthebeast"
        }
        """;
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/invalid-json-body-test/", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType.MediaType.Should().Be("text/plain");
        var result = await response.Content.ReadAsStringAsync();
        result.Should().Be("666-numberofthebeast");
    }
}
