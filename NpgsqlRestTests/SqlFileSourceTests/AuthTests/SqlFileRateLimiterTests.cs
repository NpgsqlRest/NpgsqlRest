using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void SqlFileRateLimiterTests()
    {
        // No rate limiter — unlimited
        File.WriteAllText(Path.Combine(Dir, "sql_rate_unlimited.sql"), """
            select current_user as user_name;
            """);

        // Rate limited endpoint
        File.WriteAllText(Path.Combine(Dir, "sql_rate_limited.sql"), """
            -- @rate_limiter max 2 per second
            select current_user as user_name;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class SqlFileRateLimiterTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task RateUnlimited_ThreeRequests_AllOk()
    {
        using var client = test.CreateClient();

        using var r1 = await client.GetAsync("/api/sql-rate-unlimited");
        using var r2 = await client.GetAsync("/api/sql-rate-unlimited");
        using var r3 = await client.GetAsync("/api/sql-rate-unlimited");

        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
        r3.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RateLimited_ThreeRequests_ThirdRejected()
    {
        using var client = test.CreateClient();

        using var r1 = await client.GetAsync("/api/sql-rate-limited");
        using var r2 = await client.GetAsync("/api/sql-rate-limited");
        using var r3 = await client.GetAsync("/api/sql-rate-limited");

        r1.StatusCode.Should().Be(HttpStatusCode.OK);
        r2.StatusCode.Should().Be(HttpStatusCode.OK);
        r3.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var content = await r3.Content.ReadAsStringAsync();
        content.Should().Be("Rate limit exceeded. Please try again later.");
    }
}
