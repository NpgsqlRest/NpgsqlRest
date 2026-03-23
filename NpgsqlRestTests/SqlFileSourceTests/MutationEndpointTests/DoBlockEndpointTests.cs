namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void DoBlockEndpointTests()
    {
        File.WriteAllText(Path.Combine(Dir, "do_block.sql"), """
            do $$ begin perform 1; end; $$;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class DoBlockEndpointTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task DoBlock_ExecutesSuccessfully()
    {
        using var response = await test.Client.PostAsync("/api/do-block", null);

        // DO blocks return void, so expect 204 No Content
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
