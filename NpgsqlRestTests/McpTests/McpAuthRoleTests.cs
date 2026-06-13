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
        // Supplementary RFC 6750-shaped body (the formal challenge stays in the WWW-Authenticate header).
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        (await response.Content.ReadAsStringAsync()).Should().Be(
            """{"error":"insufficient_scope","error_description":"This tool requires a role your token does not have."}""");
    }

    // ---- tools/list role filtering (FilterToolsByRole = true) -------------

    private const string ListRequest = """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""";

    private static async Task<List<string>> ListToolNamesAsync(HttpClient client)
    {
        using var content = new StringContent(ListRequest, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/mcp", content);
        var tools = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["result"]!["tools"]!.AsArray();
        return tools.Select(t => t!["name"]!.GetValue<string>()).ToList();
    }

    [Fact]
    public async Task Anonymous_tools_list_hides_role_restricted_tools()
    {
        using var client = test.CreateClient(); // not logged in
        var names = await ListToolNamesAsync(client);
        names.Should().Contain("tool_basic");          // anonymous-callable tool is listed
        names.Should().NotContain("tool_authorized");  // `@authorize admin` tool is hidden
    }

    [Fact]
    public async Task Authenticated_tools_list_includes_tools_for_the_callers_role()
    {
        using var client = test.CreateClient();
        (await client.GetAsync("/login-as?role=admin")).EnsureSuccessStatusCode();
        var names = await ListToolNamesAsync(client);
        names.Should().Contain("tool_authorized");     // admin sees the admin tool
    }

    [Fact]
    public async Task Authenticated_with_the_wrong_role_still_does_not_see_the_tool()
    {
        using var client = test.CreateClient();
        (await client.GetAsync("/login-as?role=guest")).EnsureSuccessStatusCode();
        var names = await ListToolNamesAsync(client);
        names.Should().Contain("tool_basic");
        names.Should().NotContain("tool_authorized");  // guest lacks `admin`
    }
}
