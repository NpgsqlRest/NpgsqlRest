using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NpgsqlRest.Auth;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("ClaimAutoBindTestFixture")]
public class ClaimAutoBindTestFixtureCollection : ICollectionFixture<ClaimAutoBindTestFixture> { }

/// <summary>
/// Fixture for verifying claim → parameter auto-bind diagnostics:
///   1. A startup-time WARN when a parameter listed in <c>ParameterNameClaimsMapping</c> is declared
///      with a non-text PostgreSQL type. Such a parameter would crash with a misleading
///      <c>InvalidCastException</c> from Npgsql at request time because claim values are strings.
///   2. A request-time WARN when the body or query string supplies a value for a parameter that is
///      already auto-bound from a claim — the request value would otherwise be silently discarded.
///
/// The fixture wires an in-memory <see cref="ILoggerProvider"/> so tests can assert that the
/// expected warning was emitted. The auth mapping intentionally includes <c>_company_id</c> →
/// <c>company_id</c> so the test SQL can declare a non-text claim-mapped parameter.
/// </summary>
public class ClaimAutoBindTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly LogCollector _logCollector = new();

    public string ServerAddress { get; }

    public IReadOnlyList<LogEntry> StartupLogs { get; }

    public IEnumerable<LogEntry> RequestLogsSince(int afterIndex) => _logCollector.Snapshot().Skip(afterIndex);

    public int CurrentLogCount => _logCollector.Count;

    public ClaimAutoBindTestFixture()
    {
        var connectionString = Database.Create();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddProvider(new CollectingLoggerProvider(_logCollector));

        builder.Services.AddAuthentication().AddCookie();

        _app = builder.Build();

        // Login route signs the client in with claims that exercise both the default mappings
        // (name_identifier) and the project-specific mapping (company_id) used by the test
        // routines. company_id is a stringified integer so any text-typed claim parameter
        // succeeds, while a non-text parameter would crash if a request hit it.
        _app.MapGet("/cab-login", () => Results.SignIn(new ClaimsPrincipal(new ClaimsIdentity(
            claims: new[]
            {
                new Claim("name_identifier", "42"),
                new Claim("company_id", "7"),
            },
            authenticationType: CookieAuthenticationDefaults.AuthenticationScheme))));

        _app.UseNpgsqlRest(new(connectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "cab[_]%",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
            AuthenticationOptions = new()
            {
                DefaultUserIdClaimType = "name_identifier",
                UseUserParameters = true,
                ParameterNameClaimsMapping = new()
                {
                    { "_user_id", "name_identifier" },
                    { "_company_id", "company_id" },
                },
            },
        });

        _app.StartAsync().GetAwaiter().GetResult();

        // Snapshot the logs emitted during build/startup so tests can inspect them without races
        // against later request-time entries.
        StartupLogs = _logCollector.Snapshot();

        ServerAddress = _app.Urls.First();
    }

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

public sealed record LogEntry(LogLevel Level, string Category, string Message);

internal sealed class LogCollector
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public void Add(LogEntry entry) => _entries.Enqueue(entry);

    public IReadOnlyList<LogEntry> Snapshot() => _entries.ToArray();

    public int Count => _entries.Count;
}

internal sealed class CollectingLoggerProvider(LogCollector collector) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CollectingLogger(categoryName, collector);

    public void Dispose() { }
}

internal sealed class CollectingLogger(string category, LogCollector collector) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        collector.Add(new LogEntry(logLevel, category, formatter(state, exception)));
    }
}
