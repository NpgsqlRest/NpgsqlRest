namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void MultiMixedEndpointTests()
    {
        File.WriteAllText(Path.Combine(Dir, "multi_mixed.sql"), """
            -- HTTP POST
            -- @result1 lookup
            -- @result3 verify
            -- @param $1 id
            select name from sql_describe_test where id = $1;
            insert into sql_describe_test (id, name) values ($1 + 1000, 'multi_test');
            select count(*) as total from sql_describe_test;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class MultiMixedEndpointTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task MultiMixed_VoidCommandIsNull_AnnotatedNamesUsed()
    {
        // multi_mixed.sql: SELECT (annotated "lookup") + INSERT (void → rows affected) + SELECT (annotated "verify")
        using var body = new StringContent("{\"id\": 1}", Encoding.UTF8, "application/json");
        using var response = await test.Client.PostAsync("/api/multi-mixed", body);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {content}");

        // lookup and result2 are deterministic; verify count(*) is non-deterministic
        content.Should().Contain("\"lookup\":[\"test1\"]");
        content.Should().Contain("\"result2\":1");
        content.Should().Contain("\"verify\":[");
    }
}
