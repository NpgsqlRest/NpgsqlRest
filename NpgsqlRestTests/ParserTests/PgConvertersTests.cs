namespace NpgsqlRestTests.ParserTests;

public class PgConvertersTests
{
    #region PgArrayToJsonArray Tests

    [Fact]
    public void PgArrayToJsonArray_EmptyArray_ReturnsEmptyJsonArray()
    {
        // Arrange
        var descriptor = new TypeDescriptor("text[]");
        ReadOnlySpan<char> input = "{}";

        // Act
        var result = PgConverters.PgArrayToJsonArray(input, descriptor);

        // Assert
        result.ToString().Should().Be("[]");
    }

    [Fact]
    public void PgArrayToJsonArray_IntegerArray_ReturnsUnquotedNumbers()
    {
        // Arrange
        var descriptor = new TypeDescriptor("integer[]");
        ReadOnlySpan<char> input = "{1,2,3}";

        // Act
        var result = PgConverters.PgArrayToJsonArray(input, descriptor);

        // Assert
        result.ToString().Should().Be("[1,2,3]");
    }

    [Fact]
    public void PgArrayToJsonArray_TextArray_ReturnsQuotedStrings()
    {
        // Arrange
        var descriptor = new TypeDescriptor("text[]");
        ReadOnlySpan<char> input = "{a,bc,xyz}";

        // Act
        var result = PgConverters.PgArrayToJsonArray(input, descriptor);

        // Assert
        result.ToString().Should().Be("[\"a\",\"bc\",\"xyz\"]");
    }

    [Fact]
    public void PgArrayToJsonArray_BooleanArray_ReturnsProperBooleans()
    {
        // Arrange
        var descriptor = new TypeDescriptor("boolean[]");
        ReadOnlySpan<char> input = "{t,f,t}";

        // Act
        var result = PgConverters.PgArrayToJsonArray(input, descriptor);

        // Assert
        result.ToString().Should().Be("[true,false,true]");
    }

    [Fact]
    public void PgArrayToJsonArray_WithNulls_ReturnsNullValues()
    {
        // Arrange
        var descriptor = new TypeDescriptor("integer[]");
        ReadOnlySpan<char> input = "{1,NULL,3}";

        // Act
        var result = PgConverters.PgArrayToJsonArray(input, descriptor);

        // Assert
        result.ToString().Should().Be("[1,null,3]");
    }

    [Fact]
    public void PgArrayToJsonArray_JsonArray_ReturnsUnquotedJson()
    {
        // Arrange
        var descriptor = new TypeDescriptor("jsonb[]");
        ReadOnlySpan<char> input = "{\"{\\\"key\\\":\\\"value\\\"}\"}";

        // Act
        var result = PgConverters.PgArrayToJsonArray(input, descriptor);

        // Assert
        // JSON values should not be double-quoted
        result.ToString().Should().Contain("{");
    }

    [Fact]
    public void PgArrayToJsonArray_DateTimeArray_ReplacesSpaceWithT()
    {
        // Arrange
        var descriptor = new TypeDescriptor("timestamp[]");
        ReadOnlySpan<char> input = "{\"2023-01-15 10:30:00\",\"2023-02-20 14:45:00\"}";

        // Act
        var result = PgConverters.PgArrayToJsonArray(input, descriptor);

        // Assert
        result.ToString().Should().Contain("T");
        result.ToString().Should().NotContain(" 10:");
    }

    [Fact]
    public void PgArrayToJsonArray_WithNewlines_EscapesNewlines()
    {
        // Arrange
        var descriptor = new TypeDescriptor("text[]");
        ReadOnlySpan<char> input = "{\"line1\nline2\"}";

        // Act
        var result = PgConverters.PgArrayToJsonArray(input, descriptor);

        // Assert
        result.ToString().Should().Contain("\\n");
    }

    [Fact]
    public void PgArrayToJsonArray_WithTabs_EscapesTabs()
    {
        // Arrange
        var descriptor = new TypeDescriptor("text[]");
        ReadOnlySpan<char> input = "{\"col1\tcol2\"}";

        // Act
        var result = PgConverters.PgArrayToJsonArray(input, descriptor);

        // Assert
        result.ToString().Should().Contain("\\t");
    }

    [Fact]
    public void PgArrayToJsonArray_LargeArray_HandlesCorrectly()
    {
        // Arrange
        var descriptor = new TypeDescriptor("integer[]");
        var elements = string.Join(",", Enumerable.Range(1, 1000));
        ReadOnlySpan<char> input = $"{{{elements}}}";

        // Act
        var result = PgConverters.PgArrayToJsonArray(input, descriptor);

        // Assert
        var resultStr = result.ToString();
        resultStr.Should().StartWith("[1,");
        resultStr.Should().EndWith(",1000]");
        resultStr.Split(',').Should().HaveCount(1000);
    }

    [Fact]
    public void PgArrayToJsonArray_InvalidInput_ReturnsEmptyArray()
    {
        // Arrange - missing braces
        var descriptor = new TypeDescriptor("integer[]");
        ReadOnlySpan<char> input = "1,2,3";

        // Act
        var result = PgConverters.PgArrayToJsonArray(input, descriptor);

        // Assert
        result.ToString().Should().Be("[]");
    }

    [Fact]
    public void PgArrayToJsonArray_QuotedTextWithComma_PreservesComma()
    {
        // Arrange
        var descriptor = new TypeDescriptor("text[]");
        ReadOnlySpan<char> input = "{\"hello,world\",normal}";

        // Act
        var result = PgConverters.PgArrayToJsonArray(input, descriptor);

        // Assert
        result.ToString().Should().Be("[\"hello,world\",\"normal\"]");
    }

    #endregion

    #region PgUnknownToJsonArray Tests

    [Fact]
    public void PgUnknownToJsonArray_SimpleTuple_ReturnsJsonArray()
    {
        // Arrange
        ReadOnlySpan<char> input = "(a,b,c)";

        // Act
        var result = PgConverters.PgUnknownToJsonArray(ref input);

        // Assert
        result.ToString().Should().Be("[\"a\",\"b\",\"c\"]");
    }

    [Fact]
    public void PgUnknownToJsonArray_WithEmptyElements_ReturnsNulls()
    {
        // Arrange
        ReadOnlySpan<char> input = "(a,,c)";

        // Act
        var result = PgConverters.PgUnknownToJsonArray(ref input);

        // Assert
        result.ToString().Should().Be("[\"a\",null,\"c\"]");
    }

    [Fact]
    public void PgUnknownToJsonArray_AllEmpty_ReturnsAllNulls()
    {
        // Arrange
        ReadOnlySpan<char> input = "(,,)";

        // Act
        var result = PgConverters.PgUnknownToJsonArray(ref input);

        // Assert
        result.ToString().Should().Be("[null,null,null]");
    }

    [Fact]
    public void PgUnknownToJsonArray_QuotedValues_HandlesCorrectly()
    {
        // Arrange
        ReadOnlySpan<char> input = "(\"hello\",\"world\")";

        // Act
        var result = PgConverters.PgUnknownToJsonArray(ref input);

        // Assert
        result.ToString().Should().Be("[\"hello\",\"world\"]");
    }

    [Fact]
    public void PgUnknownToJsonArray_EscapedQuotes_HandlesCorrectly()
    {
        // Arrange - doubled quotes are escape sequences in PostgreSQL
        ReadOnlySpan<char> input = "(\"he\"\"llo\",world)";

        // Act
        var result = PgConverters.PgUnknownToJsonArray(ref input);

        // Assert
        // The doubled quote should become a single quote in output
        result.ToString().Should().Contain("\"");
    }

    [Fact]
    public void PgUnknownToJsonArray_InvalidInput_ReturnsOriginal()
    {
        // Arrange - not starting with parenthesis
        ReadOnlySpan<char> input = "a,b,c";

        // Act
        var result = PgConverters.PgUnknownToJsonArray(ref input);

        // Assert
        result.ToString().Should().Be("a,b,c");
    }

    [Fact]
    public void PgUnknownToJsonArray_SingleElement_ReturnsArrayWithOne()
    {
        // Arrange
        ReadOnlySpan<char> input = "(single)";

        // Act
        var result = PgConverters.PgUnknownToJsonArray(ref input);

        // Assert
        result.ToString().Should().Be("[\"single\"]");
    }

    #endregion

    #region QuoteText Tests

    [Fact]
    public void QuoteText_SimpleText_ReturnsQuoted()
    {
        // Arrange
        ReadOnlySpan<char> input = "hello";

        // Act
        var result = PgConverters.QuoteText(input);

        // Assert
        result.Should().Be("\"hello\"");
    }

    [Fact]
    public void QuoteText_EmptyString_ReturnsEmptyQuoted()
    {
        // Arrange
        ReadOnlySpan<char> input = "";

        // Act
        var result = PgConverters.QuoteText(input);

        // Assert
        result.Should().Be("\"\"");
    }

    [Fact]
    public void QuoteText_WithQuotes_EscapesQuotes()
    {
        // Arrange
        ReadOnlySpan<char> input = "hello\"world";

        // Act
        var result = PgConverters.QuoteText(input);

        // Assert
        result.Should().Be("\"hello\"\"world\"");
    }

    [Fact]
    public void QuoteText_MultipleQuotes_EscapesAll()
    {
        // Arrange
        ReadOnlySpan<char> input = "\"a\"b\"c\"";

        // Act
        var result = PgConverters.QuoteText(input);

        // Assert
        result.Should().Be("\"\"\"a\"\"b\"\"c\"\"\"");
    }

    [Fact]
    public void QuoteText_OnlyQuotes_EscapesAll()
    {
        // Arrange
        ReadOnlySpan<char> input = "\"\"\"";

        // Act
        var result = PgConverters.QuoteText(input);

        // Assert
        result.Should().Be("\"\"\"\"\"\"\"\"");
    }

    [Fact]
    public void QuoteText_LargeString_HandlesCorrectly()
    {
        // Arrange
        var largeString = new string('a', 10000);
        ReadOnlySpan<char> input = largeString;

        // Act
        var result = PgConverters.QuoteText(input);

        // Assert
        result.Should().HaveLength(10002); // original + 2 quotes
        result.Should().StartWith("\"");
        result.Should().EndWith("\"");
    }

    [Fact]
    public void QuoteText_LargeStringWithQuotes_HandlesCorrectly()
    {
        // Arrange - string with quotes scattered throughout
        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            builder.Append("text\"");
        }
        ReadOnlySpan<char> input = builder.ToString();

        // Act
        var result = PgConverters.QuoteText(input);

        // Assert
        // Should have 100 escaped quotes (each " becomes "")
        result.Should().HaveLength(builder.Length + 2 + 100);
    }

    #endregion

    #region Quote Tests

    [Fact]
    public void Quote_SimpleText_ReturnsQuoted()
    {
        // Arrange
        ReadOnlySpan<char> input = "hello";

        // Act
        var result = PgConverters.Quote(ref input);

        // Assert
        result.Should().Be("\"hello\"");
    }

    [Fact]
    public void Quote_EmptyString_ReturnsEmptyQuoted()
    {
        // Arrange
        ReadOnlySpan<char> input = "";

        // Act
        var result = PgConverters.Quote(ref input);

        // Assert
        result.Should().Be("\"\"");
    }

    #endregion

    #region QuoteDateTime Tests

    [Fact]
    public void QuoteDateTime_WithSpace_ReplacesWithT()
    {
        // Arrange
        ReadOnlySpan<char> input = "2023-01-15 10:30:00";

        // Act
        var result = PgConverters.QuoteDateTime(ref input);

        // Assert
        result.Should().Be("\"2023-01-15T10:30:00\"");
    }

    [Fact]
    public void QuoteDateTime_MultipleSpaces_ReplacesAll()
    {
        // Arrange
        ReadOnlySpan<char> input = "a b c";

        // Act
        var result = PgConverters.QuoteDateTime(ref input);

        // Assert
        result.Should().Be("\"aTbTc\"");
    }

    #endregion
}
