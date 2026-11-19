using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;

namespace NpgsqlRest;

/// <summary>
/// UTF-8 counterparts of the <see cref="PgConverters"/> text helpers: they take a PostgreSQL value as the UTF-8 text
/// it arrives in and append its JSON (or CSV) form to a <see cref="Utf8Buffer"/>, without creating strings.
/// Output is byte-for-byte what the string-based helpers produce once encoded as UTF-8.
/// </summary>
internal static class PgUtf8Converters
{
    // The same encoder PgConverters.SerializeString uses through JsonSerializer, so escaping is identical.
    private static readonly JavaScriptEncoder JsonEncoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    private static readonly SearchValues<byte> ArrayDelimiters = SearchValues.Create(",{}\"\\"u8);

    private const byte Quote = (byte)'"';

    /// <summary>Appends <paramref name="utf8"/> as a JSON string literal (quoted and escaped).</summary>
    public static void AppendJsonString(Utf8Buffer output, ReadOnlySpan<byte> utf8)
    {
        int first = JsonEncoder.FindFirstCharacterToEncodeUtf8(utf8);
        if (first < 0)
        {
            // Nothing to escape - by far the common case: one copy.
            var span = output.GetSpan(utf8.Length + 2);
            span[0] = Quote;
            utf8.CopyTo(span.Slice(1));
            span[utf8.Length + 1] = Quote;
            output.Advance(utf8.Length + 2);
            return;
        }
        AppendJsonStringEscaped(output, utf8, first);
    }

    private static void AppendJsonStringEscaped(Utf8Buffer output, ReadOnlySpan<byte> utf8, int first)
    {
        // Worst case every remaining byte becomes a six-byte \uXXXX escape.
        var span = output.GetSpan(checked(first + (utf8.Length - first) * 6 + 2));
        span[0] = Quote;
        utf8.Slice(0, first).CopyTo(span.Slice(1));
        JsonEncoder.EncodeUtf8(utf8.Slice(first), span.Slice(1 + first), out _, out int written, isFinalBlock: true);
        int end = 1 + first + written;
        span[end] = Quote;
        output.Advance(end + 1);
    }

    /// <summary>Appends the value between double quotes, unescaped (for types whose text never needs escaping).</summary>
    public static void AppendQuoted(Utf8Buffer output, ReadOnlySpan<byte> utf8)
    {
        var span = output.GetSpan(utf8.Length + 2);
        span[0] = Quote;
        utf8.CopyTo(span.Slice(1));
        span[utf8.Length + 1] = Quote;
        output.Advance(utf8.Length + 2);
    }

    /// <summary>
    /// Appends a date/time value between double quotes with every space replaced by 'T'
    /// ("2026-01-01 10:00:00+00" becomes "2026-01-01T10:00:00+00").
    /// </summary>
    public static void AppendQuotedDateTime(Utf8Buffer output, ReadOnlySpan<byte> utf8)
    {
        var span = output.GetSpan(utf8.Length + 2);
        span[0] = Quote;
        var destination = span.Slice(1, utf8.Length);
        utf8.CopyTo(destination);
        destination.Replace((byte)' ', (byte)'T');
        span[utf8.Length + 1] = Quote;
        output.Advance(utf8.Length + 2);
    }

    /// <summary>Appends the value as a CSV field: between double quotes, with embedded quotes doubled.</summary>
    public static void AppendCsvQuoted(Utf8Buffer output, ReadOnlySpan<byte> utf8)
    {
        output.Append(Quote);
        while (true)
        {
            int index = utf8.IndexOf(Quote);
            if (index < 0)
            {
                output.Append(utf8);
                break;
            }
            output.Append(utf8.Slice(0, index + 1));
            output.Append(Quote);
            utf8 = utf8.Slice(index + 1);
        }
        output.Append(Quote);
    }

    /// <summary>
    /// Appends a PostgreSQL array literal ({a,b,"c d",NULL}, including multidimensional arrays) as a JSON array.
    /// The UTF-8 port of <see cref="PgConverters.PgArrayToJsonArray"/>; the delimiters are ASCII, and a UTF-8
    /// multi-byte sequence never contains an ASCII byte, so scanning bytes for them is safe.
    /// </summary>
    public static void AppendPgArrayAsJson(Utf8Buffer output, ReadOnlySpan<byte> value, TypeDescriptor descriptor)
    {
        var len = value.Length;
        if (value.IsEmpty || len < 3 || value[0] != (byte)'{' || value[^1] != (byte)'}')
        {
            if (descriptor.IsArray is true)
            {
                output.Append("[]"u8);
            }
            else
            {
                output.Append(value);
            }
            return;
        }

        var current = Utf8Buffer.Rent(); // the element being collected (unescaped)
        try
        {
            output.Append((byte)'[');
            var quoted = (descriptor.Category & (TypeCategory.Numeric | TypeCategory.Boolean | TypeCategory.Json)) == 0;
            var isBoolean = (descriptor.Category & TypeCategory.Boolean) != 0;
            var isDateTime = (descriptor.Category & TypeCategory.DateTime) != 0;
            bool insideQuotes = false;
            bool hasQuotes = false;
            int braceDepth = 1; // the opening brace is already consumed

            int i = 1;
            while (i < len)
            {
                var remaining = value.Slice(i);
                int nextDelimiterOffset = remaining.IndexOfAny(ArrayDelimiters);
                if (nextDelimiterOffset == -1)
                {
                    // No more delimiters; this shouldn't happen with well-formed input
                    break;
                }

                if (nextDelimiterOffset > 0)
                {
                    var segment = remaining.Slice(0, nextDelimiterOffset);
                    if (isBoolean)
                    {
                        foreach (var ch in segment)
                        {
                            if (ch == (byte)'t')
                            {
                                current.Append("true"u8);
                            }
                            else if (ch == (byte)'f')
                            {
                                current.Append("false"u8);
                            }
                            else
                            {
                                current.Append(ch);
                            }
                        }
                    }
                    else if (isDateTime)
                    {
                        var destination = current.GetSpan(segment.Length).Slice(0, segment.Length);
                        segment.CopyTo(destination);
                        destination.Replace((byte)' ', (byte)'T');
                        current.Advance(segment.Length);
                    }
                    else
                    {
                        current.Append(segment);
                    }
                    i += nextDelimiterOffset;
                }

                byte currentChar = value[i];

                // Escapes (\\ and \") are consumed by the backslash branch below (backslash is a scan delimiter), so a
                // quote that reaches here is never escaped.
                if (currentChar == Quote)
                {
                    if (insideQuotes && i + 1 < len && value[i + 1] == Quote)
                    {
                        // "" inside quotes means a literal quote character
                        current.Append(Quote);
                        i += 2;
                    }
                    else
                    {
                        insideQuotes = !insideQuotes;
                        hasQuotes = true;
                        i++;
                    }
                }
                else if (currentChar == (byte)'{' && !insideQuotes)
                {
                    // Multidimensional arrays: nested opening brace. Flush any pending content first.
                    if (current.Length > 0)
                    {
                        AppendArrayElement(output, current, quoted, hasQuotes);
                        output.Append((byte)',');
                        current.Clear();
                        hasQuotes = false;
                    }
                    output.Append((byte)'[');
                    braceDepth++;
                    i++;
                }
                else if (currentChar == (byte)'}' && !insideQuotes)
                {
                    if (current.Length > 0 || hasQuotes)
                    {
                        AppendArrayElement(output, current, quoted, hasQuotes);
                        current.Clear();
                        hasQuotes = false;
                    }
                    output.Append((byte)']');
                    braceDepth--;
                    i++;
                    // Comma after a closing bracket, if not at the end
                    if (braceDepth > 0 && i < len && value[i] == (byte)',')
                    {
                        output.Append((byte)',');
                        i++;
                    }
                }
                else if (currentChar == (byte)',' && !insideQuotes)
                {
                    AppendArrayElement(output, current, quoted, hasQuotes);
                    output.Append((byte)',');
                    current.Clear();
                    hasQuotes = false;
                    i++;
                }
                else
                {
                    // A backslash, or a delimiter character that is part of the content
                    if (isBoolean)
                    {
                        if (currentChar == (byte)'t')
                        {
                            current.Append("true"u8);
                        }
                        else if (currentChar == (byte)'f')
                        {
                            current.Append("false"u8);
                        }
                        else
                        {
                            current.Append(currentChar);
                        }
                        i++;
                    }
                    else if (isDateTime)
                    {
                        current.Append(currentChar == (byte)' ' ? (byte)'T' : currentChar);
                        i++;
                    }
                    else if (currentChar == (byte)'\\' && i + 1 < len)
                    {
                        byte nextChar = value[i + 1];
                        if (nextChar == (byte)'\\')
                        {
                            // \\ -> single backslash
                            current.Append((byte)'\\');
                            i += 2;
                        }
                        else if (nextChar == Quote)
                        {
                            // \" -> literal quote
                            current.Append(Quote);
                            i += 2;
                        }
                        else
                        {
                            // Other escape - keep as-is (shouldn't happen in well-formed input)
                            current.Append(currentChar);
                            i++;
                        }
                    }
                    else
                    {
                        current.Append(currentChar);
                        i++;
                    }
                }
            }
        }
        finally
        {
            Utf8Buffer.Return(current);
        }
    }

    private static void AppendArrayElement(Utf8Buffer output, Utf8Buffer current, bool quoted, bool hasQuotes)
    {
        var element = current.WrittenSpan;
        // An unquoted NULL is the SQL null; a quoted "NULL" is the text
        if (!hasQuotes && element.SequenceEqual("NULL"u8))
        {
            output.Append("null"u8);
        }
        else if (quoted)
        {
            AppendJsonString(output, element);
        }
        else
        {
            output.Append(element);
        }
    }

    /// <summary>
    /// Decodes UTF-8 into a pooled char buffer for the converters that still work on text (composite types, arrays of
    /// composite types, records). The returned span is valid until the next call with the same buffer.
    /// </summary>
    public static ReadOnlySpan<char> DecodeChars(ReadOnlySpan<byte> utf8, ref char[]? buffer)
    {
        // UTF-16 never needs more code units than UTF-8 needs bytes.
        if (buffer is null || buffer.Length < utf8.Length)
        {
            if (buffer is not null)
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
            buffer = ArrayPool<char>.Shared.Rent(Math.Max(utf8.Length, 256));
        }
        int chars = Encoding.UTF8.GetChars(utf8, buffer);
        return new ReadOnlySpan<char>(buffer, 0, chars);
    }
}
