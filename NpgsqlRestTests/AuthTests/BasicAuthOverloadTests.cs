namespace NpgsqlRestTests;

public static partial class Database
{
    public static void BasicAuthOverloadTests()
    {
        script.Append("""
        -- overloads of one path: the one-parameter version is public, the two-parameter version uses Basic Auth
        create function basic_auth_overload(_a text)
        returns text language sql as $$ select 'public ' || _a $$;
        comment on function basic_auth_overload(text) is 'HTTP GET';

        create function basic_auth_overload(_a text, _b text)
        returns text language sql as $$ select 'protected ' || _a || _b $$;
        comment on function basic_auth_overload(text, text) is '
        HTTP GET
        basic_auth my_name Myb55+6lW6iiUOI3opLkysOaS8J0NNIuQ+qE2SGaKs3r62ngDJROrhX75+zmLC7t
        ';

        -- same, created in the opposite order (the routine registered last becomes the path's primary endpoint)
        create function basic_auth_overload_rev(_a text, _b text)
        returns text language sql as $$ select 'protected ' || _a || _b $$;
        comment on function basic_auth_overload_rev(text, text) is '
        HTTP GET
        basic_auth my_name Myb55+6lW6iiUOI3opLkysOaS8J0NNIuQ+qE2SGaKs3r62ngDJROrhX75+zmLC7t
        ';

        create function basic_auth_overload_rev(_a text)
        returns text language sql as $$ select 'public ' || _a $$;
        comment on function basic_auth_overload_rev(text) is 'HTTP GET';
        """);
    }
}

/// <summary>
/// Security regression: Basic Auth applies to the overload a request resolves to. It used to be checked only for the
/// path's primary endpoint: an overload with basic_auth (whose primary had none) ran without credentials, and public
/// overloads of a basic_auth primary were challenged. Both creation orders are covered (the last one registered is the
/// primary).
/// </summary>
[Collection("TestFixture")]
public class BasicAuthOverloadTests(TestFixture test)
{
    [Theory]
    [InlineData("/api/basic-auth-overload/")]
    [InlineData("/api/basic-auth-overload-rev/")]
    public async Task Public_overload_needs_no_credentials(string path)
    {
        using var result = await test.Client.GetAsync(path + "?a=x");
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        (await result.Content.ReadAsStringAsync()).Should().Be("public x");
    }

    [Theory]
    [InlineData("/api/basic-auth-overload/")]
    [InlineData("/api/basic-auth-overload-rev/")]
    public async Task Basic_auth_overload_without_credentials_is_challenged(string path)
    {
        using var result = await test.Client.GetAsync(path + "?a=x&b=y");
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        result.Headers.WwwAuthenticate.ToString().Should().StartWith("Basic");
    }

    [Theory]
    [InlineData("/api/basic-auth-overload/")]
    [InlineData("/api/basic-auth-overload-rev/")]
    public async Task Basic_auth_overload_with_credentials_runs(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path + "?a=x&b=y");
        request.Headers.Add("Authorization", "Basic bXlfbmFtZTpteV9wYXNzd29yZA=="); // my_name:my_password
        using var result = await test.Client.SendAsync(request);
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        (await result.Content.ReadAsStringAsync()).Should().Be("protected xy");
    }
}
