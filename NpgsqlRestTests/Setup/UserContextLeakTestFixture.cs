using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("UserContextLeakTestFixture")]
public class UserContextLeakTestFixtureCollection : ICollectionFixture<UserContextLeakTestFixture> { }

/// <summary>
/// Reproduces the pooled-connection identity leak: a pool of exactly one connection that is NOT reset on close
/// (the configuration the npgsqlrest client used to force), so consecutive requests share one session and any
/// session-level user_context setting left by one request is visible to the next.
/// </summary>
public class UserContextLeakTestFixture : IDisposable
{
    private readonly WebApplication _app;

    public string ServerAddress { get; }

    public UserContextLeakTestFixture()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(Database.Create())
        {
            NoResetOnClose = true,
            MaxPoolSize = 1,
        }.ConnectionString;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication().AddCookie();

        _app = builder.Build();

        _app.MapGet("/ucl-login", () => Results.SignIn(new ClaimsPrincipal(new ClaimsIdentity(
            claims:
            [
                new Claim("user_id", "123"),
                new Claim("user_name", "admin"),
            ],
            authenticationType: CookieAuthenticationDefaults.AuthenticationScheme))));

        _app.UseNpgsqlRest(new(connectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "ucl[_]%",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
            AuthenticationOptions = new()
            {
                ClaimsJsonContextKey = "request.user_claims",
            },
        });

        _app.StartAsync().GetAwaiter().GetResult();
        ServerAddress = _app.Urls.First();
    }

#pragma warning disable CA1816
    public void Dispose()
#pragma warning restore CA1816
    {
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }
}
