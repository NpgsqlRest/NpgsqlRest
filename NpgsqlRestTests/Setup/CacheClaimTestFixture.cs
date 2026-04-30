using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NpgsqlRest.Auth;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("CacheClaimTestFixture")]
public class CacheClaimTestFixtureCollection : ICollectionFixture<CacheClaimTestFixture> { }

/// <summary>
/// Fixture for verifying that cache keys correctly include claim-resolved parameter values.
/// Provides two distinct login endpoints (`/cct-login-a` → user_a, `/cct-login-b` → user_b)
/// so a single test can switch between identities and observe whether the cache distinguishes them.
///
/// Cache backend: in-memory `RoutineCache` (default). User-parameters mapping is configured so
/// the `_user_id` parameter is auto-populated from the `name_identifier` claim when an endpoint
/// has the `user_parameters` annotation.
/// </summary>
public class CacheClaimTestFixture : IDisposable
{
    private readonly WebApplication _app;

    public string ServerAddress { get; }

    public CacheClaimTestFixture()
    {
        var connectionString = Database.Create();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddAuthentication().AddCookie();

        _app = builder.Build();

        // Two distinct identities. Each /login route signs in as one user and sets the auth cookie.
        _app.MapGet("/cct-login-a", () => Results.SignIn(new ClaimsPrincipal(new ClaimsIdentity(
            claims: new[] { new Claim("name_identifier", "user_a") },
            authenticationType: CookieAuthenticationDefaults.AuthenticationScheme))));

        _app.MapGet("/cct-login-b", () => Results.SignIn(new ClaimsPrincipal(new ClaimsIdentity(
            claims: new[] { new Claim("name_identifier", "user_b") },
            authenticationType: CookieAuthenticationDefaults.AuthenticationScheme))));

        _app.UseNpgsqlRest(new(connectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "cct_%",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
            AuthenticationOptions = new()
            {
                DefaultUserIdClaimType = "name_identifier",
                ParameterNameClaimsMapping = new()
                {
                    { "_user_id", "name_identifier" }
                },
            },
            CacheOptions = new()
            {
                DefaultRoutineCache = new RoutineCache(),
                MemoryCachePruneIntervalSeconds = 3600
            }
        });

        _app.StartAsync().GetAwaiter().GetResult();

        ServerAddress = _app.Urls.First();
    }

    /// <summary>
    /// Creates a fresh HttpClient bound to this fixture. Each client has its own cookie container,
    /// so two clients can hold independent auth cookies (one signed in as user_a, the other as user_b).
    /// </summary>
    public HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { UseCookies = true, CookieContainer = new System.Net.CookieContainer() };
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
