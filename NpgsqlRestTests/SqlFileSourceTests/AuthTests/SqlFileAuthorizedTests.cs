namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void SqlFileAuthorizedTests()
    {
        // Authorized — requires authentication
        File.WriteAllText(Path.Combine(Dir, "sf_authorized.sql"), """
            -- @authorize
            select 'authorized' as result;
            """);

        // Authorized with specific role that doesn't match any user role
        File.WriteAllText(Path.Combine(Dir, "sf_authorized_roles1.sql"), """
            -- @authorize test_role
            select 'roles1' as result;
            """);

        // Authorized with roles — one matches (role1)
        File.WriteAllText(Path.Combine(Dir, "sf_authorized_roles2.sql"), """
            -- @authorize test_role, role1
            select 'roles2' as result;
            """);

        // Authorized with roles — role1 matches among duplicates
        File.WriteAllText(Path.Combine(Dir, "sf_authorized_roles3.sql"), """
            -- @authorize test_role1 role1 test_role2 test_role1
            select 'roles3' as result;
            """);

        // Authorized with roles — none match
        File.WriteAllText(Path.Combine(Dir, "sf_authorized_roles4.sql"), """
            -- @authorize test_role1 test_role2 test_role3
            select 'roles4' as result;
            """);

        // Authorized by user name — matches "user"
        File.WriteAllText(Path.Combine(Dir, "sf_authorized_by_name.sql"), """
            -- @authorize user
            select 'by_name' as result;
            """);

        // Authorized by user name — wrong name
        File.WriteAllText(Path.Combine(Dir, "sf_authorized_by_name_wrong.sql"), """
            -- @authorize wrong_user
            select 'by_name_wrong' as result;
            """);

        // Authorized by user ID — matches "user123"
        File.WriteAllText(Path.Combine(Dir, "sf_authorized_by_userid.sql"), """
            -- @authorize user123
            select 'by_userid' as result;
            """);

        // Authorized by user ID — wrong ID
        File.WriteAllText(Path.Combine(Dir, "sf_authorized_by_userid_wrong.sql"), """
            -- @authorize wrong_id
            select 'by_userid_wrong' as result;
            """);

        // Authorized mixed — wrong role but matching user ID
        File.WriteAllText(Path.Combine(Dir, "sf_authorized_mixed.sql"), """
            -- @authorize wrong_role, user123
            select 'mixed' as result;
            """);

        // Allow anonymous — explicitly public
        File.WriteAllText(Path.Combine(Dir, "sf_allow_anonymous.sql"), """
            -- @allow_anonymous
            select 'anonymous_ok' as result;
            """);

        // No auth annotation — should be accessible without auth
        File.WriteAllText(Path.Combine(Dir, "sf_no_auth.sql"), """
            select 'no_auth' as result;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class SqlFileAuthorizedTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task Authorized_Unauthorized_Without_Login()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/sf-authorized");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":401");
        content.Should().Contain("\"title\":\"Unauthorized\"");
    }

    [Fact]
    public async Task Authorized_Ok_After_Login()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");
        using var response = await client.GetAsync("/api/sf-authorized");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Authorized_Roles1_Forbidden_NoMatchingRole()
    {
        using var client = test.CreateClient();
        using var response1 = await client.GetAsync("/api/sf-authorized-roles1");
        response1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await client.GetAsync("/login");

        using var response2 = await client.GetAsync("/api/sf-authorized-roles1");
        response2.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var content = await response2.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":403");
        content.Should().Contain("\"title\":\"Forbidden\"");
    }

    [Fact]
    public async Task Authorized_Roles2_Ok_OneRoleMatches()
    {
        using var client = test.CreateClient();
        using var response1 = await client.GetAsync("/api/sf-authorized-roles2");
        response1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await client.GetAsync("/login");

        using var response2 = await client.GetAsync("/api/sf-authorized-roles2");
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Authorized_Roles3_Ok_Role1Matches()
    {
        using var client = test.CreateClient();
        using var response1 = await client.GetAsync("/api/sf-authorized-roles3");
        response1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await client.GetAsync("/login");

        using var response2 = await client.GetAsync("/api/sf-authorized-roles3");
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Authorized_Roles4_Forbidden_NoMatchingRoles()
    {
        using var client = test.CreateClient();
        using var response1 = await client.GetAsync("/api/sf-authorized-roles4");
        response1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await client.GetAsync("/login");

        using var response2 = await client.GetAsync("/api/sf-authorized-roles4");
        response2.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var content = await response2.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":403");
        content.Should().Contain("\"title\":\"Forbidden\"");
    }

    [Fact]
    public async Task Authorized_ByName_Ok()
    {
        using var client = test.CreateClient();
        using var response1 = await client.GetAsync("/api/sf-authorized-by-name");
        response1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await client.GetAsync("/login");

        using var response2 = await client.GetAsync("/api/sf-authorized-by-name");
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Authorized_ByName_Wrong_Forbidden()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");
        using var response = await client.GetAsync("/api/sf-authorized-by-name-wrong");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authorized_ByUserId_Ok()
    {
        using var client = test.CreateClient();
        using var response1 = await client.GetAsync("/api/sf-authorized-by-userid");
        response1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await client.GetAsync("/login");

        using var response2 = await client.GetAsync("/api/sf-authorized-by-userid");
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Authorized_ByUserId_Wrong_Forbidden()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");
        using var response = await client.GetAsync("/api/sf-authorized-by-userid-wrong");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authorized_Mixed_WrongRole_MatchingUserId_Ok()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");
        using var response = await client.GetAsync("/api/sf-authorized-mixed");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
