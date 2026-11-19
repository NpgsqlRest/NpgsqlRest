using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("RequestContextBatchTestFixture")]
public class RequestContextBatchTestFixtureCollection : ICollectionFixture<RequestContextBatchTestFixture> { }

/// <summary>
/// For the request context that is sent together with the routine (RequestContextBatch). The pool has exactly one
/// connection and it is never reset on close, so every request runs in the same PostgreSQL session: anything a
/// request left on its connection would be visible to the next request.
/// </summary>
public class RequestContextBatchTestFixture : IDisposable
{
    private readonly WebApplication _app;

    public string ServerAddress { get; }

    public NpgsqlRestOptions Options { get; }

    public RequestContextBatchTestFixture()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(Database.Create())
        {
            NoResetOnClose = true,
            MaxPoolSize = 1,
            // Its own pool: Npgsql pools by connection string, and another fixture with the same one would share this
            // fixture's only connection - and whatever that fixture's requests leave on it.
            ApplicationName = "request-context-batch-tests",
        }.ConnectionString;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication().AddCookie();

        _app = builder.Build();

        _app.MapGet("/rcb-login", () => Results.SignIn(new ClaimsPrincipal(new ClaimsIdentity(
            claims:
            [
                new Claim("user_id", "123"),
                new Claim("user_name", "admin"),
            ],
            authenticationType: CookieAuthenticationDefaults.AuthenticationScheme))));

        Options = new(connectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "rcb[_]%",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
        };
        _app.UseNpgsqlRest(Options);

        _app.StartAsync().GetAwaiter().GetResult();
        ServerAddress = _app.Urls.First();
    }

    /// <summary>A client that is logged in as user_id 123, user_name admin.</summary>
    public async Task<HttpClient> LoggedInClientAsync()
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = true, CookieContainer = new System.Net.CookieContainer() })
        {
            BaseAddress = new Uri(ServerAddress)
        };
        using var login = await client.GetAsync("/rcb-login");
        login.EnsureSuccessStatusCode();
        return client;
    }

    public HttpClient AnonymousClient() => new() { BaseAddress = new Uri(ServerAddress) };

#pragma warning disable CA1816
    public void Dispose()
#pragma warning restore CA1816
    {
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }
}
