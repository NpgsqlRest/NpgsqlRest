using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace NpgsqlRest;

[JsonSerializable(typeof(string))]
internal partial class NpgsqlRestSerializerContext : JsonSerializerContext;

internal static partial class ParameterPattern
{
    [GeneratedRegex(@"\$\d+")]
    public static partial Regex PostgreSqlParameterPattern();
}

public static class PgConverters
{
    // SIMD-accelerated search values for delimiter detection
    private static readonly SearchValues<char> ArrayDelimiters = SearchValues.Create(",{}\"\\");
    private static readonly SearchValues<char> TupleDelimiters = SearchValues.Create(",()\"\\");
    private static readonly SearchValues<char> QuoteSearchValue = SearchValues.Create("\"");

    private static readonly JsonSerializerOptions PlainTextSerializerOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new NpgsqlRestSerializerContext()
    };

    [UnconditionalSuppressMessage("Aot", "IL2026:RequiresUnreferencedCode",
        Justification = "Serializes only string type that have AOT friendly TypeInfoResolver")]
    [UnconditionalSuppressMessage("Aot", "IL3050:RequiresDynamic",
        Justification = "Serializes only string type that have AOT friendly TypeInfoResolver")]
    public static string SerializeString(string value) => JsonSerializer.Serialize(value, PlainTextSerializerOptions);

    [UnconditionalSuppressMessage("Aot", "IL2026:RequiresUnreferencedCode",
    Justification = "Serializes only string type that have AOT friendly TypeInfoResolver")]
    [UnconditionalSuppressMessage("Aot", "IL3050:RequiresDynamic",
    Justification = "Serializes only string type that have AOT friendly TypeInfoResolver")]
    public static string SerializeObject(object? value) => JsonSerializer.Serialize(value, PlainTextSerializerOptions);

    [UnconditionalSuppressMessage("Aot", "IL2026:RequiresUnreferencedCode",
        Justification = "Serializes only string type that have AOT friendly TypeInfoResolver")]
    [UnconditionalSuppressMessage("Aot", "IL3050:RequiresDynamic",
        Justification = "Serializes only string type that have AOT friendly TypeInfoResolver")]
    public static string SerializeString(ref ReadOnlySpan<char> value) => JsonSerializer.Serialize(value.ToString(), PlainTextSerializerOptions);

    internal static ReadOnlySpan<char> PgUnknownToJsonArray(ref ReadOnlySpan<char> value)
    {
        if (value[0] != Consts.OpenParenthesis || value[^1] != Consts.CloseParenthesis)
        {
            // should never happen
            return value;
        }

        var len = value.Length;
        var result = new StringBuilder(len * 2);
        result.Append(Consts.OpenBracket);
        var current = new StringBuilder();
        bool insideQuotes = false;
        bool first = true;

        int i = 1;
        while (i < len)
        {
            // Use SIMD-accelerated search to find the next delimiter
            var remaining = value.Slice(i);
            int nextDelimiterOffset = remaining.IndexOfAny(TupleDelimiters);

            if (nextDelimiterOffset == -1)
            {
                // No more delimiters, append the rest
                if (!insideQuotes)
                {
                    current.Append(remaining);
                }
                break;
            }

            // Append characters before the delimiter
            if (nextDelimiterOffset > 0)
            {
                current.Append(remaining.Slice(0, nextDelimiterOffset));
                i += nextDelimiterOffset;
            }

            char currentChar = value[i];

            if ((currentChar == Consts.Comma || (currentChar == Consts.CloseParenthesis && i == len - 1)) && !insideQuotes)
            {
                if (!first)
                {
                    result.Append(Consts.Comma);
                }
                else
                {
                    first = false;
                }
                if (current.Length == 0)
                {
                    result.Append(Consts.Null);
                }
                else
                {
                    var segment = current.ToString();
                    result.Append(SerializeString(segment));
                    current.Clear();
                }
                i++;
            }
            else
            {
                if (currentChar == Consts.DoubleQuote && i < len - 2 && value[i + 1] == Consts.DoubleQuote)
                {
                    current.Append(currentChar);
                    i += 2;
                    continue;
                }
                if (currentChar == Consts.DoubleQuote)
                {
                    insideQuotes = !insideQuotes;
                    i++;
                }
                else
                {
                    if (currentChar == Consts.Backslash && i < len - 2 && value[i + 1] == Consts.Backslash)
                    {
                        i += 2;
                        current.Append(currentChar);
                    }
                    else
                    {
                        current.Append(currentChar);
                        i++;
                    }
                }
            }
        }

        result.Append(Consts.CloseBracket);
        return result.ToString();
    }

    internal static ReadOnlySpan<char> PgArrayToJsonArray(ReadOnlySpan<char> value, TypeDescriptor descriptor)
    {
        var len = value.Length;
        if (value.IsEmpty || len < 3 || value[0] != Consts.OpenBrace || value[^1] != Consts.CloseBrace)
        {
            if (descriptor.IsArray is true)
            {
                return Consts.EmptyArray.AsSpan();
            }
            return value;
        }

        var result = new StringBuilder(len * 2);
        result.Append(Consts.OpenBracket);
        var current = new StringBuilder();
        var quoted = !(descriptor.IsNumeric || descriptor.IsBoolean || descriptor.IsJson);
        bool insideQuotes = false;
        bool hasQuotes = false;
        int braceDepth = 1; // We've already consumed the opening brace

        bool IsNull()
        {
            if (current.Length == 4)
            {
                return
                    current[0] == 'N' &&
                    current[1] == 'U' &&
                    current[2] == 'L' &&
                    current[3] == 'L';
            }
            return false;
        }

        int i = 1;
        while (i < len)
        {
            // Use SIMD-accelerated search to find the next delimiter
            var remaining = value.Slice(i);
            int nextDelimiterOffset = remaining.IndexOfAny(ArrayDelimiters);

            if (nextDelimiterOffset == -1)
            {
                // No more delimiters, this shouldn't happen with well-formed input
                break;
            }

            // Process characters before the delimiter based on type
            if (nextDelimiterOffset > 0)
            {
                var segment = remaining.Slice(0, nextDelimiterOffset);
                if (descriptor.IsBoolean)
                {
                    // For booleans, we only need the first char to determine true/false
                    foreach (var ch in segment)
                    {
                        if (ch == 't')
                            current.Append(Consts.True);
                        else if (ch == 'f')
                            current.Append(Consts.False);
                        else
                            current.Append(ch);
                    }
                }
                else if (descriptor.IsDateTime)
                {
                    foreach (var ch in segment)
                    {
                        current.Append(ch == Consts.Space ? 'T' : ch);
                    }
                }
                else
                {
                    // Check for escape sequences in the segment
                    int escapeIdx = segment.IndexOfAny('\n', '\t', '\r');
                    if (escapeIdx == -1)
                    {
                        current.Append(segment);
                    }
                    else
                    {
                        foreach (var ch in segment)
                        {
                            if (ch == '\n')
                                current.Append("\\n");
                            else if (ch == '\t')
                                current.Append("\\t");
                            else if (ch == '\r')
                                current.Append("\\r");
                            else
                                current.Append(ch);
                        }
                    }
                }
                i += nextDelimiterOffset;
            }

            char currentChar = value[i];

            if (currentChar == Consts.DoubleQuote && (i == 1 || value[i - 1] != Consts.Backslash))
            {
                insideQuotes = !insideQuotes;
                hasQuotes = true;
                i++;
            }
            else if (currentChar == Consts.OpenBrace && !insideQuotes)
            {
                // Handle multidimensional arrays - nested opening brace
                // Flush any pending content first
                if (current.Length > 0)
                {
                    var currentIsNull = IsNull() && !hasQuotes;
                    if (quoted && !currentIsNull)
                    {
                        result.Append(Consts.DoubleQuote);
                    }
                    if (currentIsNull)
                    {
                        result.Append(Consts.Null);
                    }
                    else
                    {
                        result.Append(current);
                    }
                    if (quoted && !currentIsNull)
                    {
                        result.Append(Consts.DoubleQuote);
                    }
                    result.Append(Consts.Comma);
                    current.Clear();
                    hasQuotes = false;
                }
                result.Append(Consts.OpenBracket);
                braceDepth++;
                i++;
            }
            else if (currentChar == Consts.CloseBrace && !insideQuotes)
            {
                // Handle closing brace
                var currentIsNull = IsNull() && !hasQuotes;
                if (current.Length > 0 || hasQuotes)
                {
                    if (quoted && !currentIsNull)
                    {
                        result.Append(Consts.DoubleQuote);
                    }
                    if (currentIsNull)
                    {
                        result.Append(Consts.Null);
                    }
                    else
                    {
                        result.Append(current);
                    }
                    if (quoted && !currentIsNull)
                    {
                        result.Append(Consts.DoubleQuote);
                    }
                    current.Clear();
                    hasQuotes = false;
                }
                result.Append(Consts.CloseBracket);
                braceDepth--;
                i++;
                // Add comma after closing bracket if not at the end
                if (braceDepth > 0 && i < len && value[i] == Consts.Comma)
                {
                    result.Append(Consts.Comma);
                    i++; // Skip the comma
                }
            }
            else if (currentChar == Consts.Comma && !insideQuotes)
            {
                var currentIsNull = IsNull() && !hasQuotes;
                if (quoted && !currentIsNull)
                {
                    result.Append(Consts.DoubleQuote);
                }

                if (currentIsNull)
                {
                    result.Append(Consts.Null);
                }
                else
                {
                    result.Append(current);
                }

                if (quoted && !currentIsNull)
                {
                    result.Append(Consts.DoubleQuote);
                }
                result.Append(Consts.Comma);
                current.Clear();
                hasQuotes = false;
                i++;
            }
            else
            {
                // Handle backslash or other delimiter characters that are part of content
                if (descriptor.IsBoolean)
                {
                    if (currentChar == 't')
                        current.Append(Consts.True);
                    else if (currentChar == 'f')
                        current.Append(Consts.False);
                    else
                        current.Append(currentChar);
                }
                else if (descriptor.IsDateTime)
                {
                    current.Append(currentChar == Consts.Space ? 'T' : currentChar);
                }
                else
                {
                    if (currentChar == '\n')
                        current.Append("\\n");
                    else if (currentChar == '\t')
                        current.Append("\\t");
                    else if (currentChar == '\r')
                        current.Append("\\r");
                    else
                        current.Append(currentChar);
                }
                i++;
            }
        }

        return result.ToString().AsSpan();
    }

    /// <summary>
    /// Converts a PostgreSQL array of composite types to a JSON array of objects.
    /// Input format: {"(field1,field2,...)","(field1,field2,...)"}
    /// Output format: [{"name1":field1,"name2":field2,...},...]
    /// </summary>
    internal static ReadOnlySpan<char> PgCompositeArrayToJsonArray(
        ReadOnlySpan<char> value,
        string[] fieldNames,
        TypeDescriptor[] fieldDescriptors)
    {
        var len = value.Length;

        // Handle empty or null arrays
        if (value.IsEmpty || len < 2)
        {
            return Consts.EmptyArray.AsSpan();
        }

        // Check for empty array: {}
        if (len == 2 && value[0] == Consts.OpenBrace && value[1] == Consts.CloseBrace)
        {
            return Consts.EmptyArray.AsSpan();
        }

        // Check for multidimensional array: {{...},{...}}
        // Multidimensional composite arrays are not fully supported - fall back to treating
        // composite elements as strings to preserve the data
        if (len >= 4 && value[0] == Consts.OpenBrace && value[1] == Consts.OpenBrace)
        {
            // Use PgArrayToJsonArray which handles multidimensional arrays and will
            // serialize composite tuples as quoted strings
            return PgArrayToJsonArray(value, new TypeDescriptor("text"));
        }

        // Must start with { and end with }
        if (value[0] != Consts.OpenBrace || value[^1] != Consts.CloseBrace)
        {
            return value;
        }

        var result = new StringBuilder(len * 3);
        result.Append(Consts.OpenBracket);

        bool firstElement = true;
        int i = 1; // Skip opening {

        while (i < len - 1) // Stop before closing }
        {
            // Skip whitespace and commas between elements
            while (i < len - 1 && (value[i] == Consts.Comma || value[i] == Consts.Space))
            {
                i++;
            }

            if (i >= len - 1)
                break;

            // Each element should be quoted: "(...)"
            if (value[i] != Consts.DoubleQuote)
            {
                // Handle NULL element (unquoted NULL)
                if (i + 4 <= len && value.Slice(i, 4).SequenceEqual("NULL".AsSpan()))
                {
                    if (!firstElement)
                        result.Append(Consts.Comma);
                    firstElement = false;
                    result.Append(Consts.Null);
                    i += 4;
                    continue;
                }
                break; // Unexpected format
            }

            i++; // Skip opening quote

            // Element should start with (
            if (i >= len - 1 || value[i] != Consts.OpenParenthesis)
            {
                break; // Unexpected format
            }

            i++; // Skip (

            if (!firstElement)
                result.Append(Consts.Comma);
            firstElement = false;

            result.Append(Consts.OpenBrace);

            // Parse fields within the tuple
            int fieldIndex = 0;
            var fieldValue = new StringBuilder();
            bool insideFieldQuotes = false;
            bool fieldHasValue = false;

            while (i < len - 1 && fieldIndex < fieldNames.Length)
            {
                char c = value[i];

                // Check for end of tuple
                if (c == Consts.CloseParenthesis && !insideFieldQuotes)
                {
                    // Output current field
                    OutputField(result, fieldNames, fieldDescriptors, fieldIndex, fieldValue, fieldHasValue);
                    i++; // Skip )

                    // Skip closing quote of the element
                    if (i < len && value[i] == Consts.DoubleQuote)
                        i++;

                    break;
                }

                // Field separator
                if (c == Consts.Comma && !insideFieldQuotes)
                {
                    OutputField(result, fieldNames, fieldDescriptors, fieldIndex, fieldValue, fieldHasValue);
                    result.Append(Consts.Comma);

                    fieldIndex++;
                    fieldValue.Clear();
                    fieldHasValue = false;
                    i++;
                    continue;
                }

                // Handle quotes within field values
                if (c == Consts.DoubleQuote)
                {
                    // Check for escaped quote (double quote "")
                    if (i + 1 < len && value[i + 1] == Consts.DoubleQuote)
                    {
                        fieldValue.Append(Consts.DoubleQuote);
                        fieldHasValue = true;
                        i += 2;
                        continue;
                    }
                    else
                    {
                        // Toggle quote state
                        insideFieldQuotes = !insideFieldQuotes;
                        i++;
                        continue;
                    }
                }

                // Handle backslash escapes
                if (c == Consts.Backslash && i + 1 < len)
                {
                    char nextChar = value[i + 1];
                    if (nextChar == Consts.Backslash)
                    {
                        // Escaped backslash -> single backslash
                        fieldValue.Append(Consts.Backslash);
                        fieldHasValue = true;
                        i += 2;
                        continue;
                    }
                    else if (nextChar == Consts.DoubleQuote)
                    {
                        // \"\" (4 chars) = literal quote character inside quoted field
                        // \" (2 chars) = quote delimiter (start/end of quoted field)
                        if (i + 3 < len && value[i + 2] == Consts.Backslash && value[i + 3] == Consts.DoubleQuote)
                        {
                            // \"\" -> literal quote character
                            fieldValue.Append(Consts.DoubleQuote);
                            fieldHasValue = true;
                            i += 4;
                            continue;
                        }
                        else
                        {
                            // \" -> quote delimiter, toggle quote state
                            insideFieldQuotes = !insideFieldQuotes;
                            i += 2;
                            continue;
                        }
                    }
                }

                // Regular character
                fieldValue.Append(c);
                fieldHasValue = true;
                i++;
            }

            result.Append(Consts.CloseBrace);
        }

        result.Append(Consts.CloseBracket);
        return result.ToString().AsSpan();
    }

    private static void OutputField(
        StringBuilder result,
        string[] fieldNames,
        TypeDescriptor[] fieldDescriptors,
        int fieldIndex,
        StringBuilder fieldValue,
        bool fieldHasValue)
    {
        result.Append(Consts.DoubleQuote);
        result.Append(fieldNames[fieldIndex]);
        result.Append(Consts.DoubleQuoteColon);

        if (!fieldHasValue || fieldValue.Length == 0)
        {
            // NULL field
            result.Append(Consts.Null);
        }
        else
        {
            var descriptor = fieldDescriptors[fieldIndex];
            var valueStr = fieldValue.ToString();

            if (descriptor.IsArray)
            {
                // Array field inside composite - convert PostgreSQL array format to JSON array
                // e.g., {1,2,3} -> [1,2,3]
                result.Append(PgArrayToJsonArray(valueStr.AsSpan(), descriptor));
            }
            else if (descriptor.IsNumeric)
            {
                result.Append(valueStr);
            }
            else if (descriptor.IsBoolean)
            {
                if (valueStr == "t" || valueStr == "true")
                    result.Append(Consts.True);
                else if (valueStr == "f" || valueStr == "false")
                    result.Append(Consts.False);
                else
                    result.Append(valueStr);
            }
            else if (descriptor.IsJson)
            {
                result.Append(valueStr);
            }
            else
            {
                // String type - needs JSON escaping
                result.Append(SerializeString(valueStr));
            }
        }
    }

    internal static string QuoteText(ReadOnlySpan<char> value)
    {
        // Use SIMD-accelerated count of quotes
        int quoteCount = 0;
        var remaining = value;
        while (true)
        {
            int idx = remaining.IndexOfAny(QuoteSearchValue);
            if (idx == -1)
                break;
            quoteCount++;
            remaining = remaining.Slice(idx + 1);
        }

        int newLength = value.Length + 2 + quoteCount;
        Span<char> result = stackalloc char[newLength];
        result[0] = Consts.DoubleQuote;

        if (quoteCount == 0)
        {
            // Fast path: no quotes to escape, just copy
            value.CopyTo(result.Slice(1));
            result[newLength - 1] = Consts.DoubleQuote;
            return new string(result);
        }

        // Slow path: need to escape quotes
        int currentPos = 1;
        remaining = value;
        while (true)
        {
            int idx = remaining.IndexOfAny(QuoteSearchValue);
            if (idx == -1)
            {
                // Copy remaining and finish
                remaining.CopyTo(result.Slice(currentPos));
                currentPos += remaining.Length;
                break;
            }

            // Copy up to the quote
            remaining.Slice(0, idx).CopyTo(result.Slice(currentPos));
            currentPos += idx;

            // Escape the quote
            result[currentPos++] = Consts.DoubleQuote;
            result[currentPos++] = Consts.DoubleQuote;

            remaining = remaining.Slice(idx + 1);
        }

        result[currentPos] = Consts.DoubleQuote;
        return new string(result);
    }

    internal static string QuoteDateTime(ref ReadOnlySpan<char> value)
    {
        int newLength = value.Length + 2;
        Span<char> result = stackalloc char[newLength];
        result[0] = Consts.DoubleQuote;
        int currentPos = 1;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == Consts.Space)
            {
                result[currentPos++] = 'T';
            }
            else
            {
                result[currentPos++] = value[i];
            }
        }
        result[currentPos] = Consts.DoubleQuote;
        return new string(result);
    }

    internal static string Quote(ref ReadOnlySpan<char> value)
    {
        int newLength = value.Length + 2;
        Span<char> result = stackalloc char[newLength];
        result[0] = Consts.DoubleQuote;
        int currentPos = 1;
        for (int i = 0; i < value.Length; i++)
        {
            result[currentPos++] = value[i];
        }
        result[currentPos] = Consts.DoubleQuote;
        return new string(result);
    }

    public static int PgCountParams(this string sql)
    {
        return ParameterPattern.PostgreSqlParameterPattern().Matches(sql).Count;
    }

    public static string SerializeDatbaseObject(object? value)
    {
        if (value is null || value == DBNull.Value)
        {
            return Consts.Null;
        }
        if (value is string stringValue)
        {
            return SerializeObject(stringValue);
        }
        else if (value is int or long or double or decimal or float or short or byte)
        {
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!;
        }
        else if (value is bool boolValue)
        {
            return boolValue.ToString().ToLowerInvariant();
        }
        else if (value is DateTime dateTime)
        {
            return string.Concat("\"", dateTime.ToString("o"), "\"");
        }
        else if (value is Array array)
        {
            return FormatArray(array);
        }
        else
        {
            return string.Concat("\"", value.ToString(), "\"");
        }
    }

    private static string FormatArray(Array array)
    {
        var elements = new List<string>();

        for (int i = 0; i < array.Length; i++)
        {
            var item = array.GetValue(i);
            elements.Add(SerializeDatbaseObject(item));
        }

        return $"[{string.Join(",", elements)}]";
    }
}