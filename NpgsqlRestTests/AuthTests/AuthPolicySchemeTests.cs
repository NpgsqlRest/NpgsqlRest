using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NpgsqlRestClient;

namespace NpgsqlRestTests.AuthTests;

/// <summary>
/// Integration tests for <see cref="Builder.BuildAuthentication"/> covering the policy-scheme wiring
/// added for the named cookie schemes fix. The bug being tested: prior to the fix, a config with
/// <c>CookieAuth: true</c> plus any <c>Auth:Schemes</c> entry of type Cookies would issue named-scheme
/// cookies on login but reject every authenticated request bearing only that named cookie, because
/// ASP.NET's authentication middleware only authenticates the default scheme — which was the main
/// cookie scheme — and no policy scheme was registered to dispatch on the cookie present.
///
/// These tests drive the real <see cref="Builder.BuildAuthentication"/> through a temporary config
/// file (same pattern as <see cref="AuthLegacyFieldFailFastTests"/>) and assert against the resulting
/// service-provider state: which schemes are registered, what name the default authenticate scheme
/// has, and — for the policy scheme — what its <c>ForwardDefaultSelector</c> returns for a request
/// bearing a given cookie or Authorization header.
/// </summary>
public class AuthPolicySchemeTests
{
    private const string SyntheticPolicySchemeName = "NpgsqlRest_PolicyScheme";

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

    private static IServiceProvider Sp(Builder builder)
        => builder.Instance.Services.BuildServiceProvider();

    private static DefaultHttpContext ContextWithCookie(string cookieName, string cookieValue = "v")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Append("Cookie", $"{cookieName}={cookieValue}");
        return ctx;
    }

    private static DefaultHttpContext ContextWithAuthHeader(string headerValue)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["Authorization"] = headerValue;
        return ctx;
    }

    // ------------------------------------------------------------------------
    // CookieSchemesInOrder population
    // ------------------------------------------------------------------------

    [Fact]
    public void Main_cookie_scheme_is_tracked_in_CookieSchemesInOrder()
    {
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "CookieName": ".main"
        }
        """);

        builder.CookieSchemesInOrder.Should().ContainSingle();
        builder.CookieSchemesInOrder[0].SchemeName.Should().Be("Cookies");
        builder.CookieSchemesInOrder[0].CookieName.Should().Be(".main");
    }

    [Fact]
    public void Main_cookie_scheme_without_CookieName_uses_AspNetCore_default()
    {
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies"
        }
        """);

        builder.CookieSchemesInOrder.Should().ContainSingle();
        builder.CookieSchemesInOrder[0].CookieName.Should().Be(".AspNetCore.Cookies");
    }

    [Fact]
    public void Named_cookie_scheme_is_tracked_with_main_first()
    {
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "CookieName": ".main",
            "Schemes": {
                "short_session": {
                    "Type": "Cookies",
                    "CookieValid": "1 hour",
                    "CookieName": ".short"
                }
            }
        }
        """);

        builder.CookieSchemesInOrder.Should().HaveCount(2);
        builder.CookieSchemesInOrder[0].SchemeName.Should().Be("Cookies");
        builder.CookieSchemesInOrder[0].CookieName.Should().Be(".main");
        builder.CookieSchemesInOrder[1].SchemeName.Should().Be("short_session");
        builder.CookieSchemesInOrder[1].CookieName.Should().Be(".short");
    }

    [Fact]
    public void Named_cookie_scheme_without_explicit_name_uses_AspNetCore_default_for_lookup()
    {
        // When neither root CookieName nor scheme CookieName is set, ASP.NET defaults the actual
        // cookie name to ".AspNetCore.<schemeName>". The tracker must record that effective name so
        // the selector's Cookies.ContainsKey check works.
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "Schemes": {
                "short_session": { "Type": "Cookies", "CookieValid": "1 hour" }
            }
        }
        """);

        // RegisterCookieSchemeFromConfig falls back to mainCookieName when a scheme has no explicit
        // CookieName; main has none either, so the scheme registration sets no Cookie.Name and
        // ASP.NET's per-scheme .AspNetCore.<schemeName> default applies.
        var trackedNamed = builder.CookieSchemesInOrder.Single(t => t.SchemeName == "short_session");
        trackedNamed.CookieName.Should().Be(".AspNetCore.short_session");
    }

    // ------------------------------------------------------------------------
    // Policy-scheme registration decisions
    // ------------------------------------------------------------------------

    [Fact]
    public void Cookies_only_no_named_schemes_does_not_register_policy_scheme()
    {
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies"
        }
        """);

        var sp = Sp(builder);
        var schemes = sp.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetAllSchemesAsync().GetAwaiter().GetResult().ToList();

        schemes.Should().Contain(s => s.Name == "Cookies");
        schemes.Should().NotContain(s => s.Name == SyntheticPolicySchemeName);

        // Default authenticate scheme remains the main cookie scheme — preserves legacy behavior.
        var defaultScheme = sp.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetDefaultAuthenticateSchemeAsync().GetAwaiter().GetResult();
        defaultScheme!.Name.Should().Be("Cookies");
    }

    [Fact]
    public void Cookies_plus_named_cookie_scheme_registers_synthetic_policy_scheme_as_default()
    {
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "CookieName": ".main",
            "Schemes": {
                "short_session": {
                    "Type": "Cookies",
                    "CookieValid": "1 hour",
                    "CookieName": ".short"
                }
            }
        }
        """);

        var sp = Sp(builder);
        var provider = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var schemes = provider.GetAllSchemesAsync().GetAwaiter().GetResult().ToList();

        schemes.Should().Contain(s => s.Name == "Cookies");
        schemes.Should().Contain(s => s.Name == "short_session");
        schemes.Should().Contain(s => s.Name == SyntheticPolicySchemeName);

        var defaultScheme = provider.GetDefaultAuthenticateSchemeAsync().GetAwaiter().GetResult();
        defaultScheme!.Name.Should().Be(SyntheticPolicySchemeName,
            "with named cookie schemes the synthetic policy scheme must be the default so " +
            "UseAuthentication walks the cookie-aware selector on every request");
    }

    [Fact]
    public void Cookies_plus_bearer_no_named_schemes_uses_composite_policy_scheme_name()
    {
        // Legacy multi-main case — unchanged by the fix. Composite name avoids collision with any
        // individual scheme.
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "BearerTokenAuth": true,
            "BearerTokenAuthScheme": "BearerToken"
        }
        """);

        var sp = Sp(builder);
        var defaultScheme = sp.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetDefaultAuthenticateSchemeAsync().GetAwaiter().GetResult();
        defaultScheme!.Name.Should().Be("Cookies_and_BearerToken");
    }

    [Fact]
    public void Cookies_plus_bearer_plus_named_cookie_keeps_composite_name()
    {
        // When both legacy and new triggers apply, the legacy composite name wins (named cookie
        // schemes just get added to the same cookie-aware selector). The synthetic name is reserved
        // for the cookies-only-but-multiple case.
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "BearerTokenAuth": true,
            "BearerTokenAuthScheme": "BearerToken",
            "Schemes": {
                "short_session": { "Type": "Cookies", "CookieValid": "1 hour", "CookieName": ".short" }
            }
        }
        """);

        var sp = Sp(builder);
        var defaultScheme = sp.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetDefaultAuthenticateSchemeAsync().GetAwaiter().GetResult();
        defaultScheme!.Name.Should().Be("Cookies_and_BearerToken");
        defaultScheme.Name.Should().NotBe(SyntheticPolicySchemeName);
    }

    // ------------------------------------------------------------------------
    // ForwardDefaultSelector dispatch behavior — the actual fix
    // ------------------------------------------------------------------------

    private static Func<HttpContext, string?> GetSelector(Builder builder, string policySchemeName)
    {
        var sp = Sp(builder);
        var opts = sp.GetRequiredService<IOptionsMonitor<PolicySchemeOptions>>().Get(policySchemeName);
        opts.ForwardDefaultSelector.Should().NotBeNull(
            "the policy scheme must install a ForwardDefaultSelector callback");
        return opts.ForwardDefaultSelector!;
    }

    [Fact]
    public void Selector_named_cookie_present_returns_named_scheme()
    {
        // The user-visible repro: a request bearing ONLY the named-scheme cookie was rejected because
        // the default scheme was the main cookie scheme. Now the selector routes to the named scheme.
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "CookieName": ".main",
            "Schemes": {
                "short_session": { "Type": "Cookies", "CookieValid": "1 hour", "CookieName": ".short" }
            }
        }
        """);

        var selector = GetSelector(builder, SyntheticPolicySchemeName);

        var ctx = ContextWithCookie(".short");
        selector(ctx).Should().Be("short_session");
    }

    [Fact]
    public void Selector_main_cookie_present_returns_main_scheme()
    {
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "CookieName": ".main",
            "Schemes": {
                "short_session": { "Type": "Cookies", "CookieValid": "1 hour", "CookieName": ".short" }
            }
        }
        """);

        var selector = GetSelector(builder, SyntheticPolicySchemeName);

        var ctx = ContextWithCookie(".main");
        selector(ctx).Should().Be("Cookies");
    }

    [Fact]
    public void Selector_both_cookies_present_returns_main_first_per_documented_order()
    {
        // Edge case: both cookies somehow co-exist. The walk order is "main first, then named in
        // registration order" — so .main wins. (See bug-doc test plan #3 — "first match wins per
        // the documented order".)
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "CookieName": ".main",
            "Schemes": {
                "short_session": { "Type": "Cookies", "CookieValid": "1 hour", "CookieName": ".short" }
            }
        }
        """);

        var selector = GetSelector(builder, SyntheticPolicySchemeName);

        var ctx = new DefaultHttpContext();
        ctx.Request.Headers.Append("Cookie", ".main=a; .short=b");
        selector(ctx).Should().Be("Cookies");
    }

    [Fact]
    public void Selector_neither_cookie_present_falls_back_to_main_cookie_scheme()
    {
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "CookieName": ".main",
            "Schemes": {
                "short_session": { "Type": "Cookies", "CookieValid": "1 hour", "CookieName": ".short" }
            }
        }
        """);

        var selector = GetSelector(builder, SyntheticPolicySchemeName);

        var ctx = new DefaultHttpContext();
        // No cookies at all — anonymous request. Falls back to cookieScheme so subsequent
        // AuthenticateAsync runs the cookie handler (yields anonymous principal, no false 401s).
        selector(ctx).Should().Be("Cookies");
    }

    [Fact]
    public void Selector_bearer_header_still_routes_to_bearer_scheme()
    {
        // Composite mode (cookies + bearer + named cookie). Bearer-header dispatch must be unchanged.
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "BearerTokenAuth": true,
            "BearerTokenAuthScheme": "BearerToken",
            "Schemes": {
                "short_session": { "Type": "Cookies", "CookieValid": "1 hour", "CookieName": ".short" }
            }
        }
        """);

        var selector = GetSelector(builder, "Cookies_and_BearerToken");

        // Opaque (non-JWT) bearer token → BearerToken scheme.
        var ctx = ContextWithAuthHeader("Bearer opaque-token-not-three-dot-parts");
        selector(ctx).Should().Be("BearerToken");
    }

    [Fact]
    public void Selector_jwt_bearer_routes_to_jwt_scheme()
    {
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "JwtAuth": true,
            "JwtAuthScheme": "Bearer",
            "JwtSecret": "test-secret-needs-to-be-at-least-32-chars-long"
        }
        """);

        var selector = GetSelector(builder, "Cookies_and_Bearer");

        // Three-part dotted token → JWT branch.
        var ctx = ContextWithAuthHeader("Bearer header.payload.signature");
        selector(ctx).Should().Be("Bearer");
    }

    [Fact]
    public void Selector_named_cookie_works_in_composite_mode_too()
    {
        // Both bearer and a named cookie scheme. A request with only the named cookie must still
        // resolve to the named scheme (not get short-circuited by the bearer branch or fall through
        // to the main cookie default).
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "CookieName": ".main",
            "BearerTokenAuth": true,
            "BearerTokenAuthScheme": "BearerToken",
            "Schemes": {
                "short_session": { "Type": "Cookies", "CookieValid": "1 hour", "CookieName": ".short" }
            }
        }
        """);

        var selector = GetSelector(builder, "Cookies_and_BearerToken");

        var ctx = ContextWithCookie(".short");
        selector(ctx).Should().Be("short_session");
    }

    [Fact]
    public void Selector_named_cookie_with_default_cookie_name_resolves_correctly()
    {
        // Named scheme without explicit CookieName → effective cookie is .AspNetCore.<schemeName>.
        // The selector must use the same default to find it.
        var builder = BuildFromConfig("""
        {
            "CookieAuth": true,
            "CookieAuthScheme": "Cookies",
            "Schemes": {
                "short_session": { "Type": "Cookies", "CookieValid": "1 hour" }
            }
        }
        """);

        var selector = GetSelector(builder, SyntheticPolicySchemeName);

        var ctx = ContextWithCookie(".AspNetCore.short_session");
        selector(ctx).Should().Be("short_session");
    }
}
