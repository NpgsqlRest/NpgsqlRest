using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NpgsqlRestClient;

namespace NpgsqlRestTests.AuthTests;

/// <summary>
/// Tests for the <c>CookieSameSite</c> and <c>CookieSecure</c> config knobs added to support
/// cross-origin scenarios — e.g. an SPA on <c>app.example.com</c> calling this API on
/// <c>api.example.com</c>. Browsers require <c>SameSite=None; Secure</c> for cross-site cookie auth;
/// without these knobs, the cookie defaults from ASP.NET Core (<c>Lax</c> / <c>SameAsRequest</c>)
/// silently break that scenario.
///
/// Coverage:
///   - All four <see cref="SameSiteMode"/> values parse correctly (case-insensitive).
///   - All three <see cref="CookieSecurePolicy"/> values parse correctly.
///   - Invalid values throw at startup (fail-fast for typos in security-relevant config).
///   - Unset values preserve ASP.NET defaults — verified by checking that the configured
///     <see cref="CookieAuthenticationOptions"/> still hold ASP.NET's defaults
///     (<see cref="SameSiteMode.Lax"/> / <see cref="CookieSecurePolicy.SameAsRequest"/>).
///   - Named cookie schemes inherit from the root Auth section when unset, and overrides win.
/// </summary>
public class AuthCookieSameSiteSecureTests
{
    private static Builder BuildFromConfig(string authJson)
    {
        var json = $$"""
        {
            "Auth": {{authJson}}
        }
        """;
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, json);
        var config = new Config();
        try
        {
            config.Build([tempFile], []);
        }
        finally
        {
            File.Delete(tempFile);
        }
        var builder = new Builder(config);
        builder.BuildInstance();
        builder.BuildAuthentication();
        return builder;
    }

    private static CookieAuthenticationOptions Options(Builder builder, string schemeName)
        => builder.Instance.Services.BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(schemeName);

    // ------------------------------------------------------------------------
    // Main cookie scheme — SameSite parsing
    // ------------------------------------------------------------------------

    [Theory]
    [InlineData("Strict", SameSiteMode.Strict)]
    [InlineData("Lax", SameSiteMode.Lax)]
    [InlineData("None", SameSiteMode.None)]
    [InlineData("Unspecified", SameSiteMode.Unspecified)]
    [InlineData("strict", SameSiteMode.Strict)] // case-insensitive
    [InlineData("NONE", SameSiteMode.None)]
    public void Main_cookie_SameSite_parses_all_enum_values(string raw, SameSiteMode expected)
    {
        // SameSite=None requires Secure=Always to actually be honored by browsers; pair them here so
        // the warning path doesn't fire incidentally in tests focused on parsing.
        var builder = BuildFromConfig($$"""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "CookieSameSite": "{{raw}}",
            "CookieSecure": "Always"
        }
        """);

        Options(builder, "Cookies").Cookie.SameSite.Should().Be(expected);
    }

    [Fact]
    public void Main_cookie_SameSite_invalid_throws_at_startup()
    {
        var act = () => BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "CookieSameSite": "Loose"
        }
        """);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Invalid value 'Loose' for Auth:CookieSameSite*Strict*");
    }

    [Fact]
    public void Main_cookie_SameSite_unset_preserves_AspNet_default()
    {
        // ASP.NET's CookieAuthenticationHandler defaults Cookie.SameSite to Lax. With CookieSameSite
        // omitted, we must not overwrite that — the assertion proves the no-op branch in the
        // closure is reached.
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies"
        }
        """);

        Options(builder, "Cookies").Cookie.SameSite.Should().Be(SameSiteMode.Lax,
            "ASP.NET Core's CookieAuthenticationOptions default is Lax — unset config must not " +
            "overwrite it");
    }

    // ------------------------------------------------------------------------
    // Main cookie scheme — SecurePolicy parsing
    // ------------------------------------------------------------------------

    [Theory]
    [InlineData("SameAsRequest", CookieSecurePolicy.SameAsRequest)]
    [InlineData("Always", CookieSecurePolicy.Always)]
    [InlineData("None", CookieSecurePolicy.None)]
    [InlineData("ALWAYS", CookieSecurePolicy.Always)] // case-insensitive
    public void Main_cookie_Secure_parses_all_enum_values(string raw, CookieSecurePolicy expected)
    {
        var builder = BuildFromConfig($$"""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "CookieSecure": "{{raw}}"
        }
        """);

        Options(builder, "Cookies").Cookie.SecurePolicy.Should().Be(expected);
    }

    [Fact]
    public void Main_cookie_Secure_invalid_throws_at_startup()
    {
        var act = () => BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "CookieSecure": "AlwaysHttps"
        }
        """);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Invalid value 'AlwaysHttps' for Auth:CookieSecure*SameAsRequest*Always*None*");
    }

    [Fact]
    public void Main_cookie_Secure_unset_preserves_AspNet_default()
    {
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies"
        }
        """);

        Options(builder, "Cookies").Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.SameAsRequest,
            "ASP.NET's default is SameAsRequest — unset config must not overwrite it");
    }

    // ------------------------------------------------------------------------
    // Combined cross-origin scenario — the actual reason these knobs exist
    // ------------------------------------------------------------------------

    [Fact]
    public void Main_cookie_cross_origin_pattern_writes_SameSiteNone_and_Secure_Always()
    {
        // This is the configuration shape an external-API caller needs: SPA on a different origin
        // calling this API, cookie-based session. Both knobs flow through to ASP.NET's
        // CookieAuthenticationOptions so the browser actually accepts the cross-site cookie.
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "CookieSameSite": "None",
            "CookieSecure": "Always"
        }
        """);

        var opts = Options(builder, "Cookies");
        opts.Cookie.SameSite.Should().Be(SameSiteMode.None);
        opts.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
    }

    // ------------------------------------------------------------------------
    // Named cookie schemes — inheritance + per-scheme override
    // ------------------------------------------------------------------------

    [Fact]
    public void Named_cookie_inherits_SameSite_and_Secure_from_root()
    {
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "CookieSameSite": "None",
            "CookieSecure": "Always",
            "Schemes": {
                "short_session": { "Type": "Cookies", "CookieValid": "1 hour" }
            }
        }
        """);

        var opts = Options(builder, "short_session");
        opts.Cookie.SameSite.Should().Be(SameSiteMode.None,
            "named scheme without explicit CookieSameSite should inherit the root value");
        opts.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always,
            "named scheme without explicit CookieSecure should inherit the root value");
    }

    [Fact]
    public void Named_cookie_per_scheme_override_wins_over_root()
    {
        // Root says None+Always (cross-origin), but a tighter "short session" scheme pins itself to
        // Strict + SameAsRequest. Mirrors the existing override-wins behavior for CookieValid /
        // CookieMultiSessions / etc.
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "CookieSameSite": "None",
            "CookieSecure": "Always",
            "Schemes": {
                "short_session": {
                    "Type": "Cookies",
                    "CookieValid": "1 hour",
                    "CookieSameSite": "Strict",
                    "CookieSecure": "SameAsRequest"
                }
            }
        }
        """);

        var named = Options(builder, "short_session");
        named.Cookie.SameSite.Should().Be(SameSiteMode.Strict);
        named.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.SameAsRequest);

        // Main scheme keeps the root values — override is per-scheme only.
        var main = Options(builder, "Cookies");
        main.Cookie.SameSite.Should().Be(SameSiteMode.None);
        main.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
    }

    [Fact]
    public void Named_cookie_no_root_no_override_uses_AspNet_default()
    {
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "Schemes": {
                "short_session": { "Type": "Cookies", "CookieValid": "1 hour" }
            }
        }
        """);

        Options(builder, "short_session").Cookie.SameSite.Should().Be(SameSiteMode.Lax);
        Options(builder, "short_session").Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.SameAsRequest);
    }

    [Fact]
    public void Named_cookie_invalid_SameSite_throws_with_scheme_path()
    {
        // Error message should pinpoint the offending key path so users know which scheme broke.
        var act = () => BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "Schemes": {
                "short_session": {
                    "Type": "Cookies",
                    "CookieValid": "1 hour",
                    "CookieSameSite": "Loose"
                }
            }
        }
        """);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Auth:Schemes:short_session:CookieSameSite*");
    }
}
