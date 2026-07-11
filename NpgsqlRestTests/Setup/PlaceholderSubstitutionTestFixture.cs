using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("PlaceholderSubstitutionFixture")]
public class PlaceholderSubstitutionFixtureCollection : ICollectionFixture<PlaceholderSubstitutionTestFixture> { }

/// <summary>
/// Fixture for `{name}` parameter-value substitution in annotation values: case-insensitive matching
/// (request-time), and the build-time warning for an unknown placeholder. Live server + captured startup
/// logs. Functions are in the shared `public` schema under the `phsub_` prefix.
/// </summary>
public class PlaceholderSubstitutionTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly LogCollector _logCollector = new();

    public string ServerAddress { get; }
    public IReadOnlyList<LogEntry> StartupLogs { get; }

    public PlaceholderSubstitutionTestFixture()
    {
        var connectionString = Database.Create();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddProvider(new CollectingLoggerProvider(_logCollector));

        _app = builder.Build();

        _app.UseNpgsqlRest(new(connectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "phsub[_]%",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
            // Allowlisted env vars available to {name} substitution. Case-insensitive (as the client builds it).
            // `region` deliberately collides with the phsub_collision routine's `_region` parameter to test precedence.
            // Mirrors the client convention: a "!NAME" key marks a name that resolved to a real value
            // (drives the {!NAME}/{!NAME:fallback} forms); UNSET_VAR is allowlisted-but-unresolved
            // (plain key only, empty value), so its inline fallback applies.
            SubstitutionEnvironmentVariables = new(StringComparer.OrdinalIgnoreCase)
            {
                ["SERVER_NAME"] = "pod-7",
                ["!SERVER_NAME"] = "pod-7",
                ["region"] = "env-region",
                ["!region"] = "env-region",
                ["UNSET_VAR"] = "",
            },
        });

        _app.StartAsync().GetAwaiter().GetResult();
        StartupLogs = _logCollector.Snapshot();
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
