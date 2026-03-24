namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void SqlFileAuthMultiCommandTests()
    {
        // Authorized multi-command — two selects behind auth
        File.WriteAllText(Path.Combine(Dir, "sf_auth_multi_cmd.sql"), """
            -- @authorize
            -- @param $1 id
            select name from sql_describe_test where id = $1;
            select count(*) as total from sql_describe_test;
            """);

        // Authorized multi-command with role — role1 matches
        File.WriteAllText(Path.Combine(Dir, "sf_auth_multi_cmd_role.sql"), """
            -- @authorize role1
            -- @param $1 id
            select id, name from sql_describe_test where id = $1;
            select count(*) as total from sql_describe_test;
            """);

        // Authorized multi-command with wrong role — should be forbidden
        File.WriteAllText(Path.Combine(Dir, "sf_auth_multi_cmd_wrong_role.sql"), """
            -- @authorize nonexistent_role
            -- @param $1 id
            select name from sql_describe_test where id = $1;
            select count(*) as total from sql_describe_test;
            """);

        // Multi-command with user_parameters
        File.WriteAllText(Path.Combine(Dir, "sf_auth_multi_cmd_user_params.sql"), """
            -- @authorize
            -- @user_parameters
            -- @param $1 _user_id
            select $1 as user_id;
            select now() as server_time;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class SqlFileAuthMultiCommandTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task MultiCommand_Unauthorized_Without_Login()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/sf-auth-multi-cmd?id=1");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MultiCommand_Ok_After_Login()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");
        using var response = await client.GetAsync("/api/sf-auth-multi-cmd?id=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("result1");
        content.Should().Contain("result2");
    }

    [Fact]
    public async Task MultiCommand_WithMatchingRole_Ok()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");
        using var response = await client.GetAsync("/api/sf-auth-multi-cmd-role?id=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("result1");
        content.Should().Contain("result2");
    }

    [Fact]
    public async Task MultiCommand_WithWrongRole_Forbidden()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");
        using var response = await client.GetAsync("/api/sf-auth-multi-cmd-wrong-role?id=1");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MultiCommand_WithUserParams_ClaimsAutoFilled()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");
        using var response = await client.GetAsync("/api/sf-auth-multi-cmd-user-params");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("user123");
        content.Should().Contain("result1");
        content.Should().Contain("result2");
    }
}
