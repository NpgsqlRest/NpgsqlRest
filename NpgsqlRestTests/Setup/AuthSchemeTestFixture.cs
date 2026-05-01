using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NpgsqlRest.Auth;
using NpgsqlRestClient;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("AuthSchemeTestFixture")]
public class AuthSchemeTestFixtureCollection : ICollectionFixture<AuthSchemeTestFixture> { }

/// <summary>
/// End-to-end fixture for <c>Auth:Schemes</c> — verifies that the full login pipeline (PostgreSQL
/// function returns a scheme name in its `scheme` column → NpgsqlRest passes it to <c>Results.SignIn</c>
/// for cookies / <see cref="JwtLoginHandler"/> for JWT → ASP.NET Core writes a cookie or token-pair
/// scoped to that scheme's options) actually delivers the scheme-specific behavior end-to-end.
///
/// The fixture mirrors what <c>Builder.RegisterAuthSchemes</c> would produce from this config:
///
/// <code>
/// "Auth": {
///   "CookieAuth": true,
///   "CookieValid": "14 days",
///   "JwtAuth": true,
///   "JwtSecret": "...32+chars...",
///   "Schemes": {
///     "ast_short_session": { "Type": "Cookies", "CookieValid": "1 hour", "CookieMultiSessions": false },
///     "ast_jwt_admin":     { "Type": "Jwt",     "JwtSecret": "...different-32+chars...", "JwtExpire": "5 minutes" }
///   }
/// }
/// </code>
///
/// We don't go through Builder.cs here because the registration paths are already covered by
/// AuthSchemeRegistrationTests; this fixture exercises the integration with NpgsqlRest's LoginHandler
/// for both cookie and JWT scheme types.
/// </summary>
public class AuthSchemeTestFixture : IDisposable
{
    private readonly WebApplication _app;

    public string ServerAddress { get; }

    /// <summary>
    /// Distinct JWT signing secrets per scheme — proves the per-scheme secret override works.
    /// Both ≥32 chars for HS256.
    /// </summary>
    public const string MainJwtSecret = "main-scheme-secret-at-least-32-chars-long-x";
    public const string AdminJwtSecret = "admin-scheme-secret-totally-different-32-chars";

    public AuthSchemeTestFixture()
    {
        var connectionString = Database.Create();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Main JwtTokenConfig — also registered with JwtLoginHandler so login functions returning the
        // main scheme name get tokens minted with this secret.
        var mainJwtConfig = new JwtTokenConfig
        {
            Scheme = JwtBearerDefaults.AuthenticationScheme,
            Secret = MainJwtSecret,
            Expire = TimeSpan.FromMinutes(60),
            RefreshExpire = TimeSpan.FromDays(7),
        };

        // Per-scheme JWT config — different secret, much shorter expiration. Mirrors what
        // RegisterJwtSchemeFromConfig would build from a Schemes:ast_jwt_admin config.
        var adminJwtConfig = new JwtTokenConfig
        {
            Scheme = "ast_jwt_admin",
            Secret = AdminJwtSecret,
            Expire = TimeSpan.FromMinutes(5),
            RefreshExpire = TimeSpan.FromHours(1),
        };

        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.ExpireTimeSpan = TimeSpan.FromDays(14);
                options.Cookie.Name = ".main-cookie";
                options.Cookie.MaxAge = TimeSpan.FromDays(14);
                options.Cookie.HttpOnly = true;
            })
            .AddCookie("ast_short_session", options =>
            {
                options.ExpireTimeSpan = TimeSpan.FromHours(1);
                options.Cookie.Name = ".short-cookie";
                options.Cookie.MaxAge = null; // session-only — MultiSessions=false
                options.Cookie.HttpOnly = true;
            })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = mainJwtConfig.GetTokenValidationParameters();
                options.SaveToken = true;
            })
            .AddJwtBearer("ast_jwt_admin", options =>
            {
                options.TokenValidationParameters = adminJwtConfig.GetTokenValidationParameters();
                options.SaveToken = true;
            });

        // Wire the multi-scheme JwtLoginHandler — the LoginHandler queries this when a function
        // returns one of the registered JWT scheme names.
        JwtLoginHandler.Initialize(mainJwtConfig);
        JwtLoginHandler.Register(adminJwtConfig);

        _app = builder.Build();

        _app.UseAuthentication();

        _app.UseNpgsqlRest(new(connectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "ast[_]%",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
            AuthenticationOptions = new()
            {
                DefaultAuthenticationType = CookieAuthenticationDefaults.AuthenticationScheme,
                CustomLoginHandler = async (context, principal, scheme) =>
                {
                    // Route JWT-scheme logins to JwtLoginHandler (token response), all others to the
                    // default cookie-style SignIn flow. Mirrors App.CreateJwtLoginHandler in production.
                    if (scheme is not null && JwtLoginHandler.IsScheme(scheme))
                    {
                        return await JwtLoginHandler.HandleLoginAsync(context, principal, scheme);
                    }
                    return false;
                }
            }
        });

        _app.StartAsync().GetAwaiter().GetResult();

        ServerAddress = _app.Urls.First();
    }

    /// <summary>
    /// Creates an HttpClient that does NOT auto-follow redirects (so we can inspect Set-Cookie on the
    /// login response directly) and DOES preserve cookies (for follow-up authenticated requests).
    /// </summary>
    public HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new System.Net.CookieContainer(),
            AllowAutoRedirect = false
        };
        return new HttpClient(handler) { BaseAddress = new Uri(ServerAddress), Timeout = TimeSpan.FromMinutes(5) };
    }

#pragma warning disable CA1816
    public void Dispose()
#pragma warning restore CA1816
    {
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }
}
