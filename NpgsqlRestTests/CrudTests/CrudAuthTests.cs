namespace NpgsqlRestTests;

public static partial class Database
{
    // Auth parity for plugin-generated CRUD endpoints: `authorize` (with and without roles) from the
    // TABLE comment must protect every generated variant exactly like it protects routine endpoints.
    public static void CrudAuthTests()
    {
        script.Append("""
        create table crud_auth_protected (
            id int primary key,
            name text
        );
        insert into crud_auth_protected values (1,'one'),(2,'two');

        comment on table crud_auth_protected is '
        authorize crud_role
        ';

        create table crud_auth_other_role (
            id int primary key,
            name text
        );
        insert into crud_auth_other_role values (1,'one');

        comment on table crud_auth_other_role is '
        authorize some_other_role
        ';

        create function crud_auth_login()
        returns table (
            name_identifier int,
            name text,
            role text[]
        )
        language sql as $$
        select
            777 as name_identifier,
            'crud_auth_user' as name,
            array['crud_role'] as role
        $$;
        comment on function crud_auth_login() is 'login';
        """);
    }
}

[Collection("TestFixture")]
public class CrudAuthTests(TestFixture test)
{
    private static StringContent Json(string body) =>
        new(body, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Anonymous_Requests_Get_401_On_Every_Crud_Variant()
    {
        using var client = test.Application.CreateClient();

        using var select = await client.GetAsync("/api/crud-auth-protected/");
        select.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "select must require authorization");

        using var insert = await client.PutAsync("/api/crud-auth-protected/", Json("{\"id\":3,\"name\":\"three\"}"));
        insert.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "insert must require authorization");

        using var update = await client.PostAsync("/api/crud-auth-protected/", Json("{\"id\":1,\"name\":\"hacked\"}"));
        update.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "update must require authorization");

        using var delete = await client.DeleteAsync("/api/crud-auth-protected/?id=1");
        delete.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "delete must require authorization");

        // And nothing actually changed.
        using var loggedIn = test.Application.CreateClient();
        using var login = await loggedIn.PostAsync("/api/crud-auth-login/", null);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        using var verify = await loggedIn.GetAsync("/api/crud-auth-protected/?id=1");
        (await verify.Content.ReadAsStringAsync()).Should().Be("[{\"id\":1,\"name\":\"one\"}]");
    }

    [Fact]
    public async Task Wrong_Role_Gets_403_Right_Role_Succeeds()
    {
        using var client = test.Application.CreateClient();
        using var login = await client.PostAsync("/api/crud-auth-login/", null);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        // The logged-in user has crud_role, not some_other_role.
        using var forbidden = await client.GetAsync("/api/crud-auth-other-role/");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "an authenticated user without the required role must get 403 on a CRUD endpoint");

        // The protected table with the matching role works for every variant.
        using var select = await client.GetAsync("/api/crud-auth-protected/?id=2");
        select.StatusCode.Should().Be(HttpStatusCode.OK);
        (await select.Content.ReadAsStringAsync()).Should().Be("[{\"id\":2,\"name\":\"two\"}]");

        using var insert = await client.PutAsync("/api/crud-auth-protected/", Json("{\"id\":10,\"name\":\"ten\"}"));
        insert.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var update = await client.PostAsync("/api/crud-auth-protected/", Json("{\"id\":10,\"name\":\"ten-updated\"}"));
        update.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var check = await client.GetAsync("/api/crud-auth-protected/?id=10");
        (await check.Content.ReadAsStringAsync()).Should().Be("[{\"id\":10,\"name\":\"ten-updated\"}]");

        using var delete = await client.DeleteAsync("/api/crud-auth-protected/?id=10");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var gone = await client.GetAsync("/api/crud-auth-protected/?id=10");
        (await gone.Content.ReadAsStringAsync()).Should().Be("[]");
    }
}
