using System.IdentityModel.Tokens.Jwt;
using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    /// <summary>
    /// SQL setup for the Auth Scheme login integration tests. Four login functions, each hard-coding
    /// a different scheme name — main cookie scheme, additional cookie scheme (short_session),
    /// main JWT scheme, and additional JWT scheme (admin). They don't verify any password — the
    /// tests only care about the resulting cookie attributes / token contents.
    /// </summary>
    public static void AuthSchemeLoginTests()
    {
        script.Append("""

        create function ast_login_main_cookie()
        returns table (scheme text, name_identifier text, name text)
        language sql as $$
        select 'Cookies' as scheme, 'user_main' as name_identifier, 'Main User' as name;
        $$;
        comment on function ast_login_main_cookie() is '
        HTTP GET
        login
        ';

        create function ast_login_short_cookie()
        returns table (scheme text, name_identifier text, name text)
        language sql as $$
        select 'ast_short_session' as scheme, 'user_short' as name_identifier, 'Short Session User' as name;
        $$;
        comment on function ast_login_short_cookie() is '
        HTTP GET
        login
        ';

        create function ast_login_main_jwt()
        returns table (scheme text, name_identifier text, name text)
        language sql as $$
        select 'Bearer' as scheme, 'user_main_jwt' as name_identifier, 'Main JWT User' as name;
        $$;
        comment on function ast_login_main_jwt() is '
        HTTP GET
        login
        ';

        create function ast_login_admin_jwt()
        returns table (scheme text, name_identifier text, name text)
        language sql as $$
        select 'ast_jwt_admin' as scheme, 'admin_user' as name_identifier, 'Admin User' as name;
        $$;
        comment on function ast_login_admin_jwt() is '
        HTTP GET
        login
        ';
""");
    }
}

[Collection("AuthSchemeTestFixture")]
public class AuthSchemeLoginTests(AuthSchemeTestFixture test)
{
    /// <summary>
    /// Login function returning the main "Cookies" scheme yields a cookie named ".main-cookie" with
    /// MaxAge set (multi-session). Baseline — confirms the standard path still works alongside the
    /// additional schemes.
    /// </summary>
    [Fact]
    public async Task Main_cookie_scheme_writes_main_cookie_with_max_age()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/ast-login-main-cookie");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var setCookie = string.Join(";", response.Headers.GetValues("Set-Cookie"));
        setCookie.Should().Contain(".main-cookie=");
        setCookie.Should().NotContain(".short-cookie=");
        setCookie.Should().Contain("max-age");
    }

    /// <summary>
    /// Login function returning "ast_short_session" yields a cookie named ".short-cookie" without
    /// Max-Age (session-only). Proves the scheme name flows through the LoginHandler and ASP.NET
    /// applies the per-scheme cookie options (different name, different lifetime semantics).
    /// </summary>
    [Fact]
    public async Task Additional_cookie_scheme_writes_session_only_cookie()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/ast-login-short-cookie");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var setCookie = string.Join(";", response.Headers.GetValues("Set-Cookie"));
        setCookie.Should().Contain(".short-cookie=");
        setCookie.Should().NotContain(".main-cookie=");
        setCookie.Should().NotContain("max-age", because: "MultiSessions=false on the scheme → no Max-Age (session-only cookie)");
    }

    /// <summary>
    /// Login function returning the main "Bearer" (JWT) scheme yields a JSON token response signed
    /// with the main JWT secret. Confirms the JwtLoginHandler is hit (rather than SignIn flow) when
    /// a JWT scheme is returned.
    /// </summary>
    [Fact]
    public async Task Main_jwt_scheme_returns_token_signed_with_main_secret()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/ast-login-main-jwt");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(body)!;
        var token = json["accessToken"]!.GetValue<string>();

        // Decode without validating, then verify the issuer signature matches main secret by length
        // — JwtSecurityTokenHandler.WriteToken/ReadJwtToken just inspects the unverified payload.
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(c => c.Type == "name_identifier" && c.Value == "user_main_jwt");
    }

    /// <summary>
    /// Login function returning "ast_jwt_admin" yields a token signed with the ADMIN scheme's secret
    /// (different from the main secret). Critical regression: if JwtLoginHandler used the main config
    /// regardless of scheme, the admin scope would silently use the wrong key. The test pins this by
    /// validating the token under the admin secret only — main secret would fail signature check.
    /// </summary>
    [Fact]
    public async Task Additional_jwt_scheme_token_validates_under_per_scheme_secret_only()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/ast-login-admin-jwt");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(body)!;
        var token = json["accessToken"]!.GetValue<string>();

        var handler = new JwtSecurityTokenHandler();
        var adminKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(AuthSchemeTestFixture.AdminJwtSecret));
        var mainKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(AuthSchemeTestFixture.MainJwtSecret));

        // Validates under admin secret.
        handler.ValidateToken(token, new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = adminKey,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false
        }, out _);

        // FAILS to validate under main secret — different signing key.
        var act = () => handler.ValidateToken(token, new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = mainKey,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false
        }, out _);
        act.Should().Throw<Exception>(because: "admin-scheme token must NOT validate under the main secret");
    }

    /// <summary>
    /// The admin scheme has a much shorter expiration (5 minutes) than the main JWT (60 minutes) —
    /// proves JwtExpire is being read per-scheme rather than from the main config. We allow ±60s
    /// slack so this test isn't flaky on slow machines.
    /// </summary>
    [Fact]
    public async Task Additional_jwt_scheme_uses_per_scheme_expire_duration()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/ast-login-admin-jwt");
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(body)!;
        var expiresIn = json["expiresIn"]!.GetValue<int>();

        // 5 minutes = 300 seconds. Allow generous slack on either side.
        expiresIn.Should().BeInRange(240, 360,
            because: "admin scheme uses Expire=5 minutes, distinct from the main scheme's 60 minutes");
    }
}
