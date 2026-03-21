namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("SqlFileSourceFixture")]
public class FileDiscoveryTests(SqlFileSourceTestFixture test)
{
    [Theory]
    [InlineData("/api/get-time", "GET")]
    [InlineData("/api/get-by-id?id=1", "GET")]
    [InlineData("/api/search-test?name_filter=test&active_filter=true", "GET")]
    [InlineData("/api/count-test", "GET")]
    [InlineData("/api/sub-query", "GET")]
    [InlineData("/api/annotated-query?from_date=2023-01-01T00:00:00Z&to_date=2025-01-01T00:00:00Z", "GET")]
    public async Task AllQueryEndpoints_ReturnNon404(string url, string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        using var response = await test.Client.SendAsync(request);
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            because: $"endpoint {method} {url} should be discovered from SQL files");
    }

    [Theory]
    [InlineData("/api/do-block", "POST")]
    public async Task MutationEndpoints_ReturnNon404(string url, string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        using var response = await test.Client.SendAsync(request);
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            because: $"endpoint {method} {url} should be discovered from SQL files");
    }
}
