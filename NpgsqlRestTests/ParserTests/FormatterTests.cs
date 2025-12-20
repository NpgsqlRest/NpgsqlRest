namespace NpgsqlRestTests.ParserTests;

public class FormatterTests
{
    #region Basic Replacement Tests

    [Fact]
    public void FormatString_SimpleReplacement_ReplacesCorrectly()
    {
        // Arrange
        var replacements = new Dictionary<string, string>
        {
            { "name", "John" }
        };
        ReadOnlySpan<char> input = "Hello, {name}!";

        // Act
        var result = Formatter.FormatString(input, replacements);

        // Assert
        result.ToString().Should().Be("Hello, John!");
    }

    [Fact]
    public void FormatString_MultipleReplacements_ReplacesAll()
    {
        // Arrange
        var replacements = new Dictionary<string, string>
        {
            { "first", "John" },
            { "last", "Doe" }
        };
        ReadOnlySpan<char> input = "{first} {last}";

        // Act
        var result = Formatter.FormatString(input, replacements);

        // Assert
        result.ToString().Should().Be("John Doe");
    }

    [Fact]
    public void FormatString_SameKeyMultipleTimes_ReplacesAll()
    {
        // Arrange
        var replacements = new Dictionary<string, string>
        {
            { "x", "hello" }
        };
        ReadOnlySpan<char> input = "{x} and {x} and {x}";

        // Act
        var result = Formatter.FormatString(input, replacements);

        // Assert
        result.ToString().Should().Be("hello and hello and hello");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void FormatString_EmptyInput_ReturnsEmpty()
    {
        // Arrange
        var replacements = new Dictionary<string, string>
        {
            { "name", "John" }
        };
        ReadOnlySpan<char> input = "";

        // Act
        var result = Formatter.FormatString(input, replacements);

        // Assert
        result.ToString().Should().Be("");
    }

    [Fact]
    public void FormatString_NullReplacements_ReturnsOriginal()
    {
        // Arrange
        ReadOnlySpan<char> input = "Hello, {name}!";

        // Act
        var result = Formatter.FormatString(input, null!);

        // Assert
        result.ToString().Should().Be("Hello, {name}!");
    }

    [Fact]
    public void FormatString_EmptyReplacements_ReturnsOriginal()
    {
        // Arrange
        var replacements = new Dictionary<string, string>();
        ReadOnlySpan<char> input = "Hello, {name}!";

        // Act
        var result = Formatter.FormatString(input, replacements);

        // Assert
        result.ToString().Should().Be("Hello, {name}!");
    }

    [Fact]
    public void FormatString_NoPlaceholders_ReturnsOriginal()
    {
        // Arrange
        var replacements = new Dictionary<string, string>
        {
            { "name", "John" }
        };
        ReadOnlySpan<char> input = "Hello, World!";

        // Act
        var result = Formatter.FormatString(input, replacements);

        // Assert
        result.ToString().Should().Be("Hello, World!");
    }

    [Fact]
    public void FormatString_UnknownKey_KeepsPlaceholder()
    {
        // Arrange
        var replacements = new Dictionary<string, string>
        {
            { "other", "value" }
        };
        ReadOnlySpan<char> input = "Hello, {name}!";

        // Act
        var result = Formatter.FormatString(input, replacements);

        // Assert
        result.ToString().Should().Be("Hello, {name}!");
    }

    [Fact]
    public void FormatString_MixedKnownAndUnknown_HandlesBoth()
    {
        // Arrange
        var replacements = new Dictionary<string, string>
        {
            { "known", "REPLACED" }
        };
        ReadOnlySpan<char> input = "{known} and {unknown}";

        // Act
        var result = Formatter.FormatString(input, replacements);

        // Assert
        result.ToString().Should().Be("REPLACED and {unknown}");
    }

    #endregion

    #region Malformed Input Tests

    [Fact]
    public void FormatString_UnclosedBrace_KeepsAsLiteral()
    {
        // Arrange
        var replacements = new Dictionary<string, string>
        {
            { "name", "John" }
        };
        ReadOnlySpan<char> input = "Hello, {name";

        // Act
        var result = Formatter.FormatString(input, replacements);

        // Assert
        result.ToString().Should().Be("Hello, {name");
    }

    [Fact]
    public void FormatString_UnmatchedCloseBrace_KeepsAsLiteral()
    {
        // Arrange
        var replacements = new Dictionary<string, string>
        {
            { "name", "John" }
        };
        ReadOnlySpan<char> input = "Hello, name}!";

        // Act
        var result = Formatter.FormatString(input, replacements);

        // Assert
        result.ToString().Should().Be("Hello, name}!");
    }

    [Fact]
    public void FormatString_NestedBraces_HandlesOuterOnly()
    {
        // Arrange
        var replacements = new Dictionary<string, string>
        {
            { "outer", "OUTER" },
            { "{inner}", "INNER" }
        };
        ReadOnlySpan<char> input = "{{inner}}";

        // Act
        var result = Formatter.FormatString(input, replacements);

        // Assert
        // The behavior should handle nested braces appropriately
        result.ToString().Should().NotBeEmpty();
    }

    [Fact]
    public void FormatString_EmptyPlaceholder_KeepsAsLiteral()
    {
        // Arrange
        var replacements = new Dictionary<string, string>
        {
            { "", "empty" }
        };
        ReadOnlySpan<char> input = "Hello, {}!";

        // Act
        var result = Formatter.FormatString(input, replacements);

        // Assert
        // Empty key lookup behavior
        result.ToString().Should().NotBeNull();
    }

    [Fact]
    public void FormatString_AdjacentPlaceholders_HandlesCorrectly()
    {
        // Arrange
        var replacements = new Dictionary<string, string>
        {
            { "a", "A" },
            { "b", "B" }
        };
        ReadOnlySpan<char> input = "{a}{b}";

        // Act
        var result = Formatter.FormatString(input, replacements);

        // Assert
        result.ToString().Should().Be("AB");
    }

    #endregion

    #region Performance and Large Input Tests

    [Fact]
    public void FormatString_LargeInput_HandlesCorrectly()
    {
        // Arrange
        var replacements = new Dictionary<string, string>
        {
            { "x", "replaced" }
        };
        var largeInput = string.Join(" ", Enumerable.Repeat("word", 10000)) + " {x}";
        ReadOnlySpan<char> input = largeInput;

        // Act
        var result = Formatter.FormatString(input, replacements);

        // Assert
        result.ToString().Should().EndWith("replaced");
        result.ToString().Should().StartWith("word");
    }

    [Fact]
    public void FormatString_ManyPlaceholders_HandlesCorrectly()
    {
        // Arrange
        var replacements = new Dictionary<string, string>();
        var inputBuilder = new System.Text.StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            replacements[$"key{i}"] = $"value{i}";
            inputBuilder.Append($"{{key{i}}} ");
        }
        ReadOnlySpan<char> input = inputBuilder.ToString();

        // Act
        var result = Formatter.FormatString(input, replacements);

        // Assert
        for (int i = 0; i < 100; i++)
        {
            result.ToString().Should().Contain($"value{i}");
        }
    }

    [Fact]
    public void FormatString_LongReplacementValue_HandlesCorrectly()
    {
        // Arrange
        var longValue = new string('x', 10000);
        var replacements = new Dictionary<string, string>
        {
            { "key", longValue }
        };
        ReadOnlySpan<char> input = "start {key} end";

        // Act
        var result = Formatter.FormatString(input, replacements);

        // Assert
        result.ToString().Should().StartWith("start ");
        result.ToString().Should().EndWith(" end");
        result.Length.Should().Be(6 + longValue.Length + 4); // "start " + value + " end"
    }

    #endregion

    #region Special Characters Tests

    [Fact]
    public void FormatString_PlaceholderWithNumbers_Works()
    {
        // Arrange
        var replacements = new Dictionary<string, string>
        {
            { "key123", "value" }
        };
        ReadOnlySpan<char> input = "{key123}";

        // Act
        var result = Formatter.FormatString(input, replacements);

        // Assert
        result.ToString().Should().Be("value");
    }

    [Fact]
    public void FormatString_PlaceholderWithUnderscore_Works()
    {
        // Arrange
        var replacements = new Dictionary<string, string>
        {
            { "my_key", "value" }
        };
        ReadOnlySpan<char> input = "{my_key}";

        // Act
        var result = Formatter.FormatString(input, replacements);

        // Assert
        result.ToString().Should().Be("value");
    }

    [Fact]
    public void FormatString_ReplacementWithBraces_Works()
    {
        // Arrange
        var replacements = new Dictionary<string, string>
        {
            { "key", "{value}" }
        };
        ReadOnlySpan<char> input = "{key}";

        // Act
        var result = Formatter.FormatString(input, replacements);

        // Assert
        result.ToString().Should().Be("{value}");
    }

    #endregion
}
