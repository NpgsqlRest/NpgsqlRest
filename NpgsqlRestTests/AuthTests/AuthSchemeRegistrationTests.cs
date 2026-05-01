using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NpgsqlRestClient;

namespace NpgsqlRestTests.AuthTests;

/// <summary>
/// Unit tests for <see cref="Builder.RegisterAuthSchemes"/>. The helper reads the <c>Auth:Schemes</c>
/// dictionary, validates each entry, and registers an ASP.NET Core authentication scheme of the
/// declared type (Cookies / BearerToken / Jwt). Tests verify both the happy path (scheme actually
/// registered with scheme-specific options applied) and every validation failure (collision with main
/// scheme, unsupported Type, missing Type, CookieName conflict, refresh path conflict, JWT secret
/// requirements).
///
/// Test seam: <see cref="Builder.RegisterAuthSchemes"/> takes an <see cref="AuthenticationBuilder"/>
/// that wraps an <see cref="IServiceCollection"/>. We build that pair directly and inspect the
/// resulting per-scheme options via the service provider's <c>IOptionsMonitor{TOptions}</c>. JWT and
/// BearerToken configs are also captured on <see cref="Builder.AdditionalJwtTokenConfigs"/> /
/// <see cref="Builder.AdditionalBearerTokenConfigs"/> for refresh-middleware wiring downstream.
/// </summary>
public class AuthSchemeRegistrationTests
{
    private static (Builder builder, IConfigurationSection authCfg, AuthenticationBuilder auth, IServiceCollection services)
        BuildSetup(string authJson)
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
        var authCfg = config.Cfg.GetSection("Auth");
        var services = new ServiceCollection();
        services.AddAuthentication();
        var auth = new AuthenticationBuilder(services);
        return (builder, authCfg, auth, services);
    }

    private static CookieAuthenticationOptions GetCookieOptions(IServiceCollection services, string schemeName)
    {
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(schemeName);
    }

    private static BearerTokenOptions GetBearerTokenOptions(IServiceCollection services, string schemeName)
    {
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>().Get(schemeName);
    }

    private static JwtBearerOptions GetJwtOptions(IServiceCollection services, string schemeName)
    {
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(schemeName);
    }

    // ------------------------------------------------------------------------
    // Common: shape / no-op / disabled / collision cases
    // ------------------------------------------------------------------------

    [Fact]
    public void Empty_schemes_section_is_a_no_op()
    {
        var (builder, authCfg, auth, services) = BuildSetup("""{}""");
        builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        var sp = services.BuildServiceProvider();
        var schemeProvider = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var schemes = schemeProvider.GetAllSchemesAsync().GetAwaiter().GetResult();
        schemes.Should().NotContain(s => s.Name == "short_session");
    }

    [Fact]
    public void Disabled_scheme_is_skipped()
    {
        var (builder, authCfg, auth, services) = BuildSetup("""
        {
            "Schemes": {
                "short_session": { "Type": "Cookies", "Enabled": false, "CookieValid": "1 hour" }
            }
        }
        """);
        builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        var sp = services.BuildServiceProvider();
        var schemeProvider = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = schemeProvider.GetSchemeAsync("short_session").GetAwaiter().GetResult();
        scheme.Should().BeNull("disabled schemes should not be registered");
    }

    [Fact]
    public void Scheme_name_collision_with_main_cookie_scheme_throws()
    {
        var (builder, authCfg, auth, _) = BuildSetup("""
        {
            "Schemes": { "Cookies": { "Type": "Cookies", "Enabled": true, "CookieValid": "1 hour" } }
        }
        """);

        var act = () => builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*scheme name collides with a main authentication scheme*");
    }

    [Fact]
    public void Missing_Type_throws()
    {
        var (builder, authCfg, auth, _) = BuildSetup("""
        {
            "Schemes": { "no_type": { "Enabled": true } }
        }
        """);

        var act = () => builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Auth:Schemes:no_type:Type is required.*");
    }

    [Fact]
    public void Unknown_Type_throws()
    {
        var (builder, authCfg, auth, _) = BuildSetup("""
        {
            "Schemes": { "weird": { "Type": "Hocus", "Enabled": true } }
        }
        """);

        var act = () => builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Type='Hocus' is not supported*Valid: Cookies, BearerToken, Jwt*");
    }

    // ------------------------------------------------------------------------
    // Type=Cookies
    // ------------------------------------------------------------------------

    [Fact]
    public void Cookies_scheme_registers_with_explicit_CookieValid_override()
    {
        var (builder, authCfg, auth, services) = BuildSetup("""
        {
            "Schemes": { "short_session": { "Type": "Cookies", "Enabled": true, "CookieValid": "1 hour" } }
        }
        """);
        builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        var options = GetCookieOptions(services, "short_session");
        options.ExpireTimeSpan.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void Cookies_scheme_inherits_root_CookieValid_when_unset()
    {
        var (builder, authCfg, auth, services) = BuildSetup("""
        {
            "CookieValid": "2 hours",
            "Schemes": { "inherited": { "Type": "Cookies", "Enabled": true } }
        }
        """);
        builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        GetCookieOptions(services, "inherited").ExpireTimeSpan.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Cookies_scheme_MultiSessions_false_drops_MaxAge()
    {
        var (builder, authCfg, auth, services) = BuildSetup("""
        {
            "Schemes": {
                "short_session": { "Type": "Cookies", "Enabled": true, "CookieValid": "1 hour", "CookieMultiSessions": false }
            }
        }
        """);
        builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        var options = GetCookieOptions(services, "short_session");
        options.Cookie.MaxAge.Should().BeNull("MultiSessions=false → session-only cookie");
        options.ExpireTimeSpan.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void Cookies_scheme_explicit_CookieName_collision_throws()
    {
        var (builder, authCfg, auth, _) = BuildSetup("""
        {
            "Schemes": {
                "p1": { "Type": "Cookies", "Enabled": true, "CookieValid": "1 hour", "CookieName": "session" },
                "p2": { "Type": "Cookies", "Enabled": true, "CookieValid": "2 hours", "CookieName": "session" }
            }
        }
        """);

        var act = () => builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CookieName='session' collides*");
    }

    [Fact]
    public void Cookies_scheme_invalid_interval_throws()
    {
        var (builder, authCfg, auth, _) = BuildSetup("""
        {
            "Schemes": { "bad": { "Type": "Cookies", "Enabled": true, "CookieValid": "fortnight" } }
        }
        """);

        var act = () => builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Auth:Schemes:bad:CookieValid: 'fortnight'*");
    }

    // ------------------------------------------------------------------------
    // Type=BearerToken
    // ------------------------------------------------------------------------

    [Fact]
    public void BearerToken_scheme_registers_with_expire_override()
    {
        var (builder, authCfg, auth, services) = BuildSetup("""
        {
            "Schemes": { "api_token": { "Type": "BearerToken", "Enabled": true, "BearerTokenExpire": "30 minutes" } }
        }
        """);
        builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        var options = GetBearerTokenOptions(services, "api_token");
        options.BearerTokenExpiration.Should().Be(TimeSpan.FromMinutes(30));
        options.RefreshTokenExpiration.Should().Be(TimeSpan.FromMinutes(30));

        builder.AdditionalBearerTokenConfigs.Should().ContainSingle(c => c.Scheme == "api_token");
    }

    [Fact]
    public void BearerToken_scheme_with_refresh_path_appends_to_AdditionalBearerTokenConfigs()
    {
        var (builder, authCfg, auth, _) = BuildSetup("""
        {
            "Schemes": {
                "api_token": {
                    "Type": "BearerToken",
                    "Enabled": true,
                    "BearerTokenExpire": "1 hour",
                    "BearerTokenRefreshPath": "/api/api-token/refresh"
                }
            }
        }
        """);
        builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        builder.AdditionalBearerTokenConfigs.Should().ContainSingle();
        var cfg = builder.AdditionalBearerTokenConfigs[0];
        cfg.Scheme.Should().Be("api_token");
        cfg.RefreshPath.Should().Be("/api/api-token/refresh");
    }

    [Fact]
    public void BearerToken_scheme_inherits_BearerTokenExpire_from_root_when_unset()
    {
        var (builder, authCfg, auth, services) = BuildSetup("""
        {
            "BearerTokenExpire": "2 hours",
            "Schemes": { "api_token": { "Type": "BearerToken", "Enabled": true } }
        }
        """);
        builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        GetBearerTokenOptions(services, "api_token").BearerTokenExpiration.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Refresh_path_collision_between_main_and_scheme_throws()
    {
        // Main BearerTokenRefreshPath at /api/token/refresh, scheme tries to claim the same path.
        // Two app.Use middlewares listening on the same path would race — fail at startup.
        var (builder, authCfg, auth, _) = BuildSetup("""
        {
            "BearerTokenRefreshPath": "/api/token/refresh",
            "Schemes": {
                "api_token": {
                    "Type": "BearerToken",
                    "Enabled": true,
                    "BearerTokenExpire": "1 hour",
                    "BearerTokenRefreshPath": "/api/token/refresh"
                }
            }
        }
        """);

        var act = () => builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*BearerTokenRefreshPath='/api/token/refresh' collides*");
    }

    // ------------------------------------------------------------------------
    // Type=Jwt
    // ------------------------------------------------------------------------

    [Fact]
    public void Jwt_scheme_registers_and_appends_to_AdditionalJwtTokenConfigs()
    {
        var (builder, authCfg, auth, services) = BuildSetup("""
        {
            "Schemes": {
                "admin_jwt": {
                    "Type": "Jwt",
                    "Enabled": true,
                    "JwtSecret": "admin-scheme-secret-totally-different-32-chars",
                    "JwtExpire": "5 minutes"
                }
            }
        }
        """);
        builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        // ASP.NET registered the JwtBearer scheme.
        var sp = services.BuildServiceProvider();
        var schemeProvider = sp.GetRequiredService<IAuthenticationSchemeProvider>();
        var asScheme = schemeProvider.GetSchemeAsync("admin_jwt").GetAwaiter().GetResult();
        asScheme.Should().NotBeNull();
        asScheme!.HandlerType.Should().Be<JwtBearerHandler>();

        // Config captured for downstream wiring.
        builder.AdditionalJwtTokenConfigs.Should().ContainSingle();
        var cfg = builder.AdditionalJwtTokenConfigs[0];
        cfg.Scheme.Should().Be("admin_jwt");
        cfg.Secret.Should().Be("admin-scheme-secret-totally-different-32-chars");
        cfg.Expire.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Jwt_scheme_inherits_JwtSecret_from_root_when_unset()
    {
        // Realistic case: per-scheme expire override + reuse of root secret.
        var (builder, authCfg, auth, _) = BuildSetup("""
        {
            "JwtSecret": "shared-root-secret-at-least-32-chars-long",
            "Schemes": {
                "short_jwt": {
                    "Type": "Jwt",
                    "Enabled": true,
                    "JwtExpire": "2 minutes"
                }
            }
        }
        """);
        builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        builder.AdditionalJwtTokenConfigs.Should().ContainSingle();
        var cfg = builder.AdditionalJwtTokenConfigs[0];
        cfg.Secret.Should().Be("shared-root-secret-at-least-32-chars-long");
        cfg.Expire.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void Jwt_scheme_throws_when_no_secret_anywhere()
    {
        // Neither root nor scheme provides a JwtSecret — registration must fail loudly.
        var (builder, authCfg, auth, _) = BuildSetup("""
        {
            "Schemes": {
                "no_secret": { "Type": "Jwt", "Enabled": true, "JwtExpire": "5 minutes" }
            }
        }
        """);

        var act = () => builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Auth:Schemes:no_secret*requires JwtSecret*");
    }

    [Fact]
    public void Jwt_scheme_throws_when_secret_too_short()
    {
        var (builder, authCfg, auth, _) = BuildSetup("""
        {
            "Schemes": {
                "weak_secret": {
                    "Type": "Jwt",
                    "Enabled": true,
                    "JwtSecret": "tooshort"
                }
            }
        }
        """);

        var act = () => builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*JwtSecret must be at least 32 characters*");
    }

    [Fact]
    public void Jwt_scheme_validation_field_overrides_apply()
    {
        // Per-scheme overrides for ValidateIssuer / ValidateAudience flow into the registered
        // JwtBearerOptions' TokenValidationParameters.
        var (builder, authCfg, auth, services) = BuildSetup("""
        {
            "JwtSecret": "shared-root-secret-at-least-32-chars-long",
            "Schemes": {
                "scoped": {
                    "Type": "Jwt",
                    "Enabled": true,
                    "JwtIssuer": "https://issuer.example",
                    "JwtAudience": "https://audience.example",
                    "JwtValidateIssuer": true,
                    "JwtValidateAudience": true
                }
            }
        }
        """);
        builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        var options = GetJwtOptions(services, "scoped");
        options.TokenValidationParameters.ValidIssuer.Should().Be("https://issuer.example");
        options.TokenValidationParameters.ValidAudience.Should().Be("https://audience.example");
        options.TokenValidationParameters.ValidateIssuer.Should().BeTrue();
        options.TokenValidationParameters.ValidateAudience.Should().BeTrue();
    }

    [Fact]
    public void Jwt_scheme_refresh_path_collision_with_main_throws()
    {
        var (builder, authCfg, auth, _) = BuildSetup("""
        {
            "JwtRefreshPath": "/api/jwt/refresh",
            "Schemes": {
                "scoped": {
                    "Type": "Jwt",
                    "Enabled": true,
                    "JwtSecret": "shared-root-secret-at-least-32-chars-long",
                    "JwtRefreshPath": "/api/jwt/refresh"
                }
            }
        }
        """);

        var act = () => builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*JwtRefreshPath='/api/jwt/refresh' collides*");
    }

    // ------------------------------------------------------------------------
    // Multi-type interactions
    // ------------------------------------------------------------------------

    [Fact]
    public void Mixed_scheme_types_register_independently_in_one_pass()
    {
        var (builder, authCfg, auth, services) = BuildSetup("""
        {
            "JwtSecret": "shared-root-secret-at-least-32-chars-long",
            "Schemes": {
                "short_cookie": { "Type": "Cookies",     "Enabled": true, "CookieValid": "1 hour" },
                "api_token":    { "Type": "BearerToken", "Enabled": true, "BearerTokenExpire": "30 minutes" },
                "admin_jwt":    { "Type": "Jwt",         "Enabled": true, "JwtExpire": "5 minutes" }
            }
        }
        """);
        builder.RegisterAuthSchemes(auth, authCfg, "Cookies", "BearerToken", "Bearer");

        var sp = services.BuildServiceProvider();
        var schemeProvider = sp.GetRequiredService<IAuthenticationSchemeProvider>();

        schemeProvider.GetSchemeAsync("short_cookie").GetAwaiter().GetResult().Should().NotBeNull();
        schemeProvider.GetSchemeAsync("api_token").GetAwaiter().GetResult().Should().NotBeNull();
        schemeProvider.GetSchemeAsync("admin_jwt").GetAwaiter().GetResult().Should().NotBeNull();

        builder.AdditionalBearerTokenConfigs.Should().ContainSingle(c => c.Scheme == "api_token");
        builder.AdditionalJwtTokenConfigs.Should().ContainSingle(c => c.Scheme == "admin_jwt");
    }
}
