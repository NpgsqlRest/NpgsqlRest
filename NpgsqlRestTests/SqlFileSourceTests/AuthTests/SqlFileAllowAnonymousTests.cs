namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void SqlFileAllowAnonymousTests()
    {
        // Allow anonymous — explicitly public
        File.WriteAllText(Path.Combine(Dir, "sf_anon_endpoint.sql"), """
            -- @allow_anonymous
            select 'anon_ok' as result;
            """);

        // No auth annotation — should be accessible without auth
        File.WriteAllText(Path.Combine(Dir, "sf_no_auth_endpoint.sql"), """
            select 'no_auth_ok' as result;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class SqlFileAllowAnonymousTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task AllowAnonymous_Ok_Without_Login()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/sf-anon-endpoint");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("anon_ok");
    }

    [Fact]
    public async Task NoAnnotation_Accessible_Without_Login()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/sf-no-auth-endpoint");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("no_auth_ok");
    }
}
