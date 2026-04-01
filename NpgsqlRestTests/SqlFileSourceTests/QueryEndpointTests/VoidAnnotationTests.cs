namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void VoidAnnotationTests()
    {
        // Single-command void — select with @void returns 204, not JSON
        File.WriteAllText(Path.Combine(Dir, "sf_void_single.sql"), """
            -- HTTP GET
            -- @void
            -- @param $1 my_key text
            -- @param $2 my_value text
            select set_config($1, $2, true);
            """);

        // Multi-command void — all statements execute, returns 204
        File.WriteAllText(Path.Combine(Dir, "sf_void_multi.sql"), """
            -- HTTP POST
            -- @void
            -- @param $1 key1 text
            -- @param $2 val1 text
            -- @param $3 key2 text
            -- @param $4 val2 text
            select set_config($1, $2, true);
            select set_config($3, $4, true);
            """);

        // void_result alias
        File.WriteAllText(Path.Combine(Dir, "sf_void_alias.sql"), """
            -- HTTP GET
            -- @void_result
            -- @param $1 my_key text
            -- @param $2 my_value text
            select set_config($1, $2, true);
            """);

        // Without @void — same multi-command returns JSON object
        File.WriteAllText(Path.Combine(Dir, "sf_no_void_multi.sql"), """
            -- HTTP GET
            -- @param $1 key1 text
            -- @param $2 val1 text
            select set_config($1, $2, true) as result;
            select 'done' as status;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class VoidAnnotationTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task VoidSingle_Returns204()
    {
        using var response = await test.Client.GetAsync("/api/sf-void-single?my_key=test.x&my_value=hello");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().BeEmpty();
    }

    [Fact]
    public async Task VoidMulti_Returns204()
    {
        using var response = await test.Client.PostAsync("/api/sf-void-multi",
            new StringContent("""{"key1":"test.k1","val1":"1","key2":"test.k2","val2":"2"}""",
                System.Text.Encoding.UTF8, "application/json"));

        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, content);
    }

    [Fact]
    public async Task VoidResultAlias_Returns204()
    {
        using var response = await test.Client.GetAsync("/api/sf-void-alias?my_key=test.y&my_value=world");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().BeEmpty();
    }

    [Fact]
    public async Task NoVoidMulti_ReturnsJsonObject()
    {
        // Without @void, multi-command returns JSON object with result keys
        using var response = await test.Client.GetAsync("/api/sf-no-void-multi?key1=test.k&val1=hello");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("result1");
        content.Should().Contain("result2");
    }
}
