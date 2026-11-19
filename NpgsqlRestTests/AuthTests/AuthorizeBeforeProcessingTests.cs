namespace NpgsqlRestTests;

public static partial class Database
{
    public static void AuthorizeBeforeProcessingTests()
    {
        script.Append(@"
create function auth_first_validated(_email text)
returns text language sql as $$ select 'ok ' || _email $$;
comment on function auth_first_validated(text) is 'HTTP POST
authorize
validate _email using email';

create function auth_first_strict(_value text)
returns text language sql strict as $$ select 'ok ' || _value $$;
comment on function auth_first_strict(text) is 'HTTP POST
authorize';

create function auth_first_roles(_value text)
returns text language sql as $$ select 'ok ' || _value $$;
comment on function auth_first_roles(text) is 'HTTP POST
authorize role_that_nobody_has
validate _value using not_empty';

-- overloads: the one-parameter version is public, the two-parameter version requires sign-in
create function auth_first_overload(_a text)
returns text language sql as $$ select 'public ' || _a $$;
comment on function auth_first_overload(text) is 'HTTP POST';

create function auth_first_overload(_a text, _b text)
returns text language sql as $$ select 'private ' || _a || _b $$;
comment on function auth_first_overload(text, text) is 'HTTP POST
authorize';
");
    }
}

/// <summary>
/// Security regression: authorization is decided before the request is processed. Anonymous callers of an
/// authorize endpoint used to get the endpoint's parameter processing first (body parsing, password hashing,
/// validation, encryption) - and saw its validation errors, strict-null 204 or unknown-parameter 404 - before 401.
/// </summary>
[Collection("TestFixture")]
public class AuthorizeBeforeProcessingTests(TestFixture test)
{
    private const string Unauthorized = "{\"title\":\"Unauthorized\",\"status\":401}";
    private const string Forbidden = "{\"title\":\"Forbidden\",\"status\":403}";

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Anonymous_invalid_input_gets_401_not_the_validation_error()
    {
        using var response = await test.Client.PostAsync("/api/auth-first-validated/", Json("{\"email\":\"not-an-email\"}"));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Be(Unauthorized);
    }

    [Fact]
    public async Task Anonymous_null_input_to_strict_function_gets_401_not_204()
    {
        using var response = await test.Client.PostAsync("/api/auth-first-strict/", Json("{\"value\":null}"));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Anonymous_unknown_parameter_gets_401_not_404()
    {
        using var response = await test.Client.PostAsync("/api/auth-first-strict/", Json("{\"value\":\"x\",\"probe\":1}"));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signed_in_without_role_gets_403_not_the_validation_error()
    {
        using var client = test.Application.CreateClient();
        using var login = await client.GetAsync("/login");
        using var response = await client.PostAsync("/api/auth-first-roles/", Json("{\"value\":\"\"}"));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Be(Forbidden);
    }

    [Fact]
    public async Task Signed_in_caller_still_gets_the_validation_error()
    {
        using var client = test.Application.CreateClient();
        using var login = await client.GetAsync("/login");
        using var response = await client.PostAsync("/api/auth-first-validated/", Json("{\"email\":\"not-an-email\"}"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Public_overload_of_an_authorized_endpoint_stays_public()
    {
        using var response = await test.Client.PostAsync("/api/auth-first-overload/", Json("{\"a\":\"x\"}"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("public x");
    }

    [Fact]
    public async Task Authorized_overload_still_rejects_anonymous()
    {
        using var response = await test.Client.PostAsync("/api/auth-first-overload/", Json("{\"a\":\"x\",\"b\":\"y\"}"));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Be(Unauthorized);
    }
}
