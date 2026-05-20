#pragma warning disable CS8602 // Dereference of a possibly null reference.
namespace NpgsqlRestTests;

public static partial class Database
{
    public static void HostTimeZoneIndependenceTests()
    {
        script.Append(@"
create function host_tz_echo_timestamp(_ts timestamp)
returns json
language sql
as $$ select json_build_object('v', _ts) $$;

create function host_tz_echo_timestamptz(_ts timestamptz)
returns json
language sql
as $$ select json_build_object('v', _ts) $$;

create function host_tz_echo_time(_t time)
returns json
language sql
as $$ select json_build_object('v', _t) $$;

create function host_tz_echo_timetz(_t timetz)
returns json
language sql
as $$ select json_build_object('v', _t) $$;

create function host_tz_echo_date(_d date)
returns json
language sql
as $$ select json_build_object('v', _d) $$;
");
    }
}

[Collection("TestFixture")]
public class HostTimeZoneIndependenceTests(TestFixture test)
{
    private static async Task<string> EchoAsync(HttpClient client, string endpoint, string body)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(endpoint, content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        var node = JsonNode.Parse(raw);
        return node["v"].GetValue<string>();
    }

    [Fact]
    public async Task TimestampTz_ZSuffix_RoundTripsAsUtc()
    {
        var v = await EchoAsync(test.Client, "/api/host-tz-echo-timestamptz/",
            """{"ts":"2026-05-20T06:00:00Z"}""");
        v.Should().Be("2026-05-20T06:00:00+00:00");
    }

    [Fact]
    public async Task TimestampTz_NumericOffset_ConvertedToUtc()
    {
        // 06:00+02:00 == 04:00 UTC
        var v = await EchoAsync(test.Client, "/api/host-tz-echo-timestamptz/",
            """{"ts":"2026-05-20T06:00:00+02:00"}""");
        v.Should().Be("2026-05-20T04:00:00+00:00");
    }

    [Fact]
    public async Task TimestampTz_NaiveIso_AssumedUtc()
    {
        // No offset, no Z. AssumeUniversal treats as UTC, not host-local.
        var v = await EchoAsync(test.Client, "/api/host-tz-echo-timestamptz/",
            """{"ts":"2026-05-20T06:00:00"}""");
        v.Should().Be("2026-05-20T06:00:00+00:00");
    }

    [Fact]
    public async Task Timestamp_ZSuffix_StoredAsUtcClockTimeNaively()
    {
        // `timestamp without time zone` has no offset semantics. Convention here:
        // the digits inside the Z-marked string become the naive wall-clock value.
        var v = await EchoAsync(test.Client, "/api/host-tz-echo-timestamp/",
            """{"ts":"2026-05-20T06:00:00Z"}""");
        v.Should().Be("2026-05-20T06:00:00");
    }

    [Fact]
    public async Task TimeTz_ZSuffix_RoundTripsAsUtc()
    {
        var v = await EchoAsync(test.Client, "/api/host-tz-echo-timetz/",
            """{"t":"06:00:00Z"}""");
        v.Should().Be("06:00:00+00");
    }

    [Fact]
    public async Task Time_ZSuffix_StoredAsUtcClockTime()
    {
        var v = await EchoAsync(test.Client, "/api/host-tz-echo-time/",
            """{"t":"06:00:00Z"}""");
        v.Should().Be("06:00:00");
    }

    // The legacy `TryParseDate` rejected Z- and offset-bearing strings outright. The
    // 3.16.0 enhancement falls back to a UTC DateTime parse and extracts the date
    // portion, matching the JsonTimestampsAreUtc semantic.
    [Fact]
    public async Task Date_BareDateString_StillRoundTrips()
    {
        var v = await EchoAsync(test.Client, "/api/host-tz-echo-date/",
            """{"d":"2026-05-20"}""");
        v.Should().Be("2026-05-20");
    }

    [Fact]
    public async Task Date_NaiveDateTimeString_TakesDatePortion()
    {
        var v = await EchoAsync(test.Client, "/api/host-tz-echo-date/",
            """{"d":"2026-05-20T06:00:00"}""");
        v.Should().Be("2026-05-20");
    }

    [Fact]
    public async Task Date_ZSuffix_TakesUtcDate()
    {
        // 2026-05-20T03:00:00Z is still May 20 in UTC.
        var v = await EchoAsync(test.Client, "/api/host-tz-echo-date/",
            """{"d":"2026-05-20T03:00:00Z"}""");
        v.Should().Be("2026-05-20");
    }

    [Fact]
    public async Task Date_NumericOffset_ConvertsToUtcDateFirst()
    {
        // 2026-05-21T01:30:00+02:00 == 2026-05-20T23:30:00 UTC → date is 2026-05-20.
        // On a non-UTC host the legacy mode would shift this differently; here we
        // assert the UTC interpretation that the default mode produces.
        var v = await EchoAsync(test.Client, "/api/host-tz-echo-date/",
            """{"d":"2026-05-21T01:30:00+02:00"}""");
        v.Should().Be("2026-05-20");
    }

    [Fact]
    public async Task Date_LateUtcEvening_DoesNotRollToNextLocalDay()
    {
        // 2026-05-20T23:00:00Z is May 20 in UTC. The legacy `DateOnly.TryParse`
        // would have rejected this entirely; the new fallback resolves it as
        // 2026-05-20 regardless of the host's local TZ.
        var v = await EchoAsync(test.Client, "/api/host-tz-echo-date/",
            """{"d":"2026-05-20T23:00:00Z"}""");
        v.Should().Be("2026-05-20");
    }
}
