namespace NpgsqlRestTests.ParserTests;

public class TimeSpanParserTests
{
    [Theory]
    [InlineData("1000ms", 0, 0, 1)]         // 1000 milliseconds = 1 second
    [InlineData("10s", 0, 0, 10)]           // 10 seconds
    [InlineData("5m", 0, 5, 0)]             // 5 minutes
    [InlineData("5min", 0, 5, 0)]           // 5 minutes with full unit
    [InlineData("2h", 2, 0, 0)]             // 2 hours
    [InlineData("1d", 24, 0, 0)]            // 1 day
    [InlineData("1 d", 24, 0, 0)]            // 1 day
    [InlineData("1 D", 24, 0, 0)]            // 1 day
    [InlineData("1w", 168, 0, 0)]           // 1 week = 7 days = 168 hours
    [InlineData("2 hours", 2, 0, 0)]        // Space and full unit
    [InlineData("10 SECONDS", 0, 0, 10)]    // Upper case
    public void ParsePostgresInterval_ValidSimpleInputs_ReturnsCorrectTimeSpan(string input, int expectedHours, int expectedMinutes, int expectedSeconds)
    {
        // Act
        TimeSpan? result = Parser.ParsePostgresInterval(input);

        // Assert
        TimeSpan expected = TimeSpan.FromHours(expectedHours) +
                          TimeSpan.FromMinutes(expectedMinutes) +
                          TimeSpan.FromSeconds(expectedSeconds);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("1.5h", 1, 30, 0)]         // 1.5 hours = 1 hour 30 minutes
    [InlineData("0.25d", 6, 0, 0)]         // 0.25 days = 6 hours
    [InlineData("2.5 m", 0, 2, 30)]        // 2.5 minutes = 2 minutes 30 seconds
    [InlineData("1.25s", 0, 0, 1.25)]      // 1.25 seconds
    [InlineData("1.5w", 252, 0, 0)]        // 1.5 weeks = 10.5 days = 252 hours
    [InlineData("500.5ms", 0, 0, 0.5005)]  // 500.5 milliseconds
    public void ParsePostgresInterval_ValidDecimalInputs_ReturnsCorrectTimeSpan(string input, int expectedHours, int expectedMinutes, double expectedSeconds)
    {
        // Act
        TimeSpan? result = Parser.ParsePostgresInterval(input);

        // Assert
        TimeSpan expected = TimeSpan.FromHours(expectedHours) +
                          TimeSpan.FromMinutes(expectedMinutes) +
                          TimeSpan.FromSeconds(expectedSeconds);
        result.Should().BeCloseTo(expected, TimeSpan.FromMilliseconds(1)); // 1ms tolerance
    }

    [Theory]
    [InlineData("0s", 0, 0, 0)]            // Zero seconds
    [InlineData("0.0h", 0, 0, 0)]          // Zero hours with decimal
    [InlineData("0001m", 0, 1, 0)]         // Leading zeros
    public void ParsePostgresInterval_EdgeCaseNumbers_ReturnsCorrectTimeSpan(string input, int expectedHours, int expectedMinutes, int expectedSeconds)
    {
        // Act
        TimeSpan? result = Parser.ParsePostgresInterval(input);

        // Assert
        TimeSpan expected = TimeSpan.FromHours(expectedHours) +
                          TimeSpan.FromMinutes(expectedMinutes) +
                          TimeSpan.FromSeconds(expectedSeconds);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParsePostgresInterval_NullOrWhitespace_ThrowsArgumentNullException(string? input)
    {
        // Act
        TimeSpan? result = Parser.ParsePostgresInterval(input!);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("abc")]                    // No number
    [InlineData("h5")]                     // Unit before number
    [InlineData("5.5.5h")]                 // Invalid number format
    [InlineData("5 m m")]                  // Multiple units
    public void ParsePostgresInterval_InvalidFormat_ThrowsFormatException(string input)
    {
        // Act
        TimeSpan? result = Parser.ParsePostgresInterval(input);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("5x")]                     // Unknown unit
    [InlineData("2months")]                // Unsupported unit
    [InlineData("1year")]                  // Unsupported calendar unit
    public void ParsePostgresInterval_UnknownUnit_ThrowsFormatException(string input)
    {
        // Act
        TimeSpan? result = Parser.ParsePostgresInterval(input);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ParsePostgresInterval_CaseInsensitivity_WorksWithMixedCase()
    {
        // Arrange
        string[] inputs = ["10S", "5Min", "2HoUrS", "1DAY"];
        TimeSpan[] expected =
        [
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromHours(2),
            TimeSpan.FromDays(1)
        ];

        // Act & Assert
        for (int i = 0; i < inputs.Length; i++)
        {
            TimeSpan? result = Parser.ParsePostgresInterval(inputs[i]);
            result.Should().Be(expected[i], because: $"input '{inputs[i]}' should parse correctly");
        }
    }

    [Theory]
    [InlineData("1000us", "usec")]
    [InlineData("1000us", "microsecond")]
    [InlineData("1000us", "microseconds")]
    [InlineData("1000us", "US")]
    [InlineData("1000us", "USEC")]
    [InlineData("1000us", "MicroSecond")]
    [InlineData("1000us", "MICROSECONDS")]
    public void ParsePostgresInterval_MicrosecondVariations_AllWork(string baseInput, string unitVariation)
    {
        // Arrange
        string input = baseInput.Replace("us", unitVariation);
        TimeSpan expected = TimeSpan.FromMicroseconds(1000);

        // Act
        TimeSpan? result = Parser.ParsePostgresInterval(input);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("250ms", "msec")]
    [InlineData("250ms", "millisecond")]
    [InlineData("250ms", "milliseconds")]
    [InlineData("250ms", "MS")]
    [InlineData("250ms", "MSEC")]
    [InlineData("250ms", "MilliSecond")]
    [InlineData("250ms", "MILLISECONDS")]
    public void ParsePostgresInterval_MillisecondVariations_AllWork(string baseInput, string unitVariation)
    {
        // Arrange
        string input = baseInput.Replace("ms", unitVariation);
        TimeSpan expected = TimeSpan.FromMilliseconds(250);

        // Act
        TimeSpan? result = Parser.ParsePostgresInterval(input);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("2w", "week")]
    [InlineData("2w", "weeks")]
    [InlineData("2w", "W")]
    [InlineData("2w", "WEEK")]
    [InlineData("2w", "WeEk")]
    [InlineData("2w", "WEEKS")]
    public void ParsePostgresInterval_WeekVariations_AllWork(string baseInput, string unitVariation)
    {
        // Arrange
        string input = baseInput.Replace("w", unitVariation);
        TimeSpan expected = TimeSpan.FromDays(14); // 2 weeks = 14 days

        // Act
        TimeSpan? result = Parser.ParsePostgresInterval(input);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("1500us", 0, 0, 0, 1500)]        // 1500 microseconds
    [InlineData("2500microseconds", 0, 0, 0, 2500)] // 2500 microseconds
    [InlineData("250ms", 0, 0, 0.25, 0)]          // 250 milliseconds
    [InlineData("1500milliseconds", 0, 0, 1.5, 0)] // 1500 milliseconds
    [InlineData("2weeks", 336, 0, 0, 0)]          // 2 weeks = 14 days = 336 hours
    [InlineData("3 w", 504, 0, 0, 0)]             // 3 weeks = 21 days = 504 hours
    public void ParsePostgresInterval_NewUnits_ReturnsCorrectTimeSpan(string input, int expectedHours, int expectedMinutes, double expectedSeconds, double expectedMicroseconds)
    {
        // Act
        TimeSpan? result = Parser.ParsePostgresInterval(input);

        // Assert
        TimeSpan expected = TimeSpan.FromHours(expectedHours) +
                          TimeSpan.FromMinutes(expectedMinutes) +
                          TimeSpan.FromSeconds(expectedSeconds) +
                          TimeSpan.FromMicroseconds(expectedMicroseconds);
        result.Should().BeCloseTo(expected, TimeSpan.FromMicroseconds(1)); // 1 microsecond tolerance
    }

    [Theory]
    [InlineData("0ms", 0)]                          // Zero milliseconds
    [InlineData("0us", 0)]                          // Zero microseconds
    [InlineData("0w", 0)]                           // Zero weeks
    [InlineData("1000000us", 1000)]                 // 1 million microseconds = 1 second
    [InlineData("60000ms", 60000)]                  // 60000 milliseconds = 1 minute
    [InlineData("4w", 28L * 24 * 60 * 60 * 1000)]  // 4 weeks in milliseconds
    public void ParsePostgresInterval_NewUnitsEdgeCases_ReturnsCorrectTimeSpan(string input, long expectedTotalMilliseconds)
    {
        // Act
        TimeSpan? result = Parser.ParsePostgresInterval(input);

        // Assert
        result.Should().NotBeNull();
        result!.Value.TotalMilliseconds.Should().BeApproximately(expectedTotalMilliseconds, 1); // 1ms tolerance
    }

    [Theory]
    [InlineData("5", 5)]                      // Integer without unit defaults to seconds
    [InlineData("10", 10)]                    // Another integer
    [InlineData("0", 0)]                      // Zero without unit
    [InlineData("1.5", 1.5)]                  // Decimal without unit
    [InlineData("30.25", 30.25)]              // Decimal with fractional part
    [InlineData("0.5", 0.5)]                  // Half a second
    [InlineData("120", 120)]                  // 2 minutes in seconds
    [InlineData("3600", 3600)]                // 1 hour in seconds
    public void ParsePostgresInterval_NumberWithoutUnit_DefaultsToSeconds(string input, double expectedSeconds)
    {
        // Act
        TimeSpan? result = Parser.ParsePostgresInterval(input);

        // Assert
        result.Should().NotBeNull();
        result.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }
}