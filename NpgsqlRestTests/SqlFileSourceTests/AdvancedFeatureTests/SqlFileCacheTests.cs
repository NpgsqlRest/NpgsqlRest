namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("SqlFileAdvancedFixture")]
public class SqlFileCacheTests(SqlFileAdvancedFixture test)
{
    [Fact]
    public async Task SqlFile_CachedEndpoint_ReturnsSameResultOnSecondCall()
    {
        using var response1 = await test.Client.GetAsync("/api/sf-cache-timestamp");
        var content1 = await response1.Content.ReadAsStringAsync();
        response1.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content1}");

        using var response2 = await test.Client.GetAsync("/api/sf-cache-timestamp");
        var content2 = await response2.Content.ReadAsStringAsync();
        response2.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content2}");

        content1.Should().Be(content2, "cached endpoint should return identical response");
    }

    [Fact]
    public async Task SqlFile_CachedWithKeys_DifferentKeysReturnDifferentResults()
    {
        using var response1 = await test.Client.GetAsync("/api/sf-cache-with-keys?key1=a&key2=b");
        var content1 = await response1.Content.ReadAsStringAsync();
        response1.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content1}");

        using var response2 = await test.Client.GetAsync("/api/sf-cache-with-keys?key1=c&key2=d");
        var content2 = await response2.Content.ReadAsStringAsync();
        response2.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content2}");

        content1.Should().NotBe(content2, "different cache keys should produce different results");
    }

    [Fact]
    public async Task SqlFile_CachedWithKeys_SameKeysReturnSameResult()
    {
        using var response1 = await test.Client.GetAsync("/api/sf-cache-with-keys?key1=x&key2=y");
        var content1 = await response1.Content.ReadAsStringAsync();
        response1.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content1}");

        using var response2 = await test.Client.GetAsync("/api/sf-cache-with-keys?key1=x&key2=y");
        var content2 = await response2.Content.ReadAsStringAsync();
        response2.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content2}");

        content1.Should().Be(content2, "same cache keys should return cached result");
    }

    [Fact]
    public async Task SqlFile_CachedWithExpiry_ExpiresAfterTimeout()
    {
        using var response1 = await test.Client.GetAsync("/api/sf-cache-expires");
        var content1 = await response1.Content.ReadAsStringAsync();
        response1.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content1}");

        // Wait for cache to expire (1 second + buffer)
        await Task.Delay(1500);

        using var response2 = await test.Client.GetAsync("/api/sf-cache-expires");
        var content2 = await response2.Content.ReadAsStringAsync();
        response2.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content2}");

        content1.Should().NotBe(content2, "cache should have expired after 1 second");
    }
}
