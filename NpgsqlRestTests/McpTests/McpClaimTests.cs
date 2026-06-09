using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    // Claim-binding tool, isolated in the `mcp_claim` schema. `_user_id` is mapped to the
    // `name_identifier` claim (see McpClaimTestFixture), so it binds from the caller's principal.
    public static void McpClaimTools()
    {
        script.Append(@"
create schema if not exists mcp_claim;

create function mcp_claim.claim_echo(_user_id text) returns text
language sql as 'select coalesce(_user_id, ''anonymous'')';
comment on function mcp_claim.claim_echo(text) is '
HTTP GET
@mcp Returns the caller user id from their claim.';
");
    }
}

[Collection("McpClaimFixture")]
public class McpClaimTests(McpClaimTestFixture test)
{
    private const string Call =
        """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"claim_echo","arguments":{}}}""";

    [Fact]
    public async Task A_claim_mapped_parameter_binds_from_the_forwarded_principal()
    {
        using var client = test.CreateClient();
        (await client.GetAsync("/login-as?uid=42")).EnsureSuccessStatusCode();

        using var content = new StringContent(Call, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/mcp", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // _user_id was never supplied as an argument — it bound from the name_identifier claim.
        (await response.Content.ReadAsStringAsync()).Should().Be(
            """{"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"{\"value\":\"42\"}"}],"isError":false,"structuredContent":{"value":"42"}}}""");
    }

    [Fact]
    public void A_claim_mapped_parameter_is_hidden_from_inputSchema()
    {
        // claim_echo's only parameter is claim-sourced, so the agent must not (and cannot) supply it.
        test.Tools["claim_echo"]!.ToJsonString().Should().Be(
            """{"name":"claim_echo","description":"Returns the caller user id from their claim.","inputSchema":{"type":"object","properties":{}},"annotations":{"readOnlyHint":true},"outputSchema":{"type":"object","properties":{"value":{"type":["string","null"]}}}}""");
    }
}
