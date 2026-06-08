using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

[Collection("McpAuthGateFixture")]
public class McpAuthGateTests(McpAuthGateTestFixture test)
{
    [Fact]
    public async Task Unauthenticated_request_is_rejected_with_401_and_prm_challenge()
    {
        using var content = new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/mcp", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var challenge = response.Headers.WwwAuthenticate.ToString();
        challenge.Should().StartWith("Bearer");
        // RFC 9728 §5.1: point the client at the Protected Resource Metadata document for this resource.
        challenge.Should().Contain("resource_metadata=");
        challenge.Should().Contain("/.well-known/oauth-protected-resource/mcp");
    }

    [Fact]
    public async Task Protected_resource_metadata_itself_stays_anonymous()
    {
        // The PRM document is discovery — it must be reachable without a token even when the gate is on.
        using var response = await test.Client.GetAsync("/.well-known/oauth-protected-resource/mcp");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
