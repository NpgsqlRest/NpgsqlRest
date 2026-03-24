using System.Text;
using NpgsqlTypes;

namespace NpgsqlRest;

/// <summary>
/// Shared JSON value formatting for column values.
/// Used by both single-command and multi-command rendering paths
/// to guarantee identical output for the same column types.
/// </summary>
internal static class JsonValueFormatter
{
    /// <summary>
    /// Format a single column value into the output buffer as JSON.
    /// Handles: null, arrays, array-of-composites, numeric, boolean, JSON, datetime, text with escaping.
    /// </summary>
    /// <param name="raw">Raw string value from PostgreSQL (via AllResultTypesAreUnknown)</param>
    /// <param name="value">The original object value (for DBNull check)</param>
    /// <param name="descriptor">Type descriptor for this column</param>
    /// <param name="outputBuffer">StringBuilder to write formatted value to</param>
    /// <param name="routineArrayCompositeInfo">Array composite dictionary from Routine, or null</param>
    /// <param name="columnIndex">Column index for array composite lookup</param>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static void FormatValue(
        ReadOnlySpan<char> raw,
        object value,
        TypeDescriptor descriptor,
        StringBuilder outputBuffer,
        Dictionary<int, (string[] FieldNames, TypeDescriptor[] FieldDescriptors)>? routineArrayCompositeInfo,
        int columnIndex)
    {
        if (value == DBNull.Value)
        {
            outputBuffer.Append(Consts.Null);
            return;
        }

        if (descriptor.IsArray)
        {
            // Check if this is an array of composite types
            if (routineArrayCompositeInfo is not null &&
                routineArrayCompositeInfo.TryGetValue(columnIndex, out var arrayCompositeInfo))
            {
                outputBuffer.Append(PgCompositeArrayToJsonArray(raw, arrayCompositeInfo.FieldNames, arrayCompositeInfo.FieldDescriptors));
            }
            else
            {
                outputBuffer.Append(PgArrayToJsonArray(raw, descriptor));
            }
            return;
        }

        if ((descriptor.Category & (TypeCategory.Numeric | TypeCategory.Boolean | TypeCategory.Json)) != 0)
        {
            if ((descriptor.Category & TypeCategory.Boolean) != 0)
            {
                if (raw.Length == 1 && raw[0] == 't')
                {
                    outputBuffer.Append(Consts.True);
                }
                else if (raw.Length == 1 && raw[0] == 'f')
                {
                    outputBuffer.Append(Consts.False);
                }
                else
                {
                    outputBuffer.Append(raw);
                }
            }
            else
            {
                // numeric and json passthrough
                outputBuffer.Append(raw);
            }
            return;
        }

        // Text and other types
        if (descriptor.ActualDbType == NpgsqlDbType.Unknown)
        {
            outputBuffer.Append(PgUnknownToJsonArray(ref raw));
        }
        else if (descriptor.NeedsEscape)
        {
            outputBuffer.Append(SerializeString(ref raw));
        }
        else if ((descriptor.Category & TypeCategory.DateTime) != 0)
        {
            outputBuffer.Append(QuoteDateTime(ref raw));
        }
        else
        {
            outputBuffer.Append(Quote(ref raw));
        }
    }
}
