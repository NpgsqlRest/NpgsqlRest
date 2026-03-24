namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void MultiCommandTransactionTests()
    {
        // Transactional script: BEGIN + mutations + COMMIT + verification SELECT
        File.WriteAllText(Path.Combine(Dir, "multi_transaction.sql"), """
            -- HTTP POST
            -- @result4 verification
            -- @param $1 id
            -- @param $2 new_name
            begin;
            update sql_describe_test set name = $2 where id = $1;
            commit;
            select id, name from sql_describe_test where id = $1;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class MultiCommandTransactionTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task Transaction_UpdateAndVerify_ReturnsUpdatedData()
    {
        using var body = new StringContent(
            "{\"id\": 2, \"new_name\": \"txn_updated\"}",
            Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/multi-transaction", body);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");
        content.Should().Be("""{"result1":-1,"result2":1,"result3":-1,"verification":[{"id":2,"name":"txn_updated"}]}""");

        // Restore original value
        using var restore = new StringContent(
            "{\"id\": 2, \"new_name\": \"test2\"}",
            Encoding.UTF8, "application/json");
        await test.Client.PostAsync("/api/multi-transaction", restore);
    }
}
