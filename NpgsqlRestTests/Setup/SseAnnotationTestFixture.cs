using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NpgsqlRest;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("SseAnnotationTestFixture")]
public class SseAnnotationTestFixtureCollection : ICollectionFixture<SseAnnotationTestFixture> { }

/// <summary>
/// Dedicated fixture for the <c>@sse</c> / <c>@sse_publish</c> / <c>@sse_subscribe</c> decomposition
/// (Phase B.1) and the unbound-RAISE warning (Phase A). Loads only the <c>sset_*</c> routines so the
/// fixture's broadcaster state and per-endpoint dedupe set don't fight with other fixtures.
///
/// Wires an in-memory <see cref="ILoggerProvider"/> so tests can assert that the unbound-RAISE
/// warning fires (or does not fire) for specific endpoint paths.
/// </summary>
public class SseAnnotationTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly LogCollector _logCollector = new();

    public string ServerAddress { get; }

    public IEnumerable<LogEntry> LogsSince(int afterIndex) => _logCollector.Snapshot().Skip(afterIndex);
    public int CurrentLogCount => _logCollector.Count;

    public SseAnnotationTestFixture()
    {
        // Reset the per-process dedupe state so prior fixtures' warnings don't suppress ours. The
        // warner is a process-wide static (the warning is "once per endpoint per process") and other
        // fixtures may have run RAISE INFOs through unbound endpoints already.
        SseUnboundWarner.Reset();

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
            NameSimilarTo = "sset[_]%",
            CommentsMode = CommentsMode.ParseAll,
            // Default forwarding level is INFO; tests assert level matching against this value.
            DefaultSseEventNoticeLevel = PostgresNoticeLevels.INFO,
            // We want to verify warnings fire — the flag is true by default but we set it explicitly
            // here so the fixture's intent is visible.
            WarnUnboundSseNotices = true,
            // Off by default in the fixture so the unbound-warn tests can assert their warning is
            // the only matching log entry without filtering past notice-log noise.
            LogConnectionNoticeEvents = false,
        });

        _app.StartAsync().GetAwaiter().GetResult();
        ServerAddress = _app.Urls.First();
    }

    public HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { UseCookies = false };
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
