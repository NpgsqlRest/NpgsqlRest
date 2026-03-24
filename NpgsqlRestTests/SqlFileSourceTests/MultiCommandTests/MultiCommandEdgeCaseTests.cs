namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void MultiCommandEdgeCaseTests()
    {
        // Two void commands — no result sets, only rows-affected counts
        File.WriteAllText(Path.Combine(Dir, "multi_all_void.sql"), """
            -- HTTP POST
            -- @param $1 id
            update sql_describe_test set active = true where id = $1;
            update sql_describe_test set active = true where id = $1;
            """);

        // SELECT returning no rows + SELECT returning rows (both use $1 and $2)
        File.WriteAllText(Path.Combine(Dir, "multi_empty_and_full.sql"), """
            -- HTTP POST
            -- @param $1 missing_id
            -- @param $2 existing_id
            select id, name from sql_describe_test where id = $1 and $2 > 0;
            select id, name from sql_describe_test where id = $2 and $1 > 0;
            """);

        // Many commands (5 selects)
        File.WriteAllText(Path.Combine(Dir, "multi_five_selects.sql"), """
            select 1 as a;
            select 2 as b;
            select 3 as c;
            select 4 as d;
            select 5 as e;
            """);

        // SELECT with different column shapes per command
        File.WriteAllText(Path.Combine(Dir, "multi_different_shapes.sql"), """
            -- @result1 single_col
            -- @result2 two_cols
            -- @result3 three_cols
            select 'hello' as greeting;
            select 1 as num, 'text' as str;
            select true as flag, 42 as value, now() as ts;
            """);

        // Result annotations inline above each command (not in header)
        File.WriteAllText(Path.Combine(Dir, "multi_inline_annotations.sql"), """
            -- @param $1 id
            -- @result1 first_lookup
            select id, name from sql_describe_test where id = $1;
            -- @result2 all_count
            select count(*) as total from sql_describe_test;
            -- @result3 active_check
            select active from sql_describe_test where id = $1;
            """);

        // INSERT with RETURNING (non-void mutation that returns data)
        File.WriteAllText(Path.Combine(Dir, "multi_insert_returning.sql"), """
            -- HTTP POST
            -- @result1 inserted
            -- @result2 count
            -- @param $1 id
            -- @param $2 name
            insert into sql_describe_test (id, name) values ($1, $2) returning id, name;
            select count(*) as total from sql_describe_test;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class MultiCommandEdgeCaseTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task AllVoid_ReturnsOnlyRowsAffectedCounts()
    {
        using var body = new StringContent("{\"id\": 1}", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/multi-all-void", body);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("""{"result1":1,"result2":1}""");
    }

    [Fact]
    public async Task EmptyAndFull_FirstEmptySecondHasData()
    {
        using var body = new StringContent(
            "{\"missing_id\": 999, \"existing_id\": 1}",
            Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/multi-empty-and-full", body);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("""{"result1":[],"result2":[{"id":1,"name":"test1"}]}""");
    }

    [Fact]
    public async Task FiveSelects_AllResultKeysPresent()
    {
        using var response = await test.Client.GetAsync("/api/multi-five-selects");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("""{"result1":[{"a":1}],"result2":[{"b":2}],"result3":[{"c":3}],"result4":[{"d":4}],"result5":[{"e":5}]}""");
    }

    [Fact]
    public async Task DifferentShapes_EachResultHasDifferentColumns()
    {
        using var response = await test.Client.GetAsync("/api/multi-different-shapes");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        // Third result contains now() which is non-deterministic
        content.Should().Contain("""{"single_col":[{"greeting":"hello"}],"two_cols":[{"num":1,"str":"text"}],"three_cols":[{"flag":true,"value":42,"ts":""");
    }

    [Fact]
    public async Task InlineAnnotations_ResultNamesAppliedCorrectly()
    {
        using var response = await test.Client.GetAsync("/api/multi-inline-annotations?id=1");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        // count(*) is non-deterministic; active and lookup are deterministic
        content.Should().Contain("\"first_lookup\":[{\"id\":1,\"name\":\"test1\"}]");
        content.Should().Contain("\"all_count\":[{\"total\":");
        content.Should().Contain("\"active_check\":[{\"active\":true}]");
    }

    [Fact]
    public async Task InsertReturning_NonVoidMutationReturnsData()
    {
        var uniqueId = 5000 + Random.Shared.Next(1000);
        using var body = new StringContent(
            $"{{\"id\": {uniqueId}, \"name\": \"returning_test\"}}",
            Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/multi-insert-returning", body);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        // ID is random and count is non-deterministic, use Contain for key parts
        content.Should().Contain($"\"inserted\":[{{\"id\":{uniqueId},\"name\":\"returning_test\"}}]");
        content.Should().Contain("\"count\":[{\"total\":");

        // Cleanup
        using var conn = Database.CreateConnection();
        conn.Open();
        using var cmd = new Npgsql.NpgsqlCommand($"delete from sql_describe_test where id = {uniqueId}", conn);
        cmd.ExecuteNonQuery();
    }
}
