namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void MultiSelectEndpointTests()
    {
        File.WriteAllText(Path.Combine(Dir, "multi_select.sql"), """
            -- @param $1 id
            select id, name from sql_describe_test where id = $1;
            select count(*) as total from sql_describe_test;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class MultiSelectEndpointTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task MultiSelect_ReturnsJsonObjectWithResultKeys()
    {
        using var response = await test.Client.GetAsync("/api/multi-select?id=1");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        // result1 is deterministic (id=1 filter), result2 count(*) is non-deterministic
        content.Should().Contain("\"result1\":[{\"id\":1,\"name\":\"test1\"}]");
        content.Should().Contain("\"result2\":[");
    }
}
