using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("PerConnectionDiscoveryFixture")]
public class PerConnectionDiscoveryFixtureCollection : ICollectionFixture<PerConnectionDiscoveryTestFixture> { }

/// <summary>
/// Fixture for per-connection routine discovery: two RoutineSources, one on the main test database
/// (shared schema, `pcd_` prefix) and one bound to a REAL second database with different metadata
/// (<see cref="Database.CreateSecondDatabase"/>). Covers: endpoints execute on the connection they
/// were discovered from, `connection` annotation override, cross-source path collision (last wins +
/// warning), and per-database composite type cache correctness. Live server + captured startup logs.
/// </summary>
public class PerConnectionDiscoveryTestFixture : IDisposable
{
    // The second database's ENTIRE schema - deliberately different from the main test database:
    // pcd_alt_only exists ONLY here; pcd_shared exists on both (path collision); pcd_comp has an
    // extra field compared to the main database's same-named composite type.
    private const string AltDatabaseScript = """
        create function pcd_alt_only(_x int) returns text language sql as 'select current_database()';
        comment on function pcd_alt_only(int) is 'HTTP GET';

        create function pcd_alt_override() returns text language sql as 'select current_setting(''application_name'')';
        comment on function pcd_alt_override() is '
        HTTP GET
        connection alt2';

        create function pcd_shared() returns text language sql as 'select ''from-alt''';
        comment on function pcd_shared() is 'HTTP GET';

        create type pcd_comp as (a int, b text, c int);
        create function pcd_alt_comp() returns table (val pcd_comp) language sql as 'select row(1, ''x'', 2)::pcd_comp';
        comment on function pcd_alt_comp() is 'HTTP GET';
        """;

    private readonly WebApplication _app;
    private readonly LogCollector _logCollector = new();

    public string ServerAddress { get; }
    public IReadOnlyList<LogEntry> StartupLogs { get; }

    public PerConnectionDiscoveryTestFixture()
    {
        var mainConnectionString = Database.Create();
        var altConnectionString = Database.CreateSecondDatabase("pcd_alt", AltDatabaseScript);
        var alt2ConnectionString = new Npgsql.NpgsqlConnectionStringBuilder(altConnectionString)
        {
            ApplicationName = "alt2"
        }.ConnectionString;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddProvider(new CollectingLoggerProvider(_logCollector));

        _app = builder.Build();

        _app.UseNpgsqlRest(new(mainConnectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "pcd[_]%",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
            ConnectionStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["alt"] = altConnectionString,
                ["alt2"] = alt2ConnectionString,
            },
            // One source per connection - the "alt" source discovers FROM and executes ON the second
            // database. Order matters for the collision test: alt registers last and wins pcd_shared.
            EndpointSources =
            [
                new RoutineSource { NestedJsonForCompositeTypes = true },
                new RoutineSource { ConnectionName = "alt", NestedJsonForCompositeTypes = true },
            ],
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
