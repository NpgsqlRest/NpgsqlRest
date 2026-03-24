namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void SqlFileUserContextTests()
    {
        // user_context — sets request.user_id, request.user_name, request.user_roles as PG settings
        File.WriteAllText(Path.Combine(Dir, "sf_get_user_context.sql"), """
            -- @authorize
            -- @user_context
            select
                current_setting('request.user_id', true) as user_id,
                current_setting('request.user_name', true) as user_name,
                current_setting('request.user_roles', true) as user_roles;
            """);

        // user_context without authorize — still sets context if user is authenticated
        File.WriteAllText(Path.Combine(Dir, "sf_get_user_context_no_auth.sql"), """
            -- @user_context
            select
                coalesce(current_setting('request.user_id', true), '') as user_id,
                coalesce(current_setting('request.user_name', true), '') as user_name;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class SqlFileUserContextTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task UserContext_SetsClaimsAsSettings()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");

        using var response = await client.GetAsync("/api/sf-get-user-context");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("user123");
        content.Should().Contain("user");
    }

    [Fact]
    public async Task UserContext_Unauthorized_Without_Login()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/sf-get-user-context");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UserContext_NoAuth_LoggedIn_SetsContext()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");

        using var response = await client.GetAsync("/api/sf-get-user-context-no-auth");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("user123");
        content.Should().Contain("user");
    }
}
