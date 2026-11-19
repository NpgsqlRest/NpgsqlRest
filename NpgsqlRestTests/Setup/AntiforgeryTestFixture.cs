using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NpgsqlRestClient;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("AntiforgeryTestFixture")]
public class AntiforgeryTestFixtureCollection : ICollectionFixture<AntiforgeryTestFixture> { }

/// <summary>
/// Antiforgery wired the way the npgsqlrest client does it when Antiforgery:Enabled is true:
/// AddAntiforgery + UseAntiforgery, and App.CreateAntiforgeryRouteHandler marking every NpgsqlRest endpoint as
/// requiring a token. /af-token issues the antiforgery cookie and returns the request token.
/// </summary>
public class AntiforgeryTestFixture : IDisposable
{
    public const string HeaderName = "X-Test-Csrf";
    private readonly WebApplication _app;

    public string ServerAddress { get; }

    public AntiforgeryTestFixture()
    {
        var connectionString = Database.Create();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddAntiforgery(options => options.HeaderName = HeaderName);

        _app = builder.Build();
        _app.UseAntiforgery();

        _app.MapGet("/af-token", (HttpContext context, IAntiforgery antiforgery) =>
            antiforgery.GetAndStoreTokens(context).RequestToken!);

        _app.UseNpgsqlRest(new(connectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "af[_]%",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
            RouteHandlerCreated = App.CreateAntiforgeryRouteHandler(antiForgeryUsed: true),
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
