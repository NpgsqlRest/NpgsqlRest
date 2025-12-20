namespace NpgsqlRestTests;

public static partial class Database
{
    public static void BasicAuthTests()
    {
        script.Append("""

        create function get_basic_auth_user(
            _user_name text = null --mapped to name claim
        ) 
        returns text
        language sql as $$
        select _user_name;
        $$;
        
        -- ❯ dotnet run -- hash my_password
        -- Myb55+6lW6iiUOI3opLkysOaS8J0NNIuQ+qE2SGaKs3r62ngDJROrhX75+zmLC7t
        
        comment on function get_basic_auth_user(text) is '
        basic_auth my_name Myb55+6lW6iiUOI3opLkysOaS8J0NNIuQ+qE2SGaKs3r62ngDJROrhX75+zmLC7t
        user_params
        ';
        
        create function get_basic_auth_no_creds(
            _user_name text = null --mapped to name claim
        ) 
        returns text
        language sql as $$
        select _user_name;
        $$;

        comment on function get_basic_auth_no_creds(text) is '
        basic_auth
        user_params
        ';
        
        create function get_basic_auth_challenge_command(
            _user_claims json -- mapped to claims
        ) 
        returns text
        language sql as $$
        select _user_claims;
        $$;

        comment on function get_basic_auth_challenge_command(json) is '
        basic_auth
        challenge_command = select * from auth_challenge_command($1, $2, $3, $4, $5)
        user_params
        ';
        
        create function get_basic_auth_challenge_command_pass(
            _user_claims json -- mapped to claims
        ) 
        returns text
        language sql as $$
        select _user_claims;
        $$;

        comment on function get_basic_auth_challenge_command_pass(json) is '
        basic_auth my_name Myb55+6lW6iiUOI3opLkysOaS8J0NNIuQ+qE2SGaKs3r62ngDJROrhX75+zmLC7t
        challenge_command = select * from auth_challenge_command($1, $2, $3, $4, $5)
        user_params
        ';
        
        create function auth_challenge_command(
            _user text,
            _password text,
            _valid boolean,
            _realm text,
            _path text
        ) 
        returns table (
            name_identifier int,
            name text,
            password text,
            valid boolean,
            realm text,
            path text
        )
        language sql as $$
        select 1, _user, _password, _valid, _realm, _path;
        $$;
        
        create function get_basic_auth_challenge_command_failed(
             _user_claims json -- mapped to claims
         ) 
         returns text
         language sql as $$
         select _user_claims;
         $$;

         comment on function get_basic_auth_challenge_command_failed(json) is '
         basic_auth
         challenge_command = select * from auth_challenge_command_failed($1, $2, $3, $4, $5)
         user_params
         ';
         
         create function auth_challenge_command_failed(
             _user text,
             _password text,
             _valid boolean,
             _realm text,
             _path text
         ) 
         returns table (
             status boolean,
             name_identifier int,
             name text,
             password text,
             valid boolean,
             realm text,
             path text
         )
         language sql as $$
         select false, 1, _user, _password, _valid, _realm, _path;
         $$;
         
         create function get_basic_auth_multiple_users(
            _user_name text = null --mapped to name claim
        ) 
        returns text
        language sql as $$
        select _user_name;
        $$;

        -- user1/pass1 = um4K594nL6pBQx2el0lcbKKLADof1k9atRYKy+G14f6BQPtSCkwO6qz1wJ1d9Tx/
        -- user2/pass2 = TIDVxenk9gSqApyI82XDuqUaigQ5OdBIecfRtq7wFWtHT3Ffx2s+noIjvFCAw90z
        comment on function get_basic_auth_multiple_users(text) is '
        basic_auth user1 um4K594nL6pBQx2el0lcbKKLADof1k9atRYKy+G14f6BQPtSCkwO6qz1wJ1d9Tx/
        basic_auth user2 TIDVxenk9gSqApyI82XDuqUaigQ5OdBIecfRtq7wFWtHT3Ffx2s+noIjvFCAw90z
        user_params
        ';
""");
    }
}

[Collection("TestFixture")]
public class BasicAuthTests(TestFixture test)
{
    [Fact]
    public async Task Test_get_basic_auth_user_test1()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/get-basic-auth-user");
        // ❯ npgsqlrest basic_auth my_name my_password
        // Authorization: Basic bXlfbmFtZTpteV9wYXNzd29yZA==
        request.Headers.Add("Authorization", "Basic bXlfbmFtZTpteV9wYXNzd29yZA==");
        
        using var result = await test.Client.SendAsync(request);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("my_name");
    }
    
    [Fact]
    public async Task Test_get_basic_auth_user_test2()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/get-basic-auth-user");
        request.Headers.Add("Authorization", "Basic XXX");

        using var result = await test.Client.SendAsync(request);
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var content = await result.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":401");
        content.Should().Contain("\"title\":\"Unauthorized\"");
    }

    [Fact]
    public async Task Test_get_basic_auth_user_test3()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/get-basic-auth-user");
        using var result = await test.Client.SendAsync(request);
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var content = await result.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":401");
        content.Should().Contain("\"title\":\"Unauthorized\"");
    }

    [Fact]
    public async Task Test_get_basic_auth_user_test4()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/get-basic-auth-user");
        request.Headers.Add("Authorization", "Basic eHh4"); // xxx
        using var result = await test.Client.SendAsync(request);
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var content = await result.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":401");
        content.Should().Contain("\"title\":\"Unauthorized\"");
    }

    [Fact]
    public async Task Test_get_basic_auth_invalid_username()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/get-basic-auth-user");
        request.Headers.Add("Authorization", "Basic eHh4Onl5eQ=="); // xxx:yyy
        using var result = await test.Client.SendAsync(request);
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var content = await result.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":401");
        content.Should().Contain("\"title\":\"Unauthorized\"");
    }

    [Fact]
    public async Task Test_get_basic_auth_invalid_password()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/get-basic-auth-user");
        request.Headers.Add("Authorization", "Basic bXlfbmFtZTp5eXk="); // my_name:yyy
        using var result = await test.Client.SendAsync(request);
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var content = await result.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":401");
        content.Should().Contain("\"title\":\"Unauthorized\"");
    }

    [Fact]
    public async Task Test_get_basic_auth_no_creds()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/get-basic-auth-no-creds");
        // ❯ npgsqlrest basic_auth my_name my_password
        // Authorization: Basic bXlfbmFtZTpteV9wYXNzd29yZA==
        request.Headers.Add("Authorization", "Basic bXlfbmFtZTpteV9wYXNzd29yZA=="); // valid creds but no creds set in function
        using var result = await test.Client.SendAsync(request);
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var content = await result.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":401");
        content.Should().Contain("\"title\":\"Unauthorized\"");
    }

    [Fact]
    public async Task Test_get_basic_auth_challenge_command_invalid_request1()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/get-basic-auth-challenge-command");
        using var result = await test.Client.SendAsync(request);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Should().Contain("\"status\":401");
        response.Should().Contain("\"title\":\"Unauthorized\"");
    }

    [Fact]
    public async Task Test_get_basic_auth_challenge_command_invalid_request2()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/get-basic-auth-challenge-command");
        request.Headers.Add("Authorization", "Basic eHh4"); // xxx
        using var result = await test.Client.SendAsync(request);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Should().Contain("\"status\":401");
        response.Should().Contain("\"title\":\"Unauthorized\"");
    }
    
    [Fact]
    public async Task Test_get_basic_auth_challenge_command_valid_request()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/get-basic-auth-challenge-command");
        request.Headers.Add("Authorization", "Basic eHh4Onl5eQ=="); // xxx:yyy
        using var result = await test.Client.SendAsync(request);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("{\"name_identifier\":\"1\",\"name\":\"xxx\",\"password\":\"yyy\",\"valid\":null,\"realm\":\"NpgsqlRest\",\"path\":\"/api/get-basic-auth-challenge-command\"}"); 
    }
    
    [Fact]
    public async Task Test_get_basic_auth_challenge_command_valid_request_valid_pass()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/get-basic-auth-challenge-command-pass");
        request.Headers.Add("Authorization", "Basic bXlfbmFtZTpteV9wYXNzd29yZA=="); // xxx:yyy
        using var result = await test.Client.SendAsync(request);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("{\"name_identifier\":\"1\",\"name\":\"my_name\",\"password\":\"my_password\",\"valid\":\"True\",\"realm\":\"NpgsqlRest\",\"path\":\"/api/get-basic-auth-challenge-command-pass\"}"); 
    }
    
    [Fact]
    public async Task Test_get_basic_auth_challenge_command_failed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/get-basic-auth-challenge-command-failed");
        request.Headers.Add("Authorization", "Basic eHh4Onl5eQ=="); // xxx:yyy
        using var result = await test.Client.SendAsync(request);
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var content = await result.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":401");
        content.Should().Contain("\"title\":\"Unauthorized\"");
    }

    [Fact]
    public async Task Test_get_basic_auth_multiple_users_test1()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/get-basic-auth-multiple-users");
        request.Headers.Add("Authorization", "Basic dXNlcjE6cGFzczE="); // user1:pass1
        using var result = await test.Client.SendAsync(request);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("user1");
    }
    
    [Fact]
    public async Task Test_get_basic_auth_multiple_users_test2()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/get-basic-auth-multiple-users");
        request.Headers.Add("Authorization", "Basic dXNlcjI6cGFzczI="); // user2:pass2
        using var result = await test.Client.SendAsync(request);
        var response = await result.Content.ReadAsStringAsync();
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Should().Be("user2");
    }
    
    [Fact]
    public async Task Test_get_basic_auth_multiple_users_test3()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/get-basic-auth-multiple-users");
        request.Headers.Add("Authorization", "Basic dXNlcjM6cGFzczM="); // user3:pass3
        using var result = await test.Client.SendAsync(request);
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var content = await result.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":401");
        content.Should().Contain("\"title\":\"Unauthorized\"");
    }

    [Fact]
    public async Task Test_get_basic_auth_multiple_users_test4()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/get-basic-auth-multiple-users");
        request.Headers.Add("Authorization", "Basic dXNlcjI6cGFzczM="); // user2:pass3
        using var result = await test.Client.SendAsync(request);
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var content = await result.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":401");
        content.Should().Contain("\"title\":\"Unauthorized\"");
    }

    [Fact]
    public async Task Test_get_basic_auth_multiple_users_test5()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/get-basic-auth-multiple-users");
        request.Headers.Add("Authorization", "Basic dXNlcjM6cGFzczI="); // user3:pass2
        using var result = await test.Client.SendAsync(request);
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var content = await result.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":401");
        content.Should().Contain("\"title\":\"Unauthorized\"");
    }
}

