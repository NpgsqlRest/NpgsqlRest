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
        // Supplementary RFC 6750-shaped body so clients that surface the response body (and humans with
        // curl) get an actionable message instead of an empty 401 — the formal challenge stays in the header.
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        (await response.Content.ReadAsStringAsync()).Should().Be(
            """{"error":"invalid_token","error_description":"This tool requires authentication. Provide a valid bearer token."}""");
    }

    [Fact]
    public async Task Protected_resource_metadata_itself_stays_anonymous()
    {
        // The PRM document is discovery — it must be reachable without a token even when the gate is on.
        using var response = await test.Client.GetAsync("/.well-known/oauth-protected-resource/mcp");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void RequireAuthorization_without_an_auth_scheme_logs_a_startup_warning()
    {
        // This fixture enables RequireAuthorization but registers no authentication scheme, so the plugin
        // should warn at startup that every request will be 401.
        test.StartupLogs.Should().Contain(l =>
            l.Message.Contains("RequireAuthorization is enabled but no authentication scheme is registered"));
    }
}
