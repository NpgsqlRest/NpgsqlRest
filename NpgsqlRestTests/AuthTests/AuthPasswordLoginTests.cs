using Npgsql;
using NpgsqlRest.Auth;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void AuthPasswordLoginTests()
    {
        script.Append("""
        create function password_protected_login1(
            _pass text,
            _hashed text
        ) 
        returns table (
            name_identifier int,
            name text,
            body text,
            hash text
        )
        language sql as $$
        select
            999 as name_identifier,
            'passwordprotected' as name, 
            'passwordprotected is succesuful' as body,
            _hashed
        $$;
        comment on function password_protected_login1(text,text) is 'login';

        create function password_protected_login2(
            _pass text
        ) 
        returns table (
            name_identifier int,
            name text,
            body text,
            hash text
        )
        language sql as $$
        select
            999 as name_identifier,
            'passwordprotected' as name, 
            'passwordprotected is succesuful' as body,
            'cM+KumWip708pxDiNQJHt/8rU4iECVbm4XeBRcjWP9R8k6UTVDfTAwN6wWT65rYn'
        $$;
        comment on function password_protected_login2(text) is 'login';

        -- regression: login must fail when the stored hash is NULL (e.g. an account without a password)
        create function password_protected_login_null_hash(
            _pass text
        )
        returns table (
            name_identifier int,
            name text,
            body text,
            hash text
        )
        language sql as $$
        select
            999 as name_identifier,
            'passwordprotected' as name,
            'passwordprotected is succesuful' as body,
            null::text
        $$;
        comment on function password_protected_login_null_hash(text) is 'login';

        -- regression: login must fail when the password parameter has a default and the client omits it
        create function password_protected_login_default_pass(
            _user text,
            _pass text default null
        )
        returns table (
            name_identifier int,
            name text,
            body text,
            hash text
        )
        language sql as $$
        select
            999 as name_identifier,
            'passwordprotected' as name,
            'passwordprotected is succesuful' as body,
            'cM+KumWip708pxDiNQJHt/8rU4iECVbm4XeBRcjWP9R8k6UTVDfTAwN6wWT65rYn'
        $$;
        comment on function password_protected_login_default_pass(text,text) is 'login';

        -- regression: login must fail when no parameter name matches PasswordParameterNameContains ("pass")
        create function password_protected_login_no_pass_param(
            _secret text
        )
        returns table (
            name_identifier int,
            name text,
            body text,
            hash text
        )
        language sql as $$
        select
            999 as name_identifier,
            'passwordprotected' as name,
            'passwordprotected is succesuful' as body,
            'cM+KumWip708pxDiNQJHt/8rU4iECVbm4XeBRcjWP9R8k6UTVDfTAwN6wWT65rYn'
        $$;
        comment on function password_protected_login_no_pass_param(text) is 'login';

        create table failed_logins(scheme text, user_id text, user_name text);
        create table succeeded_logins(scheme text, user_id text, user_name text);

        create procedure failed_login(
            _scheme text,
            _user_id text,
            _user_name text
        )
        language sql as $$
        insert into failed_logins(scheme, user_id, user_name) values (_scheme, _user_id, _user_name);
        $$;

        create procedure succeeded_login(
            _scheme text,
            _user_id text,
            _user_name text
        )
        language sql as $$
        insert into succeeded_logins(scheme, user_id, user_name) values (_scheme, _user_id, _user_name);
        $$;
        """);
    }
}

[Collection("TestFixture")]
public class AuthPasswordLoginTests(TestFixture test)
{
    [Fact]
    public async Task Test_password_protected_login1()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);
        var hasher = new PasswordHasher();

        var password = "MySecurePass123!";
        var hashed = hasher.HashPassword(password);

        using var content = new StringContent($"{{\"pass\": \"{password}\", \"hashed\": \"{hashed}\"}}", Encoding.UTF8, "application/json");
        using var login = await client.PostAsync(requestUri: "/api/password-protected-login1/", content);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginContent = await login.Content.ReadAsStringAsync();
        loginContent.Should().Be("passwordprotected is succesuful");
    }

    [Fact]
    public async Task Test_password_protected_login2()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        var password = "MySecurePass123!";

        using var content = new StringContent($"{{\"pass\": \"{password}\"}}", Encoding.UTF8, "application/json");
        using var login = await client.PostAsync(requestUri: "/api/password-protected-login2/", content);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginContent = await login.Content.ReadAsStringAsync();
        loginContent.Should().Be("passwordprotected is succesuful");
    }

    [Fact]
    public async Task Test_password_protected_login1_wrong_password()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);
        var hasher = new PasswordHasher();

        var password = "MySecurePass123!";
        var hashed = hasher.HashPassword("MySecurePass124!");

        using var content = new StringContent($"{{\"pass\": \"{password}\", \"hashed\": \"{hashed}\"}}", Encoding.UTF8, "application/json");
        using var login = await client.PostAsync(requestUri: "/api/password-protected-login1/", content);
        login.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var connection = Database.CreateConnection();
        await connection.OpenAsync();
        using var command = new NpgsqlCommand("select * from failed_logins", connection);
        using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue(); // there is a record

        reader.IsDBNull(0).Should().Be(true);
        reader.GetString(1).Should().Be("999"); // user id
        reader.GetString(2).Should().Be("passwordprotected"); // username
    }

    // Security regressions: a login result that contains the hash column must never sign the user in
    // unless the password was actually verified against it.

    [Fact]
    public async Task Test_password_protected_login_null_password_is_rejected()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var content = new StringContent("{\"pass\": null}", Encoding.UTF8, "application/json");
        using var login = await client.PostAsync(requestUri: "/api/password-protected-login2/", content);
        login.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await login.Content.ReadAsStringAsync()).Should().Be("{\"title\":\"Not Found\",\"status\":404}");
        login.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public async Task Test_password_protected_login_null_stored_hash_is_rejected()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var content = new StringContent("{\"pass\": \"anything\"}", Encoding.UTF8, "application/json");
        using var login = await client.PostAsync(requestUri: "/api/password-protected-login-null-hash/", content);
        login.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await login.Content.ReadAsStringAsync()).Should().Be("{\"title\":\"Not Found\",\"status\":404}");
        login.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public async Task Test_password_protected_login_omitted_default_password_is_rejected()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var content = new StringContent("{\"user\": \"admin\"}", Encoding.UTF8, "application/json");
        using var login = await client.PostAsync(requestUri: "/api/password-protected-login-default-pass/", content);
        login.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await login.Content.ReadAsStringAsync()).Should().Be("{\"title\":\"Not Found\",\"status\":404}");
        login.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public async Task Test_password_protected_login_without_password_parameter_is_rejected()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var content = new StringContent("{\"secret\": \"MySecurePass123!\"}", Encoding.UTF8, "application/json");
        using var login = await client.PostAsync(requestUri: "/api/password-protected-login-no-pass-param/", content);
        login.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await login.Content.ReadAsStringAsync()).Should().Be("{\"title\":\"Not Found\",\"status\":404}");
        login.Headers.Contains("Set-Cookie").Should().BeFalse();
    }
}