using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    // Main-database side of the endpoint connection verification tests. `pcdw_` (Warn fixture) and
    // `pcdf_` (Fail test) - both excluded from the default fixture by the `pcd%` pattern.
    // The `connection` annotations route execution to second databases created by the fixtures:
    // pcdw_exists exists on the target database, pcdw_missing and pcdf_missing do not.
    public static void ConnectionVerificationTests()
    {
        script.Append("""
create function pcdw_exists() returns text language sql as 'select ''from-main-should-not-run''';
comment on function pcdw_exists() is '
HTTP GET
connection valtw';

create function pcdw_missing() returns text language sql as 'select ''x''';
comment on function pcdw_missing() is '
HTTP GET
connection valtw';

create function pcdf_missing() returns text language sql as 'select ''x''';
comment on function pcdf_missing() is '
HTTP GET
connection valtf';
""");
    }
}

[CollectionDefinition("ConnectionVerificationWarnFixture")]
public class ConnectionVerificationWarnFixtureCollection : ICollectionFixture<ConnectionVerificationWarnTestFixture> { }

/// <summary>
/// Fixture for VerifyEndpointConnections=Warn: endpoints annotated `connection valtw` are checked at
/// startup against a real second database that has pcdw_exists but NOT pcdw_missing.
/// </summary>
public class ConnectionVerificationWarnTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly LogCollector _logCollector = new();

    public string ServerAddress { get; }
    public IReadOnlyList<LogEntry> StartupLogs { get; }

    public ConnectionVerificationWarnTestFixture()
    {
        var mainConnectionString = Database.Create();
        var targetConnectionString = Database.CreateSecondDatabase("pcd_valtw", """
            create function pcdw_exists() returns text language sql as 'select ''ok-valtw''';
            """);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddProvider(new CollectingLoggerProvider(_logCollector));

        _app = builder.Build();
        _app.UseNpgsqlRest(new(mainConnectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "pcdw[_]%",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
            ConnectionStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["valtw"] = targetConnectionString,
            },
            EndpointConnectionVerification = EndpointConnectionVerification.Warn,
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

[Collection("ConnectionVerificationWarnFixture")]
public class ConnectionVerificationWarnTests(ConnectionVerificationWarnTestFixture test)
{
    [Fact]
    public void Missing_routine_on_target_connection_logs_a_startup_warning()
    {
        test.StartupLogs.Any(l =>
            l.Level == LogLevel.Warning &&
            l.Message.Contains("pcdw_missing") &&
            l.Message.Contains("valtw"))
            .Should().BeTrue("a routed endpoint whose routine is missing on the target must be reported at startup");
    }

    [Fact]
    public void Existing_routine_on_target_connection_does_not_warn()
    {
        test.StartupLogs.Any(l =>
            l.Level == LogLevel.Warning &&
            l.Message.Contains("pcdw_exists"))
            .Should().BeFalse("a routine present on the target connection must pass verification");
    }

    [Fact]
    public async Task Routed_endpoint_executes_on_the_target_connection()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/pcdw-exists/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // the annotation routes execution to the second database, whose body returns a different value
        (await response.Content.ReadAsStringAsync()).Should().Be("ok-valtw");
    }
}

/// <summary>
/// VerifyEndpointConnections=Fail: startup must throw when a routed endpoint's routine is missing on
/// the target connection. No fixture - the failure happens inside UseNpgsqlRest.
/// </summary>
public class ConnectionVerificationFailTests
{
    [Fact]
    public async Task Missing_routine_on_target_connection_fails_startup()
    {
        var mainConnectionString = Database.Create();
        var targetConnectionString = Database.CreateSecondDatabase("pcd_valtf", "select 1");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        await using var app = builder.Build();
        var act = () => app.UseNpgsqlRest(new(mainConnectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "pcdf[_]%",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
            ConnectionStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["valtf"] = targetConnectionString,
            },
            EndpointConnectionVerification = EndpointConnectionVerification.Fail,
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*pcdf_missing*")
            .And.Message.Should().Contain("valtf");
    }
}
