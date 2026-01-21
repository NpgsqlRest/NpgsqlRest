using NpgsqlTypes;

namespace NpgsqlRest;

public class TypeDescriptor
{
    public string OriginalType { get; }
    public string Type { get; }
    public bool IsArray { get; }
    public NpgsqlDbType DbType { get; }
    public NpgsqlDbType BaseDbType { get; }
    public NpgsqlDbType ActualDbType { get; }
    public bool HasDefault { get; private set; }
    public bool IsPk { get; }
    public bool IsIdentity { get; }
    public string? CustomType { get; }
    public short? CustomTypePosition { get; }
    public string? OriginalParameterName { get; }
    public string? CustomTypeName { get; }
    internal bool ShouldRenderAsUnknownType => IsBinary is false;

    /// <summary>
    /// Pre-computed type category flags for optimized type dispatch.
    /// Use bitwise operations for fast type checking in hot paths.
    /// </summary>
    public TypeCategory Category { get; }

    // Computed properties from Category for backward compatibility
    public bool IsNumeric => (Category & TypeCategory.Numeric) != 0;
    public bool IsJson => (Category & TypeCategory.Json) != 0;
    public bool IsDate => (Category & TypeCategory.Date) != 0;
    public bool IsDateTime => (Category & TypeCategory.DateTime) != 0;
    public bool IsBoolean => (Category & TypeCategory.Boolean) != 0;
    public bool IsText => (Category & TypeCategory.Text) != 0;
    public bool NeedsEscape { get; }
    public bool IsBinary => (Category & TypeCategory.Binary) != 0;

    // Properties for nested composite type support
    /// <summary>
    /// For fields that are composite types, the converted field names of the nested type.
    /// Null if this is not a composite type field.
    /// </summary>
    public string[]? CompositeFieldNames { get; internal set; }

    /// <summary>
    /// For fields that are composite types, the type descriptors for each nested field.
    /// Null if this is not a composite type field.
    /// </summary>
    public TypeDescriptor[]? CompositeFieldDescriptors { get; internal set; }

    /// <summary>
    /// For fields that are arrays of composite types, the converted field names of the element type.
    /// Null if this is not an array of composite type field.
    /// </summary>
    public string[]? ArrayCompositeFieldNames { get; internal set; }

    /// <summary>
    /// For fields that are arrays of composite types, the type descriptors for each element field.
    /// Null if this is not an array of composite type field.
    /// </summary>
    public TypeDescriptor[]? ArrayCompositeFieldDescriptors { get; internal set; }

    /// <summary>
    /// True if this field is a composite type with resolved nested field metadata.
    /// </summary>
    public bool IsCompositeType => CompositeFieldNames != null;

    /// <summary>
    /// True if this field is an array of composite types with resolved nested field metadata.
    /// </summary>
    public bool IsArrayOfCompositeType => ArrayCompositeFieldNames != null;

    public TypeDescriptor(
        string type,
        bool hasDefault = false,
        bool isPk = false,
        bool isIdentity = false,
        string? customType = null,
        short? customTypePosition = null,
        string? originalParameterName = null,
        string? customTypeName = null)
    {
        OriginalType = type;
        HasDefault = hasDefault;
        IsPk = isPk;
        IsIdentity = isIdentity;
        IsArray = type.EndsWith("[]");
        Type = (IsArray ? type[..^2] : type).Trim(Consts.DoubleQuote);
        DbType = GetDbType();
        BaseDbType = DbType;

        // Use pre-computed lookup table for type category
        Category = TypeCategoryLookup.GetCategory(BaseDbType);

        ActualDbType = (Category & TypeCategory.CastToText) != 0 ? NpgsqlDbType.Text : BaseDbType;

        if (IsArray)
        {
            DbType |= NpgsqlDbType.Array;
            ActualDbType |= NpgsqlDbType.Array;
        }

        NeedsEscape = (Category & TypeCategory.NeedsEscape) != 0;

        CustomType = customType;
        CustomTypePosition = customTypePosition;
        OriginalParameterName = originalParameterName;
        CustomTypeName = customTypeName;
    }

    internal void SetHasDefault()
    {
        HasDefault = true;
    }

    public bool IsCastToText() => (Category & TypeCategory.CastToText) != 0;

    private NpgsqlDbType GetDbType()
    {
        // Strip type modifiers (length, precision, scale) before matching
        // e.g., "character(1)" -> "character", "numeric(10,2)" -> "numeric"
        var normalizedType = Type;
        var parenIndex = Type.IndexOf('(');
        if (parenIndex > 0)
        {
            normalizedType = Type.Substring(0, parenIndex);
        }

        var result = normalizedType switch
        {
            "smallint" => NpgsqlDbType.Smallint,
            "integer" => NpgsqlDbType.Integer,
            "bigint" => NpgsqlDbType.Bigint,
            "decimal" => NpgsqlDbType.Numeric,
            "numeric" => NpgsqlDbType.Numeric,
            "real" => NpgsqlDbType.Real,
            "double precision" => NpgsqlDbType.Double,
            "int2" => NpgsqlDbType.Smallint,
            "int4" => NpgsqlDbType.Integer,
            "int8" => NpgsqlDbType.Bigint,
            "float4" => NpgsqlDbType.Real,
            "float8" => NpgsqlDbType.Double,
            "money" => NpgsqlDbType.Money,
            "smallserial" => NpgsqlDbType.Smallint,
            "serial" => NpgsqlDbType.Integer,
            "bigserial" => NpgsqlDbType.Bigint,

            "text" => NpgsqlDbType.Text,
            "xml" => NpgsqlDbType.Xml,
            "varchar" => NpgsqlDbType.Varchar,
            "character varying" => NpgsqlDbType.Varchar,
            "bpchar" => NpgsqlDbType.Char,
            "character" => NpgsqlDbType.Char,
            "char" => NpgsqlDbType.Char,
            "name" => NpgsqlDbType.Name,
            "refcursor" => NpgsqlDbType.Refcursor,
            "jsonb" => NpgsqlDbType.Jsonb,
            "json" => NpgsqlDbType.Json,
            "jsonpath" => NpgsqlDbType.JsonPath,

            "timestamp" => NpgsqlDbType.Timestamp,
            "timestamptz" => NpgsqlDbType.TimestampTz,
            "timestamp without time zone" => NpgsqlDbType.Timestamp,
            "timestamp with time zone" => NpgsqlDbType.TimestampTz,
            "date" => NpgsqlDbType.Date,
            "time" => NpgsqlDbType.Time,
            "timetz" => NpgsqlDbType.TimeTz,
            "time without time zone" => NpgsqlDbType.Time,
            "time with time zone" => NpgsqlDbType.TimeTz,
            "interval" => NpgsqlDbType.Interval,

            "bool" => NpgsqlDbType.Boolean,
            "boolean" => NpgsqlDbType.Boolean,
            "bytea" => NpgsqlDbType.Bytea,
            "uuid" => NpgsqlDbType.Uuid,
            "bit varying" => NpgsqlDbType.Varbit,
            "varbit" => NpgsqlDbType.Varbit,
            "bit" => NpgsqlDbType.Bit,

            "cidr" => NpgsqlDbType.Cidr,
            "inet" => NpgsqlDbType.Inet,
            "macaddr" => NpgsqlDbType.MacAddr,
            "macaddr8" => NpgsqlDbType.MacAddr8,

            "tsquery" => NpgsqlDbType.TsQuery,
            "tsvector" => NpgsqlDbType.TsVector,

            "box" => NpgsqlDbType.Box,
            "circle" => NpgsqlDbType.Circle,
            "line" => NpgsqlDbType.Line,
            "lseg" => NpgsqlDbType.LSeg,
            "path" => NpgsqlDbType.Path,
            "point" => NpgsqlDbType.Point,
            "polygon" => NpgsqlDbType.Polygon,

            "oid" => NpgsqlDbType.Oid,
            "xid" => NpgsqlDbType.Xid,
            "xid8" => NpgsqlDbType.Xid8,
            "cid" => NpgsqlDbType.Cid,
            "regtype" => NpgsqlDbType.Regtype,
            "regconfig" => NpgsqlDbType.Regconfig,

            "int4range" => NpgsqlDbType.IntegerRange,
            "int8range" => NpgsqlDbType.BigIntRange,
            "numrange" => NpgsqlDbType.NumericRange,
            "tsrange" => NpgsqlDbType.TimestampRange,
            "tstzrange" => NpgsqlDbType.TimestampTzRange,
            "daterange" => NpgsqlDbType.DateRange,

            "int4multirange" => NpgsqlDbType.IntegerMultirange,
            "int8multirange" => NpgsqlDbType.BigIntMultirange,
            "nummultirange" => NpgsqlDbType.NumericMultirange,
            "tsmultirange" => NpgsqlDbType.TimestampMultirange,
            "tstzmultirange" => NpgsqlDbType.TimestampTzMultirange,
            "datemultirange" => NpgsqlDbType.DateMultirange,

            "int2vector" => NpgsqlDbType.Int2Vector,
            "oidvector" => NpgsqlDbType.Oidvector,
            "pg_lsn" => NpgsqlDbType.PgLsn,
            "tid" => NpgsqlDbType.Tid,

            "citext" => NpgsqlDbType.Citext,
            "lquery" => NpgsqlDbType.LQuery,
            "ltree" => NpgsqlDbType.LTree,
            "ltxtquery" => NpgsqlDbType.LTxtQuery,
            "hstore" => NpgsqlDbType.Hstore,
            "geometry" => NpgsqlDbType.Geometry,
            "geography" => NpgsqlDbType.Geography,

            _ => NpgsqlDbType.Unknown
        };
        return result;
    }
}
