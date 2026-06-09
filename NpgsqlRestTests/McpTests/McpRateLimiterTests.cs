using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

/// <summary>
/// Verifies <see cref="NpgsqlRest.Mcp.McpOptions.RateLimiterPolicy"/> throttles the whole <c>/mcp</c>
/// endpoint. The fixture registers a fixed-window policy of one permit per long window, so the second
/// request is rejected by ASP.NET's rate limiter (429) before the JSON-RPC handler runs.
/// </summary>
[Collection("McpRateLimiterFixture")]
public class McpRateLimiterTests(McpRateLimiterTestFixture test)
{
    private async Task<HttpResponseMessage> InitializeAsync()
    {
        using var content = new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""", Encoding.UTF8, "application/json");
        return await test.Client.PostAsync("/mcp", content);
    }

    [Fact]
    public async Task RateLimiterPolicy_throttles_the_mcp_endpoint()
    {
        using var first = await InitializeAsync();
        first.StatusCode.Should().Be(HttpStatusCode.OK);          // first request consumes the single permit

        using var second = await InitializeAsync();
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);  // second is rejected by the rate limiter
    }
}
