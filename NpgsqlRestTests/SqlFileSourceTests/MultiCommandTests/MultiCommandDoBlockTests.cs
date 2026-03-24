using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void MultiCommandDoBlockTests()
    {
        // Multi-command with a DO block in the middle
        File.WriteAllText(Path.Combine(Dir, "multi_with_do.sql"), """
            -- HTTP POST
            -- @result1 before_count
            -- @result3 after_count
            select count(*) as total from sql_describe_test;
            do $$ begin perform 1; end; $$;
            select count(*) as total from sql_describe_test;
            """);

        // DO block as first command followed by a SELECT
        File.WriteAllText(Path.Combine(Dir, "multi_do_then_select.sql"), """
            -- HTTP POST
            -- @result2 data
            do $$ begin perform 1; end; $$;
            select id, name from sql_describe_test order by id limit 1;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class MultiCommandDoBlockTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task DoBlockInMiddle_SelectsAroundIt_Work()
    {
        using var response = await test.Client.PostAsync("/api/multi-with-do", null);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        // count(*) is non-deterministic (depends on other test side effects), use Contain for structure
        content.Should().Contain("\"before_count\":[{\"total\":");
        content.Should().Contain("\"result2\":-1");
        content.Should().Contain("\"after_count\":[{\"total\":");
    }

    [Fact]
    public async Task DoBlockFirst_ThenSelect_Works()
    {
        using var response = await test.Client.PostAsync("/api/multi-do-then-select", null);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("""{"result1":-1,"data":[{"id":1,"name":"test1"}]}""");
    }
}
