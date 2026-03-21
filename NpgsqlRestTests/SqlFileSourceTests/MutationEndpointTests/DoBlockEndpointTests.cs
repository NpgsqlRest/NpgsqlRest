namespace NpgsqlRestTests.SqlFileSourceTests;

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
