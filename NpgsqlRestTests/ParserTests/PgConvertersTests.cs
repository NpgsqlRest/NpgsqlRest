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

    [Fact]
    public void PgArrayToJsonArray_NestedTupleWithQuotes_UnescapesDoubledQuotes()
    {
        // Arrange - this is the actual PostgreSQL output format for:
        // array[row('test', row(1, 'hello "world"')::my_inner)::my_outer]::text
        // The "" sequences inside quoted strings should become single "
        var descriptor = new TypeDescriptor("text[]");

        // PostgreSQL array with quoted element containing nested tuple
        // The value is: (test,"(1,""hello """"world"""")")
        // Where the nested tuple has a text field containing: hello "world"
        ReadOnlySpan<char> input = "{\"(test,\\\"(1,\\\"\\\"hello \\\"\\\"\\\"\\\"world\\\"\\\"\\\"\\\")\\\")\"}";

        // Act
        var result = PgConverters.PgArrayToJsonArray(input, descriptor);

        // Assert
        // The doubled quotes should be unescaped: "" -> "
        // So the decoded string should be: (test,"(1,"hello ""world"")")
        // Note: the inner tuple still has "" because we're just decoding the outer array escaping
        var json = result.ToString();

        // Parse as JSON to verify it's valid
        var action = () => System.Text.Json.JsonDocument.Parse(json);
        action.Should().NotThrow();

        // The decoded value should have the backslash-quotes unescaped
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var decoded = doc.RootElement[0].GetString();

        // After decoding JSON, we should get: (test,"(1,"hello ""world"")")
        // The inner "" will become " after we also unescape the tuple format
        decoded.Should().Contain("hello");
        decoded.Should().NotContain("\\\""); // Should not have escaped quotes after JSON decode
    }

    [Fact]
    public void PgCompositeArrayToJsonArray_NestedTupleWithQuotes_ProperlyUnescapes()
    {
        // Arrange - actual PostgreSQL output for:
        // array[row('test', row(1, 'hello "world"')::my_inner)::my_outer]::text
        // which is: {"(test,\"(1,\"\"hello \"\"\"\"world\"\"\"\"\"\")\")"}
        var fieldNames = new[] { "label", "nested" };
        var fieldDescriptors = new[] { new TypeDescriptor("text"), new TypeDescriptor("my_inner") };

        // The actual input from PostgreSQL (C# string literal with escaping)
        ReadOnlySpan<char> input = "{\"(test,\\\"(1,\\\"\\\"hello \\\"\\\"\\\"\\\"world\\\"\\\"\\\"\\\"\\\"\\\")\\\")\"}";;

        // Act
        var result = PgConverters.PgCompositeArrayToJsonArray(input, fieldNames, fieldDescriptors);
        var json = result.ToString();

        // Assert - should be valid JSON
        var action = () => System.Text.Json.JsonDocument.Parse(json);
        action.Should().NotThrow($"Output should be valid JSON. Got: {json}");

        // Parse and check the nested field value
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var nested = doc.RootElement[0].GetProperty("nested").GetString();

        // The nested field should be: (1,"hello ""world""") with proper escaping
        // which when JSON-decoded should give us the tuple string
        // And that tuple string should have only "" for literal quotes (not """")
        nested.Should().NotBeNull();
        // The original value has hello "world" - after tuple encoding it should be:
        // (1,"hello ""world""") - with doubled quotes for the literal "
        nested.Should().Be("(1,\"hello \"\"world\"\"\")");
    }

    [Fact]
    public void PgArrayToJsonArray_WithBackslashQuoteEscaping_UnescapesCorrectly()
    {
        // Test that PgArrayToJsonArray correctly handles \"  inside quoted elements
        // This is what happens when an array field value is extracted from a composite
        // Input: {\"(1,\\\"hello\\\")\"}  - an array element with backslash-escaped quotes
        // After processing: the tuple string should have quotes, not backslash-quotes
        var descriptor = new TypeDescriptor("text[]");

        // Array with quoted element containing backslash-escaped quotes
        // The C# string literal: {\"(1,\\\"hello\\\")\"}
        // Which represents: {"(1,\"hello\")"}
        ReadOnlySpan<char> input = "{\"(1,\\\"hello\\\")\"}";

        // Act
        var result = PgConverters.PgArrayToJsonArray(input, descriptor);
        var json = result.ToString();

        // Assert
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var firstElement = doc.RootElement[0].GetString();

        // The backslash-quote should be unescaped to just quote
        // So (1,\"hello\") should become (1,"hello")
        firstElement.Should().Be("(1,\"hello\")");
        firstElement.Should().NotContain("\\\"");
    }

    [Fact]
    public void PgArrayToJsonArray_WithDoubledQuotesAndBackslash_UnescapesCorrectly()
    {
        // Test the exact format that would come from a composite field extraction
        // After extracting from composite, the array field value might look like:
        // {"(1,\"hello \"\"quoted\"\"\")"}
        // Which represents an array with element (1,"hello ""quoted""")
        // The inner "" should become " when decoded
        var descriptor = new TypeDescriptor("text[]");

        // C# string: {"(1,\"hello \"\"quoted\"\"\")"}
        // Actual chars: {"(1,\"hello \"\"quoted\"\"\")"}
        ReadOnlySpan<char> input = "{\"(1,\\\"hello \\\"\\\"quoted\\\"\\\"\\\")\"}";

        // Act
        var result = PgConverters.PgArrayToJsonArray(input, descriptor);
        var json = result.ToString();

        // Assert
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var firstElement = doc.RootElement[0].GetString();

        // The element should be: (1,"hello ""quoted""")
        // With proper unescaping of both \" and ""
        firstElement.Should().Be("(1,\"hello \"\"quoted\"\"\")");
    }

    [Fact]
    public void TypeDescriptor_CustomArrayType_IsRecognizedAsArray()
    {
        // Verify that custom composite array types are recognized as arrays
        var descriptor = new TypeDescriptor("ts_inner_type[]");
        descriptor.IsArray.Should().BeTrue();
        descriptor.Type.Should().Be("ts_inner_type");
    }

    [Fact]
    public void PgCompositeArrayToJsonArray_WithArrayFieldSimple_ExtractsCorrectly()
    {
        // Simple case: array[row('group1', array[row(1,'hi')])]
        // PostgreSQL output: {"(group1,\"{\"\"(1,hi)\"\"}\")"}

        var fieldNames = new[] { "name", "items" };
        var fieldDescriptors = new[] { new TypeDescriptor("text"), new TypeDescriptor("my_item[]") };

        // PostgreSQL: {"(group1,\"{\"\"(1,hi)\"\"}\")"}
        // C# string (doubling backslashes and escaping quotes):
        ReadOnlySpan<char> input = "{\"(group1,\\\"{\\\"\\\"(1,hi)\\\"\\\"}\\\")\"}";;

        // Act
        var result = PgConverters.PgCompositeArrayToJsonArray(input, fieldNames, fieldDescriptors);
        var json = result.ToString();

        // Assert - should be valid JSON
        var action = () => System.Text.Json.JsonDocument.Parse(json);
        action.Should().NotThrow($"Output should be valid JSON. Got: {json}");

        // Parse and check the items field
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var items = doc.RootElement[0].GetProperty("items");
        items.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Array);

        var firstItem = items[0].GetString();
        firstItem.Should().NotBeNull();

        // The tuple string should be: (1,hi)
        firstItem.Should().Be("(1,hi)");
    }

    [Fact]
    public void PgCompositeArrayToJsonArray_WithArrayFieldContainingQuotes_ExtractsCorrectly()
    {
        // Case with quotes: array[row('group1', array[row(1,'hello "q"')])]
        // PostgreSQL output: {"(group1,\"{\"\"(1,\\\\\"\"hello \\\\\"\"\\\\\"\"q\\\\\"\"\\\\\"\"\\\\\"\")\"\"}\")"}

        var fieldNames = new[] { "name", "items" };
        var fieldDescriptors = new[] { new TypeDescriptor("text"), new TypeDescriptor("my_item[]") };

        // PostgreSQL: {"(group1,\"{\"\"(1,\\\\\"\"hello \\\\\"\"\\\\\"\"q\\\\\"\"\\\\\"\"\\\\\"\")\"\"}\")"}
        // In C#, this needs escaping - every \ becomes \\, every " becomes \"
        ReadOnlySpan<char> input = "{\"(group1,\\\"{\\\"\\\"(1,\\\\\\\\\\\"\\\"hello \\\\\\\\\\\"\\\"\\\\\\\\\\\"\\\"q\\\\\\\\\\\"\\\"\\\\\\\\\\\"\\\"\\\\\\\\\\\"\\\")\\\"\\\"}\\\")\"}";

        // Act
        var result = PgConverters.PgCompositeArrayToJsonArray(input, fieldNames, fieldDescriptors);
        var json = result.ToString();

        // Assert - should be valid JSON
        var action = () => System.Text.Json.JsonDocument.Parse(json);
        action.Should().NotThrow($"Output should be valid JSON. Got: {json}");

        // Parse and check the items field
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var items = doc.RootElement[0].GetProperty("items");
        items.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Array);

        var firstItem = items[0].GetString();
        firstItem.Should().NotBeNull();

        // The tuple string should be: (1,"hello ""q""")
        // With proper unescaping of all levels
        firstItem.Should().NotContain("\\\"", $"Should not contain backslash-quote. Got: {firstItem}. Full JSON: {json}");
        firstItem.Should().Be("(1,\"hello \"\"q\"\"\")");
    }

    [Fact]
    public void PgArrayToJsonArray_WithNestedArrayEscaping_HandlesCorrectly()
    {
        // Test the exact format that comes out of composite field extraction
        // After extracting from composite, the array field should be:
        // {"(1,\"hello \"\"q\"\"\")"}
        // Which represents: array element "(1,"hello ""q""")"
        // After array processing, element should decode to: (1,"hello ""q""")
        var descriptor = new TypeDescriptor("text[]");

        // The extracted field value from composite: {"(1,\"hello \"\"q\"\"\")"}
        // In C#: {\"(1,\\\"hello \\\"\\\"q\\\"\\\"\\\")\"}
        ReadOnlySpan<char> input = "{\"(1,\\\"hello \\\"\\\"q\\\"\\\"\\\")\"}";

        // Act
        var result = PgConverters.PgArrayToJsonArray(input, descriptor);
        var json = result.ToString();

        // Assert
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var firstElement = doc.RootElement[0].GetString();

        // The element should be: (1,"hello ""q""")
        firstElement.Should().Be("(1,\"hello \"\"q\"\"\")");
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
