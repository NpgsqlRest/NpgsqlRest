namespace NpgsqlRestTests;

/// <summary>
/// Regression: the command timeout used TimeSpan.Seconds (the seconds component) instead of the total, so a
/// "1 minute" timeout became 0 - which Npgsql treats as no timeout at all - and "90 seconds" became 30.
/// </summary>
public class CommandTimeoutConversionTests
{
    [Theory]
    [InlineData(60_000, 60)]       // "1 minute" (was 0 = unlimited)
    [InlineData(120_000, 120)]     // "2 minutes" (was 0 = unlimited)
    [InlineData(90_000, 90)]       // "90 seconds" (was 30)
    [InlineData(3_600_000, 3600)]  // "1 hour" (was 0 = unlimited)
    [InlineData(30_000, 30)]
    [InlineData(500, 1)]           // sub-second rounds up instead of becoming 0 = unlimited
    [InlineData(1_500, 2)]
    [InlineData(0, 0)]             // explicit zero keeps meaning "no timeout"
    public void Timeout_is_converted_to_total_whole_seconds(int milliseconds, int expectedSeconds)
    {
        NpgsqlRestEndpoint.ToCommandTimeoutSeconds(TimeSpan.FromMilliseconds(milliseconds)).Should().Be(expectedSeconds);
    }

    [Fact]
    public void Very_large_timeout_is_capped()
    {
        NpgsqlRestEndpoint.ToCommandTimeoutSeconds(TimeSpan.MaxValue).Should().Be(int.MaxValue);
    }
}
