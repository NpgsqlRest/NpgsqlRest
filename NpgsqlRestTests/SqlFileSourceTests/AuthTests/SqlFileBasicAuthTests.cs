using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void SqlFileBasicAuthTests()
    {
        // Basic auth with hardcoded credentials
        // npgsqlrest basic_auth my_name my_password → hash: Myb55+6lW6iiUOI3opLkysOaS8J0NNIuQ+qE2SGaKs3r62ngDJROrhX75+zmLC7t
        File.WriteAllText(Path.Combine(Dir, "sql_basic_auth.sql"), """
            -- @basic_auth my_name Myb55+6lW6iiUOI3opLkysOaS8J0NNIuQ+qE2SGaKs3r62ngDJROrhX75+zmLC7t
            select 'basic_auth_ok' as result;
            """);

        // Basic auth with no credentials (should always fail)
        File.WriteAllText(Path.Combine(Dir, "sql_basic_auth_no_creds.sql"), """
            -- @basic_auth
            select 'no_creds' as result;
            """);

        // Basic auth with multiple users
        // user1/pass1 = um4K594nL6pBQx2el0lcbKKLADof1k9atRYKy+G14f6BQPtSCkwO6qz1wJ1d9Tx/
        // user2/pass2 = TIDVxenk9gSqApyI82XDuqUaigQ5OdBIecfRtq7wFWtHT3Ffx2s+noIjvFCAw90z
        File.WriteAllText(Path.Combine(Dir, "sql_basic_auth_multi.sql"), """
            -- @basic_auth user1 um4K594nL6pBQx2el0lcbKKLADof1k9atRYKy+G14f6BQPtSCkwO6qz1wJ1d9Tx/
            -- @basic_auth user2 TIDVxenk9gSqApyI82XDuqUaigQ5OdBIecfRtq7wFWtHT3Ffx2s+noIjvFCAw90z
            select 'multi_user_ok' as result;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class SqlFileBasicAuthTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task BasicAuth_ValidCredentials_Ok()
    {
        using var client = test.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sql-basic-auth");
        // my_name:my_password → Base64: bXlfbmFtZTpteV9wYXNzd29yZA==
        request.Headers.Add("Authorization", "Basic bXlfbmFtZTpteV9wYXNzd29yZA==");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("basic_auth_ok");
    }

    [Fact]
    public async Task BasicAuth_NoHeader_Unauthorized()
    {
        using var client = test.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sql-basic-auth");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BasicAuth_InvalidBase64_Unauthorized()
    {
        using var client = test.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sql-basic-auth");
        request.Headers.Add("Authorization", "Basic XXX");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BasicAuth_WrongPassword_Unauthorized()
    {
        using var client = test.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sql-basic-auth");
        // my_name:wrong → Base64: bXlfbmFtZTp3cm9uZw==
        request.Headers.Add("Authorization", "Basic bXlfbmFtZTp3cm9uZw==");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BasicAuth_WrongUsername_Unauthorized()
    {
        using var client = test.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sql-basic-auth");
        // wrong:my_password → Base64: d3Jvbmc6bXlfcGFzc3dvcmQ=
        request.Headers.Add("Authorization", "Basic d3Jvbmc6bXlfcGFzc3dvcmQ=");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BasicAuth_NoCreds_AlwaysUnauthorized()
    {
        using var client = test.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sql-basic-auth-no-creds");
        request.Headers.Add("Authorization", "Basic bXlfbmFtZTpteV9wYXNzd29yZA==");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BasicAuth_MultiUser_User1_Ok()
    {
        using var client = test.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sql-basic-auth-multi");
        // user1:pass1 → Base64: dXNlcjE6cGFzczE=
        request.Headers.Add("Authorization", "Basic dXNlcjE6cGFzczE=");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BasicAuth_MultiUser_User2_Ok()
    {
        using var client = test.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sql-basic-auth-multi");
        // user2:pass2 → Base64: dXNlcjI6cGFzczI=
        request.Headers.Add("Authorization", "Basic dXNlcjI6cGFzczI=");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BasicAuth_MultiUser_User3_Unauthorized()
    {
        using var client = test.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sql-basic-auth-multi");
        // user3:pass3 → Base64: dXNlcjM6cGFzczM=
        request.Headers.Add("Authorization", "Basic dXNlcjM6cGFzczM=");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BasicAuth_MultiUser_CrossPassword_Unauthorized()
    {
        using var client = test.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sql-basic-auth-multi");
        // user2:pass3 → Base64: dXNlcjI6cGFzczM=
        request.Headers.Add("Authorization", "Basic dXNlcjI6cGFzczM=");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
