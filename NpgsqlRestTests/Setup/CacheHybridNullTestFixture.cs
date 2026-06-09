using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NpgsqlRestClient;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("CacheHybridNullFixture")]
public class CacheHybridNullFixtureCollection : ICollectionFixture<CacheHybridNullTestFixture> { }

/// <summary>
/// Fixture for the HybridCache null-parameter cache-key fix. Wires the real <see cref="HybridCacheWrapper"/>
/// as the routine cache backend (HybridCache rejected keys containing the old <c>\x00</c> null marker with
/// "Cache key contains invalid content", silently bypassing the cache). Functions live in the shared
/// <c>public</c> schema under the <c>chn_</c> prefix so only they are mapped.
/// </summary>
public class CacheHybridNullTestFixture : IDisposable
{
    private readonly WebApplication _app;

    public string ServerAddress { get; }

    public CacheHybridNullTestFixture()
    {
        var connectionString = Database.Create();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
#pragma warning disable EXTEXP0018 // HybridCache is experimental
        builder.Services.AddHybridCache();
#pragma warning restore EXTEXP0018
        _app = builder.Build();

        var hybridCache = _app.Services.GetRequiredService<HybridCache>();

        _app.UseNpgsqlRest(new(connectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "chn_%",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
            CacheOptions = new()
            {
                DefaultRoutineCache = new HybridCacheWrapper(hybridCache),
            }
        });

        _app.StartAsync().GetAwaiter().GetResult();
        ServerAddress = _app.Urls.First();
    }

    public HttpClient CreateClient()
        => new() { BaseAddress = new Uri(ServerAddress), Timeout = TimeSpan.FromMinutes(5) };

#pragma warning disable CA1816
    public void Dispose()
#pragma warning restore CA1816
    {
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
    }
}
