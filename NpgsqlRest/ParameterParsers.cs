using System.Globalization;
using System.Runtime.CompilerServices;
using NpgsqlTypes;

namespace NpgsqlRest;

/// <summary>
/// Pre-computed delegate array for type-specific parameter parsing.
/// Replaces sequential if-chain with O(1) delegate lookup.
/// </summary>
public static class ParameterParsers
{
    /// <summary>
    /// Delegate signature for type-specific parsing.
    /// </summary>
    /// <param name="value">The string value to parse</param>
    /// <param name="result">The parsed result if successful</param>
    /// <returns>True if parsing succeeded, false otherwise</returns>
    public delegate bool TryParseDelegate(string? value, out object? result);

    private static readonly TryParseDelegate?[] _parsers;

    static ParameterParsers()
    {
        _parsers = new TryParseDelegate?[128];

        // Numeric types
        _parsers[(int)NpgsqlDbType.Smallint] = TryParseSmallint;
        _parsers[(int)NpgsqlDbType.Integer] = TryParseInteger;
        _parsers[(int)NpgsqlDbType.Bigint] = TryParseBigint;
        _parsers[(int)NpgsqlDbType.Double] = TryParseDouble;
        _parsers[(int)NpgsqlDbType.Real] = TryParseReal;
        _parsers[(int)NpgsqlDbType.Numeric] = TryParseDecimal;
        _parsers[(int)NpgsqlDbType.Money] = TryParseDecimal;

        // Boolean
        _parsers[(int)NpgsqlDbType.Boolean] = TryParseBoolean;

        // DateTime types
        _parsers[(int)NpgsqlDbType.Timestamp] = TryParseTimestamp;
        _parsers[(int)NpgsqlDbType.TimestampTz] = TryParseTimestampTz;
        _parsers[(int)NpgsqlDbType.Date] = TryParseDate;
        _parsers[(int)NpgsqlDbType.Time] = TryParseTime;
        _parsers[(int)NpgsqlDbType.TimeTz] = TryParseTimeTz;

        // UUID
        _parsers[(int)NpgsqlDbType.Uuid] = TryParseUuid;

        // Text types - handled separately (just return string as-is)
        // Not added to lookup - will fall through to text handling
    }

    // NpgsqlDbType flags for Range and Multirange
    private const int RangeFlag = 0x40000000;      // NpgsqlDbType.Range
    private const int MultirangeFlag = 0x20000000; // NpgsqlDbType.Multirange

    /// <summary>
    /// Gets the parser delegate for the specified NpgsqlDbType.
    /// Returns null if the type should be handled as text.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TryParseDelegate? GetParser(NpgsqlDbType dbType)
    {
        int rawValue = (int)dbType;

        // Range and Multirange types are handled as text
        if ((rawValue & (RangeFlag | MultirangeFlag)) != 0)
        {
            return null;
        }

        // Strip Array flag and look up in table
        int index = rawValue & ~(int)NpgsqlDbType.Array;
        return (uint)index < (uint)_parsers.Length ? _parsers[index] : null;
    }

    private static bool TryParseSmallint(string? value, out object? result)
    {
        if (short.TryParse(value, CultureInfo.InvariantCulture.NumberFormat, out var v))
        {
            result = v;
            return true;
        }
        result = null;
        return false;
    }

    private static bool TryParseInteger(string? value, out object? result)
    {
        if (int.TryParse(value, CultureInfo.InvariantCulture.NumberFormat, out var v))
        {
            result = v;
            return true;
        }
        result = null;
        return false;
    }

    private static bool TryParseBigint(string? value, out object? result)
    {
        if (long.TryParse(value, CultureInfo.InvariantCulture.NumberFormat, out var v))
        {
            result = v;
            return true;
        }
        result = null;
        return false;
    }

    private static bool TryParseDouble(string? value, out object? result)
    {
        if (double.TryParse(value, CultureInfo.InvariantCulture.NumberFormat, out var v))
        {
            result = v;
            return true;
        }
        result = null;
        return false;
    }

    private static bool TryParseReal(string? value, out object? result)
    {
        if (float.TryParse(value, CultureInfo.InvariantCulture.NumberFormat, out var v))
        {
            result = v;
            return true;
        }
        result = null;
        return false;
    }

    private static bool TryParseDecimal(string? value, out object? result)
    {
        if (decimal.TryParse(value, CultureInfo.InvariantCulture.NumberFormat, out var v))
        {
            result = v;
            return true;
        }
        result = null;
        return false;
    }

    private static bool TryParseBoolean(string? value, out object? result)
    {
        if (bool.TryParse(value, out var v))
        {
            result = v;
            return true;
        }
        result = null;
        return false;
    }

    private static bool TryParseTimestamp(string? value, out object? result)
    {
        if (DateTime.TryParse(value, out var v))
        {
            result = v;
            return true;
        }
        result = null;
        return false;
    }

    private static bool TryParseTimestampTz(string? value, out object? result)
    {
        if (DateTime.TryParse(value, out var v))
        {
            result = DateTime.SpecifyKind(v, DateTimeKind.Utc);
            return true;
        }
        result = null;
        return false;
    }

    private static bool TryParseDate(string? value, out object? result)
    {
        if (DateOnly.TryParse(value, out var v))
        {
            result = v;
            return true;
        }
        result = null;
        return false;
    }

    private static bool TryParseTime(string? value, out object? result)
    {
        if (DateTime.TryParse(value, out var v))
        {
            result = TimeOnly.FromDateTime(v);
            return true;
        }
        result = null;
        return false;
    }

    private static bool TryParseTimeTz(string? value, out object? result)
    {
        if (DateTime.TryParse(value, out var v))
        {
            result = new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc));
            return true;
        }
        result = null;
        return false;
    }

    private static bool TryParseUuid(string? value, out object? result)
    {
        if (Guid.TryParse(value, out var v))
        {
            result = v;
            return true;
        }
        result = null;
        return false;
    }
}
