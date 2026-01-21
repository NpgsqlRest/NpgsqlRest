using System.Runtime.CompilerServices;
using NpgsqlTypes;

namespace NpgsqlRest;

/// <summary>
/// Flags enum representing PostgreSQL type categories for optimized type dispatch.
/// Used for fast bitwise checks instead of multiple boolean property accesses.
/// </summary>
[Flags]
public enum TypeCategory : ushort
{
    None = 0,
    /// <summary>Smallint, Integer, Bigint, Numeric, Real, Double, Money</summary>
    Numeric = 1 << 0,
    /// <summary>Boolean</summary>
    Boolean = 1 << 1,
    /// <summary>Json, Jsonb</summary>
    Json = 1 << 2,
    /// <summary>Text, Varchar, Char, Name, Xml, JsonPath</summary>
    Text = 1 << 3,
    /// <summary>Timestamp, TimestampTz</summary>
    DateTime = 1 << 4,
    /// <summary>Date</summary>
    Date = 1 << 5,
    /// <summary>Types requiring JSON string escaping</summary>
    NeedsEscape = 1 << 6,
    /// <summary>Types requiring ::text cast</summary>
    CastToText = 1 << 7,
    /// <summary>Bytea</summary>
    Binary = 1 << 8,
    /// <summary>Time, TimeTz</summary>
    Time = 1 << 9,
}

/// <summary>
/// Pre-computed lookup table for NpgsqlDbType to TypeCategory mapping.
/// Enables O(1) type category lookup instead of multiple switch expressions.
/// </summary>
public static class TypeCategoryLookup
{
    private static readonly TypeCategory[] _lookup;

    static TypeCategoryLookup()
    {
        // NpgsqlDbType values range from 0 to approximately 70+
        // Use 128 as safe upper bound
        _lookup = new TypeCategory[128];

        // Numeric types
        _lookup[(int)NpgsqlDbType.Smallint] = TypeCategory.Numeric;
        _lookup[(int)NpgsqlDbType.Integer] = TypeCategory.Numeric;
        _lookup[(int)NpgsqlDbType.Bigint] = TypeCategory.Numeric;
        _lookup[(int)NpgsqlDbType.Numeric] = TypeCategory.Numeric;
        _lookup[(int)NpgsqlDbType.Real] = TypeCategory.Numeric;
        _lookup[(int)NpgsqlDbType.Double] = TypeCategory.Numeric;
        _lookup[(int)NpgsqlDbType.Money] = TypeCategory.Numeric;

        // Boolean
        _lookup[(int)NpgsqlDbType.Boolean] = TypeCategory.Boolean;

        // JSON types
        _lookup[(int)NpgsqlDbType.Json] = TypeCategory.Json | TypeCategory.Text;
        _lookup[(int)NpgsqlDbType.Jsonb] = TypeCategory.Json | TypeCategory.Text;

        // Text types (also set NeedsEscape for those requiring JSON escaping)
        _lookup[(int)NpgsqlDbType.Text] = TypeCategory.Text | TypeCategory.NeedsEscape;
        _lookup[(int)NpgsqlDbType.Varchar] = TypeCategory.Text | TypeCategory.NeedsEscape;
        _lookup[(int)NpgsqlDbType.Char] = TypeCategory.Text | TypeCategory.NeedsEscape;
        _lookup[(int)NpgsqlDbType.Name] = TypeCategory.Text | TypeCategory.NeedsEscape;
        _lookup[(int)NpgsqlDbType.Xml] = TypeCategory.Text | TypeCategory.NeedsEscape;
        _lookup[(int)NpgsqlDbType.JsonPath] = TypeCategory.Text | TypeCategory.NeedsEscape;

        // DateTime types
        _lookup[(int)NpgsqlDbType.Timestamp] = TypeCategory.DateTime;
        _lookup[(int)NpgsqlDbType.TimestampTz] = TypeCategory.DateTime;

        // Date
        _lookup[(int)NpgsqlDbType.Date] = TypeCategory.Date;

        // Time types
        _lookup[(int)NpgsqlDbType.Time] = TypeCategory.Time;
        _lookup[(int)NpgsqlDbType.TimeTz] = TypeCategory.Time;

        // Binary
        _lookup[(int)NpgsqlDbType.Bytea] = TypeCategory.Binary | TypeCategory.NeedsEscape | TypeCategory.CastToText;

        // Types that need escape (non-text types that still need JSON escaping)
        _lookup[(int)NpgsqlDbType.TsQuery] = TypeCategory.NeedsEscape | TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.TsVector] = TypeCategory.NeedsEscape | TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Citext] = TypeCategory.NeedsEscape | TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.LQuery] = TypeCategory.NeedsEscape | TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.LTree] = TypeCategory.NeedsEscape | TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.LTxtQuery] = TypeCategory.NeedsEscape | TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Hstore] = TypeCategory.NeedsEscape | TypeCategory.CastToText;

        // Types that need cast to text (but don't necessarily need escape)
        _lookup[(int)NpgsqlDbType.Interval] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Bit] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Varbit] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Inet] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.MacAddr] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Cidr] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.MacAddr8] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Box] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Circle] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Line] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.LSeg] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Path] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Point] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Polygon] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Oid] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Xid] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Xid8] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Cid] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Regtype] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Regconfig] = TypeCategory.CastToText;
        // Note: Range and Multirange types (IntegerRange, BigIntRange, etc.) are composed values
        // (e.g., Range | Integer = 0x40000000 | 9) and cannot be stored in a simple lookup.
        // They are handled separately - the GetCategory method returns CastToText for them
        // via the IsRangeOrMultirange check.
        _lookup[(int)NpgsqlDbType.Int2Vector] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Oidvector] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.PgLsn] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Tid] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Geometry] = TypeCategory.CastToText;
        _lookup[(int)NpgsqlDbType.Geography] = TypeCategory.CastToText;

        // UUID and Refcursor don't have specific categories - they're handled as-is
        _lookup[(int)NpgsqlDbType.Uuid] = TypeCategory.None;
        _lookup[(int)NpgsqlDbType.Refcursor] = TypeCategory.Text;
    }

    // NpgsqlDbType flags for Range and Multirange
    private const int RangeFlag = 0x40000000;      // NpgsqlDbType.Range
    private const int MultirangeFlag = 0x20000000; // NpgsqlDbType.Multirange

    /// <summary>
    /// Gets the type category for the specified NpgsqlDbType.
    /// Array, Range, and Multirange modifiers are automatically stripped before lookup.
    /// Range and Multirange types are automatically classified as CastToText.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TypeCategory GetCategory(NpgsqlDbType dbType)
    {
        int rawValue = (int)dbType;

        // Check for Range or Multirange types - these always need CastToText
        if ((rawValue & (RangeFlag | MultirangeFlag)) != 0)
        {
            return TypeCategory.CastToText;
        }

        // Strip Array flag and look up in table
        int index = rawValue & ~(int)NpgsqlDbType.Array;
        return (uint)index < (uint)_lookup.Length ? _lookup[index] : TypeCategory.None;
    }
}
