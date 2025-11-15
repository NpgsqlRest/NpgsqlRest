namespace NpgsqlRestTests;

public static partial class Database
{
    public static void AuthLoginTests()
    {
        script.Append("""
        create function custom_authorize1() returns text language sql as 'select ''authorized''';
        comment on function custom_authorize1() is 'authorize';

        create function custom_login1() 
        returns table (
            name_identifier int,
            name text,
            role text[]
        )
        language sql as $$
        select
            123 as name_identifier,
            'username' as name,
            array['role1', 'role2'] as role
        $$;
        comment on function custom_login1() is 'login';

        create function custom_authorize1_role2_role3() returns text language sql as 'select ''authorized role2, role3''';
        comment on function custom_authorize1_role2_role3() is 'authorize role2, role3';

        create function custom_authorize1_rolex_roley() returns text language sql as 'select ''authorized rolex, roley''';
        comment on function custom_authorize1_rolex_roley() is 'authorize rolex, roley';

        create function custom_login2() 
        returns table (
            status smallint,
            name text
        )
        language sql as $$
        select
            406 as status, 'username' as name
        $$;
        comment on function custom_login2() is 'login';

        create function custom_login3() 
        returns table (
            status boolean,
            name text
        )
        language sql as $$
        select
            false as status, 'username' as name
        $$;
        comment on function custom_login3() is 'login';

        create function custom_logout() returns void language sql as '';
        comment on function custom_logout() is 'logout';

        create function custom_login4() 
        returns table (
            name text
        )
        language plpgsql
        as 
        $$
        begin
            return;
        end;
        $$;

        create function custom_login5() 
        returns table (
            name text
        )
        language plpgsql
        as 
        $$
        begin
            return query select 'name';
        end;
        $$;

        comment on function custom_login4() is 'login';
        comment on function custom_login5() is 'login';

        create function custom_login6() 
        returns table (
            name_identifier int,
            name text,
            body text
        )
        language sql as $$
        select
            123 as name_identifier,
            'username' as name,
            'some message' as body
        $$;
        comment on function custom_login6() is 'login';
        
        """);
    }
}

[Collection("TestFixture")]
public class AuthLoginTests(TestFixture test)
{
    [Fact]
    public async Task Test_custom_login1()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var response1 = await client.PostAsync("/api/custom-authorize1/", null);
        response1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var login = await client.PostAsync(requestUri: "/api/custom-login1/", null);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginContent = await login.Content.ReadAsStringAsync();
        //loginContent.Should().Be("[{\"nameIdentifier\":123,\"name\":\"username\",\"role\":[\"role1\",\"role2\"]}]");
        loginContent.Should().BeEmpty();

        using var response2 = await client.PostAsync("/api/custom-authorize1/", null);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        using var response3 = await client.PostAsync("/api/custom-authorize1-role2-role3/", null);
        response3.StatusCode.Should().Be(HttpStatusCode.OK);

        using var response4 = await client.PostAsync("/api/custom-authorize1-rolex-roley/", null);
        response4.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Test_custom_login2()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var response1 = await client.PostAsync("/api/custom-authorize1/", null);
        response1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var login = await client.PostAsync(requestUri: "/api/custom-login2/", null);
        ((int)login.StatusCode).Should().Be(406);

        using var response2 = await client.PostAsync("/api/custom-authorize1/", null);
        response2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Test_custom_login3()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var response1 = await client.PostAsync("/api/custom-authorize1/", null);
        response1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var login = await client.PostAsync(requestUri: "/api/custom-login3/", null);
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var response2 = await client.PostAsync("/api/custom-authorize1/", null);
        response2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Test_custom_login1_custom_logout()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var response1 = await client.PostAsync("/api/custom-authorize1/", null);
        response1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var login = await client.PostAsync(requestUri: "/api/custom-login1/", null);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        using var response2 = await client.PostAsync("/api/custom-authorize1/", null);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        using var logout = await client.PostAsync(requestUri: "/api/custom-logout/", null);
        logout.StatusCode.Should().Be(HttpStatusCode.OK);

        using var response3 = await client.PostAsync("/api/custom-authorize1/", null);
        response3.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Test_custom_login_4_and_5()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var response1 = await client.PostAsync("/api/custom-authorize1/", null);
        response1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var login1 = await client.PostAsync(requestUri: "/api/custom-login4/", null);
        login1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var response2 = await client.PostAsync("/api/custom-authorize1/", null);
        response2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var login2 = await client.PostAsync(requestUri: "/api/custom-login5/", null);
        login2.StatusCode.Should().Be(HttpStatusCode.OK);

        using var response3 = await client.PostAsync("/api/custom-authorize1/", null);
        response3.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Test_custom_login6()
    {
        using var client = test.Application.CreateClient();
        client.Timeout = TimeSpan.FromHours(1);

        using var response1 = await client.PostAsync("/api/custom-authorize1/", null);
        response1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var login = await client.PostAsync(requestUri: "/api/custom-login6/", null);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginContent = await login.Content.ReadAsStringAsync();
        loginContent.Should().Be("some message");

        using var response2 = await client.PostAsync("/api/custom-authorize1/", null);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}