using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("SqlFileProxyFixture")]
public class SqlFileProxyOutTests(SqlFileProxyFixture test)
{
    [Fact]
    public async Task SqlFile_ProxyOut_ExecutesSqlThenForwardsToUpstream()
    {
        test.Server.Reset();
        test.Server
            .Given(Request.Create()
                .WithPath("/api/sf-proxy-out-basic")
                .UsingPost()
                .WithBody(b => b.Contains("key") && b.Contains("value")))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"processed\":true}"));

        using var response = await test.Client.GetAsync("/api/sf-proxy-out-basic");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Contain("processed");
    }
}
