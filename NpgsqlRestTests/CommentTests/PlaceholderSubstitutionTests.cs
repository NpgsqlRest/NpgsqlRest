using Microsoft.Extensions.Logging;
using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    // `{name}` parameter-value substitution in annotation values. Functions in the shared `public` schema,
    // `phsub_` prefix (the PlaceholderSubstitutionTestFixture maps only `phsub_%`).
    public static void PlaceholderSubstitutionTests()
    {
        script.Append(@"
-- Case-insensitive matching: placeholder {_FILE} (upper) resolves param _file. (#1)
create function phsub_casing(_file text) returns text language sql as 'select ''ok''';
comment on function phsub_casing(text) is '
HTTP GET
X-Filename: {_FILE}';

-- Unknown placeholder {_fil} (typo of _file) -> build-time warning. (#2)
create function phsub_typo(_file text) returns text language sql as 'select ''ok''';
comment on function phsub_typo(text) is '
HTTP GET
Content-Disposition: attachment; filename={_fil}';

-- Non-identifier braces ({0}) must NOT warn (heuristic skips literal/JSON-like braces). (#2)
create function phsub_nonident(_x int) returns text language sql as 'select ''ok''';
comment on function phsub_nonident(int) is '
HTTP GET
Cache-Control: max-age={0}';

-- env var resolves into a response header (per-pod server name); case-insensitive form too. (#3 env)
create function phsub_env(_x int) returns text language sql as 'select ''ok''';
comment on function phsub_env(int) is '
HTTP GET
X-Server: {SERVER_NAME}
X-Server-Lc: {server_name}';

-- name collision: a routine parameter wins over an allowlisted env var of the same name. (#3 precedence)
create function phsub_collision(_region text) returns text language sql as 'select ''ok''';
comment on function phsub_collision(text) is '
HTTP GET
X-Region: {region}';
");
    }
}

[Collection("PlaceholderSubstitutionFixture")]
public class PlaceholderSubstitutionTests(PlaceholderSubstitutionTestFixture test)
{
    // #1 — case-insensitive matching
    [Fact]
    public async Task Placeholder_matching_is_case_insensitive()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/phsub-casing/?file=q1.csv");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // {_FILE} (uppercase) resolved the _file parameter; pre-fix this stayed the literal "{_FILE}".
        response.Headers.GetValues("X-Filename").Single().Should().Be("q1.csv");
    }

    // #2 — unknown placeholder warns at build time
    [Fact]
    public void Unknown_placeholder_logs_a_build_time_warning()
    {
        var warning = test.StartupLogs.FirstOrDefault(l =>
            l.Level == LogLevel.Warning &&
            l.Message.Contains("parameter placeholder '_fil'") &&
            l.Message.Contains("no routine parameter matches"));
        warning.Should().NotBeNull("a placeholder that matches no parameter should warn so typos are caught");
    }

    // #2 — the heuristic must not flag non-identifier braces
    [Fact]
    public void Non_identifier_braces_do_not_warn()
    {
        test.StartupLogs.Any(l =>
            l.Level == LogLevel.Warning &&
            l.Message.Contains("parameter placeholder '0'"))
            .Should().BeFalse("{0} is not an identifier and must not be treated as a parameter placeholder");
    }

    // #3 — allowlisted env var resolves into a response header, case-insensitively
    [Fact]
    public async Task Allowlisted_env_var_resolves_in_a_response_header()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/phsub-env/?x=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Server").Single().Should().Be("pod-7");
        response.Headers.GetValues("X-Server-Lc").Single().Should().Be("pod-7");   // {server_name} matched SERVER_NAME
    }

    // #3 — an allowlisted env-var placeholder must not trigger the unknown-placeholder warning
    [Fact]
    public void Allowlisted_env_var_placeholder_does_not_warn()
    {
        test.StartupLogs.Any(l =>
            l.Level == LogLevel.Warning &&
            l.Message.Contains("parameter placeholder 'SERVER_NAME'"))
            .Should().BeFalse("an allowlisted env var is a valid placeholder and must not warn");
    }

    // #3 — a routine parameter wins over an allowlisted env var of the same name
    [Fact]
    public async Task Parameter_wins_over_env_var_on_name_collision()
    {
        using var client = test.CreateClient();
        using var response = await client.GetAsync("/api/phsub-collision/?region=us");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Region").Single().Should().Be("us");   // the param value, not "env-region"
    }
}
