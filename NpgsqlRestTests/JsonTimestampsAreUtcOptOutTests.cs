#pragma warning disable CS8602 // Dereference of a possibly null reference.
using NpgsqlTypes;

namespace NpgsqlRestTests;

// Unit-level coverage for the `JsonTimestampsAreUtc` opt-out toggle on ParameterParsers.
// Visible behavioral differences between the two modes only manifest on non-UTC hosts
// (on a UTC host, the host-local-conversion of legacy mode is a no-op and the wire-level
// output is identical to the new mode). These tests therefore verify the *plumbing*: the
// flag is settable, restores correctly, and the parsers continue to produce valid results
// in both modes. The genuine non-UTC behavioral coverage belongs in a CI matrix step that
// re-runs the suite under `TZ=America/Los_Angeles` (or similar) — there, the legacy mode
// produces a host-offset-shifted value while the default mode produces the correct UTC
// instant.
// [Collection("TestFixture")] serializes against the integration-test collection so the
// static `ParameterParsers.JsonTimestampsAreUtc` flag mutations below cannot race with
// HTTP tests that exercise the parsers in another collection.
[Collection("TestFixture")]
public class JsonTimestampsAreUtcOptOutTests
{
    [Fact]
    public void FlagDefaultsToTrue()
    {
        ParameterParsers.JsonTimestampsAreUtc.Should().BeTrue();
    }

    [Fact]
    public void FlagOff_TimestampTz_NaiveString_ParsesSuccessfully()
    {
        var parser = ParameterParsers.GetParser(NpgsqlDbType.TimestampTz);
        parser.Should().NotBeNull();

        var previous = ParameterParsers.JsonTimestampsAreUtc;
        try
        {
            ParameterParsers.JsonTimestampsAreUtc = false;
            var ok = parser!("2026-05-20T06:00:00", out var v);
            ok.Should().BeTrue();
            v.Should().BeOfType<DateTime>();
            ((DateTime)v!).Kind.Should().Be(DateTimeKind.Utc);
        }
        finally
        {
            ParameterParsers.JsonTimestampsAreUtc = previous;
        }
    }

    [Fact]
    public void FlagOff_Timestamp_NaiveString_ReturnsUnspecifiedKind()
    {
        var parser = ParameterParsers.GetParser(NpgsqlDbType.Timestamp);

        var previous = ParameterParsers.JsonTimestampsAreUtc;
        try
        {
            ParameterParsers.JsonTimestampsAreUtc = false;
            var ok = parser!("2026-05-20T06:00:00", out var v);
            ok.Should().BeTrue();
            ((DateTime)v!).Kind.Should().Be(DateTimeKind.Unspecified);
        }
        finally
        {
            ParameterParsers.JsonTimestampsAreUtc = previous;
        }
    }

    [Fact]
    public void FlagOff_TimeTz_NaiveString_ReturnsDateTimeOffset()
    {
        var parser = ParameterParsers.GetParser(NpgsqlDbType.TimeTz);

        var previous = ParameterParsers.JsonTimestampsAreUtc;
        try
        {
            ParameterParsers.JsonTimestampsAreUtc = false;
            var ok = parser!("06:00:00", out var v);
            ok.Should().BeTrue();
            v.Should().BeOfType<DateTimeOffset>();
        }
        finally
        {
            ParameterParsers.JsonTimestampsAreUtc = previous;
        }
    }

    [Fact]
    public void FlagOff_Time_NaiveString_ReturnsTimeOnly()
    {
        var parser = ParameterParsers.GetParser(NpgsqlDbType.Time);

        var previous = ParameterParsers.JsonTimestampsAreUtc;
        try
        {
            ParameterParsers.JsonTimestampsAreUtc = false;
            var ok = parser!("06:00:00", out var v);
            ok.Should().BeTrue();
            v.Should().BeOfType<TimeOnly>();
            ((TimeOnly)v!).Hour.Should().Be(6);
        }
        finally
        {
            ParameterParsers.JsonTimestampsAreUtc = previous;
        }
    }

    [Fact]
    public void FlagOff_InvalidInput_ReturnsFalse()
    {
        var parser = ParameterParsers.GetParser(NpgsqlDbType.TimestampTz);

        var previous = ParameterParsers.JsonTimestampsAreUtc;
        try
        {
            ParameterParsers.JsonTimestampsAreUtc = false;
            var ok = parser!("not-a-date", out var v);
            ok.Should().BeFalse();
            v.Should().BeNull();
        }
        finally
        {
            ParameterParsers.JsonTimestampsAreUtc = previous;
        }
    }
}
