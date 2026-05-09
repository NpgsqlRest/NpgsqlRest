using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NpgsqlRest.Auth;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ClaimAutoBindWarningsTests()
    {
        // The cab_* routines are loaded by ClaimAutoBindTestFixture (good config). The cabx_*
        // routine is *not* loaded by that fixture — it is only referenced by the dedicated
        // fail-fast test, which spins up its own WebApplication to assert UseNpgsqlRest throws
        // when a non-text parameter is in ParameterNameClaimsMapping.
        script.Append("""

        create function cab_get_user_id(
            _user_id text
        ) returns text
        language sql immutable as $$
            select _user_id;
        $$;

        create function cab_get_user_id_post(
            _user_id text,
            _other text = null
        ) returns text
        language sql immutable as $$
            select _user_id || ':' || coalesce(_other, '');
        $$;
        comment on function cab_get_user_id_post(text, text) is 'HTTP POST';

        create function cabx_create_widget(
            _company_id int,
            _widget_name text
        ) returns int
        language sql immutable as $$
            select _company_id;
        $$;
        """);
    }
}

[Collection("ClaimAutoBindTestFixture")]
public class ClaimAutoBindWarningsTests(ClaimAutoBindTestFixture test)
{
    private static IEnumerable<LogEntry> WarningsContaining(IEnumerable<LogEntry> logs, params string[] needles) =>
        logs.Where(e => e.Level == LogLevel.Warning && needles.All(n => e.Message.Contains(n)));

    [Fact]
    public async Task QueryStringSuppliesClaimMappedParam_LogsRuntimeWarningAndClaimWins()
    {
        // Sign in (cookie carries name_identifier=42, company_id=7).
        using var client = test.CreateClient();
        (await client.GetAsync("/cab-login")).StatusCode.Should().Be(HttpStatusCode.OK);

        var before = test.CurrentLogCount;

        // Request supplies userId=999 in the query string. The endpoint is auto-bound from the
        // name_identifier claim (=42), so the claim must win and the body value is silently dropped.
        // The fix: a WARN is emitted naming the endpoint, parameter, source ("query"), and claim
        // so the developer can see the collision in logs.
        using var response = await client.GetAsync("/api/cab-get-user-id/?userId=999");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("42",
            "claim must always win over a request-supplied value for an auto-bound parameter");

        var matches = WarningsContaining(test.RequestLogsSince(before),
            "/api/cab-get-user-id",
            "_user_id",
            "query",
            "name_identifier").ToList();

        matches.Should().ContainSingle();
    }

    [Fact]
    public async Task BodySuppliesClaimMappedParam_LogsRuntimeWarningAndClaimWins()
    {
        using var client = test.CreateClient();
        (await client.GetAsync("/cab-login")).StatusCode.Should().Be(HttpStatusCode.OK);

        var before = test.CurrentLogCount;

        // POST endpoint: body sends {"userId": "999", "other": "x"}. The claim auto-bind for _user_id
        // overrides 999, but we log a WARN so the developer notices the silent drop.
        using var response = await client.PostAsync(
            "/api/cab-get-user-id-post/",
            new StringContent("{\"userId\":\"999\",\"other\":\"x\"}", System.Text.Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("42:x",
            "claim must win for _user_id, but the unmapped _other parameter must still come from the body");

        var matches = WarningsContaining(test.RequestLogsSince(before),
            "/api/cab-get-user-id-post",
            "_user_id",
            "body",
            "name_identifier").ToList();

        matches.Should().ContainSingle();
    }

    [Fact]
    public async Task NoCollisionMeansNoRuntimeWarning()
    {
        using var client = test.CreateClient();
        (await client.GetAsync("/cab-login")).StatusCode.Should().Be(HttpStatusCode.OK);

        var before = test.CurrentLogCount;

        // Request omits userId entirely — no collision, so no WARN should be emitted.
        using var response = await client.GetAsync("/api/cab-get-user-id/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("42");

        var matches = WarningsContaining(test.RequestLogsSince(before),
            "_user_id",
            "auto-bound from claim").ToList();

        matches.Should().BeEmpty(
            "no WARN must fire when the request does not supply a value for the claim-mapped parameter");
    }
}

/// <summary>
/// Fail-fast test: a parameter listed in <see cref="NpgsqlRestAuthenticationOptions.ParameterNameClaimsMapping"/>
/// must be declared with a text-compatible PostgreSQL type. Otherwise every authenticated request
/// would crash with a misleading <see cref="System.InvalidCastException"/> from Npgsql ("Writing
/// values of 'System.String' is not supported for parameters having NpgsqlDbType '<X>'"). The
/// configuration has no valid runtime, so we surface it at startup instead of letting it ship.
/// </summary>
public class ClaimAutoBindFailFastTests
{
    [Fact]
    public void NonTextClaimMappedParameter_ThrowsAtStartup()
    {
        // Make sure the cabx_create_widget routine (with `_company_id int`) exists in the DB — it is
        // shared across the suite via the static SQL script. We then build a minimal WebApplication
        // that points NpgsqlRest at this routine with `_company_id` in ParameterNameClaimsMapping.
        // Construction must throw — the misdeclaration is caught before a request ever arrives.
        var connectionString = Database.Create();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication().AddCookie();

        var app = builder.Build();

        Action act = () => app.UseNpgsqlRest(new(connectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "cabx[_]create[_]widget",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
            AuthenticationOptions = new()
            {
                UseUserParameters = true,
                ParameterNameClaimsMapping = new()
                {
                    { "_company_id", "company_id" },
                },
            },
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*_company_id*company_id*not text-compatible*");

        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Fact]
    public void TextClaimMappedParameter_DoesNotThrowAtStartup()
    {
        // Sanity check: the same wiring with a text-typed claim-mapped parameter must build cleanly.
        // (The runtime collision tests in ClaimAutoBindWarningsTests rely on this fixture working.)
        var connectionString = Database.Create();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthentication().AddCookie();

        var app = builder.Build();

        Action act = () => app.UseNpgsqlRest(new(connectionString)
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "cab[_]get[_]user[_]id",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
            AuthenticationOptions = new()
            {
                UseUserParameters = true,
                ParameterNameClaimsMapping = new()
                {
                    { "_user_id", "name_identifier" },
                },
            },
        });

        act.Should().NotThrow();

        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
