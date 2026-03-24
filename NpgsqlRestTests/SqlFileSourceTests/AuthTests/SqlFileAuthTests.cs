using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void SqlFileAuthTests()
    {
        // Authorized — requires authentication
        File.WriteAllText(Path.Combine(Dir, "auth_required.sql"), """
            -- @authorize
            select 'authorized' as result;
            """);

        // Authorized with specific role
        File.WriteAllText(Path.Combine(Dir, "auth_role_match.sql"), """
            -- @authorize role1
            select 'role_match' as result;
            """);

        // Authorized with non-matching role
        File.WriteAllText(Path.Combine(Dir, "auth_role_no_match.sql"), """
            -- @authorize nonexistent_role
            select 'role_no_match' as result;
            """);

        // Authorized with multiple roles (one matches)
        File.WriteAllText(Path.Combine(Dir, "auth_multi_role.sql"), """
            -- @authorize wrong_role, role2
            select 'multi_role' as result;
            """);

        // Authorized by user name
        File.WriteAllText(Path.Combine(Dir, "auth_by_name.sql"), """
            -- @authorize user
            select 'by_name' as result;
            """);

        // Authorized by user name — wrong name
        File.WriteAllText(Path.Combine(Dir, "auth_by_name_wrong.sql"), """
            -- @authorize wrong_user
            select 'by_name_wrong' as result;
            """);

        // Authorized by user ID
        File.WriteAllText(Path.Combine(Dir, "auth_by_userid.sql"), """
            -- @authorize user123
            select 'by_userid' as result;
            """);

        // Authorized by user ID — wrong ID
        File.WriteAllText(Path.Combine(Dir, "auth_by_userid_wrong.sql"), """
            -- @authorize wrong_id
            select 'by_userid_wrong' as result;
            """);

        // Authorized mixed — wrong role but matching user ID
        File.WriteAllText(Path.Combine(Dir, "auth_mixed.sql"), """
            -- @authorize wrong_role, user123
            select 'mixed' as result;
            """);

        // Allow anonymous — explicitly public
        File.WriteAllText(Path.Combine(Dir, "auth_anonymous.sql"), """
            -- @allow_anonymous
            select 'anonymous_ok' as result;
            """);

        // No auth annotation — should be accessible without auth
        File.WriteAllText(Path.Combine(Dir, "auth_none.sql"), """
            select 'no_auth' as result;
            """);

        // Authorized multi-command
        File.WriteAllText(Path.Combine(Dir, "auth_multi_cmd.sql"), """
            -- @authorize
            -- @param $1 id
            select name from sql_describe_test where id = $1;
            select count(*) as total from sql_describe_test;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class SqlFileAuthTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task Auth_Required_Unauthorized_Without_Login()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/auth-required");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Auth_Required_Ok_After_Login()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");
        using var response = await client.GetAsync("/api/auth-required");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("authorized");
    }

    [Fact]
    public async Task Auth_Role_Match_Ok()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");
        using var response = await client.GetAsync("/api/auth-role-match");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Auth_Role_NoMatch_Forbidden()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");
        using var response = await client.GetAsync("/api/auth-role-no-match");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Auth_MultiRole_OneMatches_Ok()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");
        using var response = await client.GetAsync("/api/auth-multi-role");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Auth_ByName_Ok()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");
        using var response = await client.GetAsync("/api/auth-by-name");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Auth_ByName_Wrong_Forbidden()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");
        using var response = await client.GetAsync("/api/auth-by-name-wrong");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Auth_ByUserId_Ok()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");
        using var response = await client.GetAsync("/api/auth-by-userid");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Auth_ByUserId_Wrong_Forbidden()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");
        using var response = await client.GetAsync("/api/auth-by-userid-wrong");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Auth_Mixed_WrongRole_MatchingUserId_Ok()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");
        using var response = await client.GetAsync("/api/auth-mixed");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Auth_AllowAnonymous_Ok_Without_Login()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/auth-anonymous");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("anonymous_ok");
    }

    [Fact]
    public async Task Auth_NoAnnotation_Accessible_Without_Login()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/auth-none");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("no_auth");
    }

    [Fact]
    public async Task Auth_MultiCommand_Unauthorized_Without_Login()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/auth-multi-cmd?id=1");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Auth_MultiCommand_Ok_After_Login()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");
        using var response = await client.GetAsync("/api/auth-multi-cmd?id=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("result1");
        content.Should().Contain("result2");
    }
}
