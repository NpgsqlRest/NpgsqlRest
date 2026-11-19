using System.Text;
using NpgsqlTypes;

namespace NpgsqlRest;

/// <summary>
/// JSON value formatting for column values, used by the row renderer for single-statement and multi-statement
/// results alike, so the same column types always produce the same output.
/// </summary>
internal static class JsonValueFormatter
{
    /// <summary>
    /// Formats a single column value into the output buffer as JSON: null, arrays, arrays of composite types, numbers,
    /// booleans, JSON, date/time values, and text with escaping. The value is the UTF-8 text PostgreSQL sent (every
    /// column is in text format, see AllResultTypesAreUnknown) and the output is UTF-8, so no string is created for it.
    /// </summary>
    /// <param name="raw">The value as UTF-8 text</param>
    /// <param name="isNull">True for SQL NULL</param>
    /// <param name="descriptor">Type descriptor for this column</param>
    /// <param name="output">Buffer to write the formatted value to</param>
    /// <param name="routineArrayCompositeInfo">Array composite dictionary from Routine, or null</param>
    /// <param name="columnIndex">Column index for array composite lookup</param>
    /// <param name="charBuffer">Pooled scratch for the converters that still work on text</param>
    public static void FormatValue(
        ReadOnlySpan<byte> raw,
        bool isNull,
        TypeDescriptor descriptor,
        Utf8Buffer output,
        Dictionary<int, (string[] FieldNames, TypeDescriptor[] FieldDescriptors)>? routineArrayCompositeInfo,
        int columnIndex,
        ref char[]? charBuffer)
    {
        if (isNull)
        {
            output.Append(Consts.Utf8Null);
            return;
        }

        if (descriptor.IsArray)
        {
            if (routineArrayCompositeInfo is not null &&
                routineArrayCompositeInfo.TryGetValue(columnIndex, out var arrayCompositeInfo))
            {
                // Array of composite types: converted as text
                var chars = PgUtf8Converters.DecodeChars(raw, ref charBuffer);
                output.Append(PgCompositeArrayToJsonArray(chars, arrayCompositeInfo.FieldNames, arrayCompositeInfo.FieldDescriptors));
            }
            else
            {
                PgUtf8Converters.AppendPgArrayAsJson(output, raw, descriptor);
            }
            return;
        }

        if ((descriptor.Category & (TypeCategory.Numeric | TypeCategory.Boolean | TypeCategory.Json)) != 0)
        {
            if ((descriptor.Category & TypeCategory.Boolean) != 0)
            {
                if (raw.Length == 1 && raw[0] == (byte)'t')
                {
                    output.Append("true"u8);
                }
                else if (raw.Length == 1 && raw[0] == (byte)'f')
                {
                    output.Append("false"u8);
                }
                else
                {
                    output.Append(raw);
                }
            }
            else
            {
                // numeric and json passthrough
                output.Append(raw);
            }
            return;
        }

        // Text and other types
        if (descriptor.ActualDbType == NpgsqlDbType.Unknown)
        {
            if (raw.Length < 2 || raw[0] != (byte)Consts.OpenParenthesis || raw[^1] != (byte)Consts.CloseParenthesis)
            {
                // Not a record: an enum, a domain, or another type without a specific mapping - an escaped JSON string
                PgUtf8Converters.AppendJsonString(output, raw);
            }
            else
            {
                var chars = PgUtf8Converters.DecodeChars(raw, ref charBuffer);
                output.Append(PgUnknownToJsonArray(ref chars));
            }
        }
        else if (descriptor.NeedsEscape)
        {
            PgUtf8Converters.AppendJsonString(output, raw);
        }
        else if ((descriptor.Category & TypeCategory.DateTime) != 0)
        {
            PgUtf8Converters.AppendQuotedDateTime(output, raw);
        }
        else
        {
            PgUtf8Converters.AppendQuoted(output, raw);
        }
    }
}
