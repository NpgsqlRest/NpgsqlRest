using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

[Collection("McpAudienceFixture")]
public class McpAudienceTests(McpAudienceTestFixture test)
{
    private const string Ping = """{"jsonrpc":"2.0","id":1,"method":"ping"}""";

    private static async Task<HttpResponseMessage> PostPingAsync(HttpClient client)
    {
        var content = new StringContent(Ping, Encoding.UTF8, "application/json");
        return await client.PostAsync("/mcp", content);
    }

    [Fact]
    public async Task A_token_whose_audience_matches_is_accepted()
    {
        using var client = test.CreateClient();
        (await client.GetAsync($"/login-as?aud={Uri.EscapeDataString(McpAudienceTestFixture.Audience)}")).EnsureSuccessStatusCode();

        using var response = await PostPingAsync(client);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("""{"jsonrpc":"2.0","id":1,"result":{}}""");
    }

    [Fact]
    public async Task A_token_issued_for_a_different_resource_is_rejected_with_401()
    {
        using var client = test.CreateClient();
        (await client.GetAsync("/login-as?aud=https://other.example/api")).EnsureSuccessStatusCode();

        using var response = await PostPingAsync(client);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Contain("resource_metadata=");
    }
}
