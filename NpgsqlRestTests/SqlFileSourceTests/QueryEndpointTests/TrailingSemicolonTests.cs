namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void TrailingSemicolonTests()
    {
        File.WriteAllText(Path.Combine(Dir, "trailing_semi.sql"), """
            select $1 as val;
            """);

        File.WriteAllText(Path.Combine(Dir, "trailing_multi_semi.sql"), """
            select $1 as val;;;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class TrailingSemicolonTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task TrailingSemicolon_StillSingleCommand()
    {
        using var response = await test.Client.GetAsync("/api/trailing-semi?$1=test");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("[{\"val\":\"test\"}]");
    }

    [Fact]
    public async Task TrailingMultipleSemicolons_StillSingleCommand()
    {
        using var response = await test.Client.GetAsync("/api/trailing-multi-semi?$1=test");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("[{\"val\":\"test\"}]");
    }
}
