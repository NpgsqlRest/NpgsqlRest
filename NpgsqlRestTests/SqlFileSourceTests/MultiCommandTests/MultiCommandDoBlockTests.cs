namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void MultiCommandDoBlockTests()
    {
        // Multi-command with a DO block in the middle (auto-skipped)
        File.WriteAllText(Path.Combine(Dir, "multi_with_do.sql"), """
            -- HTTP POST
            -- @result before_count
            select count(*) as total from sql_describe_test;
            do $$ begin perform 1; end; $$;
            -- @result after_count
            select count(*) as total from sql_describe_test;
            """);

        // DO block as first command (auto-skipped) followed by a SELECT
        File.WriteAllText(Path.Combine(Dir, "multi_do_then_select.sql"), """
            -- HTTP POST
            do $$ begin perform 1; end; $$;
            -- @result data
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

        // DO block is auto-skipped, only two SELECT results remain
        content.Should().Contain("\"before_count\":[");
        content.Should().Contain("\"after_count\":[");
        content.Should().NotContain("-1");
    }

    [Fact]
    public async Task DoBlockFirst_ThenSelect_Works()
    {
        using var response = await test.Client.PostAsync("/api/multi-do-then-select", null);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("{\"data\":[{\"id\":1,\"name\":\"test1\"}]}");
    }
}
