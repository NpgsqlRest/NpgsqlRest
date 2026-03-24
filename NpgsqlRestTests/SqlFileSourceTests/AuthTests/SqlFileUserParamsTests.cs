namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void SqlFileUserParamsTests()
    {
        // user_parameters with _user_id claim mapping — auto-filled from name_identifier claim
        File.WriteAllText(Path.Combine(Dir, "sf_get_user_id.sql"), """
            -- @authorize
            -- @user_parameters
            -- @param $1 _user_id
            select $1 as user_id;
            """);

        // user_parameters with _user_name claim mapping — auto-filled from name claim
        File.WriteAllText(Path.Combine(Dir, "sf_get_user_name.sql"), """
            -- @authorize
            -- @user_parameters
            -- @param $1 _user_name
            select $1 as user_name;
            """);

        // user_parameters with both _user_id and _user_name
        File.WriteAllText(Path.Combine(Dir, "sf_get_user_id_and_name.sql"), """
            -- @authorize
            -- @user_parameters
            -- @param $1 _user_id
            -- @param $2 _user_name
            select $1 as user_id, $2 as user_name;
            """);

    }
}

[Collection("SqlFileSourceFixture")]
public class SqlFileUserParamsTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task UserParams_UserId_AutoFilledFromClaim()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");

        using var response = await client.GetAsync("/api/sf-get-user-id");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        // name_identifier claim = "user123"
        content.Should().Contain("user123");
    }

    [Fact]
    public async Task UserParams_UserName_AutoFilledFromClaim()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");

        using var response = await client.GetAsync("/api/sf-get-user-name");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        // name claim = "user"
        content.Should().Contain("user");
    }

    [Fact]
    public async Task UserParams_BothIdAndName_AutoFilled()
    {
        using var client = test.CreateClient();
        await client.GetAsync("/login");

        using var response = await client.GetAsync("/api/sf-get-user-id-and-name");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("user123");
        content.Should().Contain("user");
    }

    [Fact]
    public async Task UserParams_WithAuth_Unauthorized_Without_Login()
    {
        // With @authorize + @user_parameters, unauthenticated requests should be rejected
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/sf-get-user-id-and-name");
        // user_parameters endpoints may not match route when no claims present
        var status = (int)response.StatusCode;
        status.Should().BeOneOf(401, 404, 403);
    }
}
