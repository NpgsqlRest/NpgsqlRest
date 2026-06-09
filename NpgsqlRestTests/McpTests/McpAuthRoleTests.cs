using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

[Collection("McpAuthRoleFixture")]
public class McpAuthRoleTests(McpAuthRoleTestFixture test)
{
    private const string CallAuthorized =
        """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"tool_authorized","arguments":{}}}""";

    [Fact]
    public async Task Authenticated_with_the_required_role_executes_the_tool()
    {
        using var client = test.CreateClient();
        (await client.GetAsync("/login-as?role=admin")).EnsureSuccessStatusCode();

        using var content = new StringContent(CallAuthorized, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/mcp", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // tool_authorized returns the scalar text 'secret'.
        (await response.Content.ReadAsStringAsync()).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"value\":\"secret\"}"}],"isError":false,"structuredContent":{"value":"secret"}}}""");
    }

    [Fact]
    public async Task Authenticated_with_the_wrong_role_is_rejected_with_403_insufficient_scope()
    {
        using var client = test.CreateClient();
        (await client.GetAsync("/login-as?role=guest")).EnsureSuccessStatusCode();

        using var content = new StringContent(CallAuthorized, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/mcp", content);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var challenge = response.Headers.WwwAuthenticate.ToString();
        challenge.Should().Contain("error=\"insufficient_scope\"");
        challenge.Should().Contain("scope=\"mcp.read\"");
        challenge.Should().Contain("resource_metadata=");
    }
}
