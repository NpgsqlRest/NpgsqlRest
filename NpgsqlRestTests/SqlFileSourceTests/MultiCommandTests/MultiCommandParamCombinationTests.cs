namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void MultiCommandParamCombinationTests()
    {
        // Shared parameter $1 used in all commands, $2 only in some
        File.WriteAllText(Path.Combine(Dir, "multi_shared_params.sql"), """
            -- HTTP POST
            -- @param $1 id
            -- @param $2 new_name
            select name from sql_describe_test where id = $1;
            update sql_describe_test set name = $2 where id = $1;
            select name from sql_describe_test where id = $1;
            """);

        // Both commands use both $1 and $2 for different lookups
        File.WriteAllText(Path.Combine(Dir, "multi_disjoint_params.sql"), """
            -- HTTP POST
            -- @param $1 first_id
            -- @param $2 second_id
            select id, name from sql_describe_test where id = $1 and $2 > 0;
            select id, name from sql_describe_test where id = $2 and $1 > 0;
            """);

        // Two params used naturally across three commands
        File.WriteAllText(Path.Combine(Dir, "multi_complex_params.sql"), """
            -- HTTP POST
            -- @result1 check_exists
            -- @result2 update_result
            -- @result3 final_check
            -- @param $1 id
            -- @param $2 target_name
            select count(*) as cnt from sql_describe_test where id = $1 and name != $2;
            update sql_describe_test set name = $2 where id = $1;
            select id, name from sql_describe_test where id = $1 and name = $2;
            """);

        // No parameters at all — multiple parameterless SELECTs
        File.WriteAllText(Path.Combine(Dir, "multi_no_params.sql"), """
            select count(*) as total from sql_describe_test;
            select min(id) as min_id, max(id) as max_id from sql_describe_test;
            select now() as server_time;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class MultiCommandParamCombinationTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task SharedParams_UpdateAndVerify()
    {
        using var body = new StringContent(
            "{\"id\": 2, \"new_name\": \"shared_test\"}",
            Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/multi-shared-params", body);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("""{"result1":["test2"],"result2":1,"result3":["shared_test"]}""");

        // Restore
        using var restore = new StringContent(
            "{\"id\": 2, \"new_name\": \"test2\"}",
            Encoding.UTF8, "application/json");
        await test.Client.PostAsync("/api/multi-shared-params", restore);
    }

    [Fact]
    public async Task DisjointParams_EachCommandGetsItsParam()
    {
        using var body = new StringContent(
            "{\"first_id\": 1, \"second_id\": 2}",
            Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/multi-disjoint-params", body);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("""{"result1":[{"id":1,"name":"test1"}],"result2":[{"id":2,"name":"test2"}]}""");
    }

    [Fact]
    public async Task ComplexParams_ThreeParamsAcrossFourCommands()
    {
        using var body = new StringContent(
            "{\"id\": 1, \"target_name\": \"complex_test\"}",
            Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/multi-complex-params", body);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("""{"check_exists":[1],"update_result":1,"final_check":[{"id":1,"name":"complex_test"}]}""");

        // Restore original values
        using var restore = new StringContent(
            "{\"id\": 1, \"target_name\": \"test1\"}",
            Encoding.UTF8, "application/json");
        await test.Client.PostAsync("/api/multi-complex-params", restore);
    }

    [Fact]
    public async Task NoParams_MultipleParameterlessSelects()
    {
        using var response = await test.Client.GetAsync("/api/multi-no-params");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        // count(*), min/max, and now() are all non-deterministic
        content.Should().Contain("\"result1\":[");
        content.Should().Contain("\"result2\":[{\"minId\":");
        content.Should().Contain("\"result3\":[\"");
    }
}
