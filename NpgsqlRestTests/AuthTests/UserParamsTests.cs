namespace NpgsqlRestTests;

public static partial class Database
{
    public static void UserParamsTests()
    {
        script.Append("""
        create function user_params_login() 
        returns table (
            name_identifier int,
            name text,
            role text[]
        )
        language sql as $$
        select
            123 as name_identifier,
            'myname' as name,
            array['admin', 'user', 'contains"quote'] as role
        $$;
        comment on function user_params_login() is 'login';
        
        create function get_user_params(
            _user_id text,
            _user_name text,
            _user_roles text[]
        ) 
        returns table (
            name_identifier int,
            name text,
            role text[]
        )
        language sql as $$
        select
            _user_id::int,
            _user_name,
            _user_roles
        $$;
        comment on function get_user_params(text, text, text[]) is '
        authorize
        user_params
        ';

        create function get_user_params_unauthorized(
            _user_id text = 456,
            _user_name text = 'notmyname',
            _user_roles text[] = array[]::text[]
        ) 
        returns table (
            name_identifier int,
            name text,
            role text[]
        )
        language sql as $$
        select
            _user_id::int,
            _user_name,
            _user_roles
        $$;
        comment on function get_user_params_unauthorized(text, text, text[]) is '
        user_params
        ';

        create function post_user_params_unauthorized(
            _user_id text = 456,
            _user_name text = 'notmyname',
            _user_claims json = null
        )
        returns table (
            name_identifier int,
            name text,
            claims json
        )
        language sql as $$
        select
            _user_id::int,
            _user_name,
            _user_claims
        $$;
        comment on function post_user_params_unauthorized(text, text, json) is '
        HTTP POST
        user_params
        ';

        create function get_user_ip_and_full_claims(
            _ip_address text,
            _user_claims json
        )
        returns table (
            ip_address text,
            user_claims json
        )
        language sql as $$
        select
            _ip_address,
            _user_claims
        $$;
        comment on function get_user_ip_and_full_claims(text, json) is '
        authorize
        user_params
        ';
        
        create function user_params_login_with_nulls() 
        returns table (
            name_identifier int,
            name text, 
            role text[]
        )
        language sql as $$
        select
            123 as name_identifier,
            null::text as name,
            null::text[] as role
        $$;
        comment on function user_params_login_with_nulls() is 'login';
        """);
    }
}

[Collection("TestFixture")]
public class UserParamsTests(TestFixture test)
{
    [Fact]
    public async Task Test_get_user_ip_and_full_claims1()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var login = await client.PostAsync("/api/user-params-login/", null);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        using var response = await client.GetAsync("/api/get-user-ip-and-full-claims/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();

        content.Should().Be("[{\"ipAddress\":null,\"userClaims\":{\"name_identifier\":\"123\",\"name\":\"myname\",\"role\":[\"admin\",\"user\",\"contains\\\"quote\"]}}]");
    }

    [Fact]
    public async Task Test_get_user_params1()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var login = await client.PostAsync("/api/user-params-login/", null);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        using var response = await client.GetAsync("/api/get-user-params/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("[{\"nameIdentifier\":123,\"name\":\"myname\",\"role\":[\"admin\",\"user\",\"contains\\\"quote\"]}]");
    }

    [Fact]
    public async Task Test_get_user_params_unauthorized1()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var response = await client.GetAsync("/api/get-user-params-unauthorized/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("[{\"nameIdentifier\":456,\"name\":\"notmyname\",\"role\":[]}]");
    }
    
    // Security regression: anonymous callers must not be able to supply claim-bound parameters
    // (query string) - the request value is ignored and the routine default applies.
    [Fact]
    public async Task Test_get_user_params_unauthorized_ignores_client_values()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var response = await client.GetAsync("/api/get-user-params-unauthorized/?userId=1&userName=admin");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("[{\"nameIdentifier\":456,\"name\":\"notmyname\",\"role\":[]}]");
    }

    // Security regression: same for the JSON body, including the claims JSON parameter.
    [Fact]
    public async Task Test_post_user_params_unauthorized_ignores_client_values()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var body = new StringContent(
            "{\"userId\":\"1\",\"userName\":\"admin\",\"userClaims\":{\"role\":\"admin\"}}", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/post-user-params-unauthorized/", body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("[{\"nameIdentifier\":456,\"name\":\"notmyname\",\"claims\":null}]");
    }

    [Fact]
    public async Task Test_get_user_params_with_nulls1()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var login = await client.PostAsync("/api/user-params-login-with-nulls/", null);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        using var response = await client.GetAsync("/api/get-user-params/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("[{\"nameIdentifier\":123,\"name\":null,\"role\":null}]");
    }
}