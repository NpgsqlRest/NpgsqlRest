using NpgsqlTypes;

namespace NpgsqlRest;

public class TypeDescriptor
{
    public string OriginalType { get; }
    public string Type { get; }
    public bool IsArray { get; }
    public bool IsNumeric { get; }
    public bool IsJson { get; }
    public bool IsDate { get; }
    public bool IsDateTime { get; }
    public bool IsBoolean { get; }
    public bool IsText { get; }
    public NpgsqlDbType DbType { get; }
    public NpgsqlDbType BaseDbType { get; }
    public NpgsqlDbType ActualDbType { get; }
    public bool HasDefault { get; private set; }
    public bool NeedsEscape { get; }
    public bool IsPk { get; }
    public bool IsIdentity { get; }
    public string? CustomType { get; }
    public short? CustomTypePosition { get; }
    public string? OriginalParameterName { get; }
    public string? CustomTypeName { get; }
    public bool IsBinary { get; }
    internal bool ShouldRenderAsUnknownType => IsBinary is false;

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

        ActualDbType = CastToText(BaseDbType) ? NpgsqlDbType.Text : BaseDbType;

        if (IsArray)
        {
            DbType |= NpgsqlDbType.Array;
            ActualDbType |= NpgsqlDbType.Array;
        }

        IsNumeric = BaseDbType switch
        {
            NpgsqlDbType.Smallint => true,
            NpgsqlDbType.Integer => true,
            NpgsqlDbType.Bigint => true,
            NpgsqlDbType.Numeric => true,
            NpgsqlDbType.Real => true,
            NpgsqlDbType.Double => true,
            NpgsqlDbType.Money => true,
            _ => false
        };

        IsJson = BaseDbType switch
        {
            NpgsqlDbType.Jsonb => true,
            NpgsqlDbType.Json => true,
            _ => false
        };
        
        IsDate = BaseDbType == NpgsqlDbType.Date;
        
        IsBoolean = BaseDbType == NpgsqlDbType.Boolean;

        IsDateTime = BaseDbType == NpgsqlDbType.Timestamp || BaseDbType == NpgsqlDbType.TimestampTz;

        IsText = BaseDbType == NpgsqlDbType.Text ||
            BaseDbType == NpgsqlDbType.Xml ||
            BaseDbType == NpgsqlDbType.Varchar ||
            BaseDbType == NpgsqlDbType.Char ||
            BaseDbType == NpgsqlDbType.Name ||
            BaseDbType == NpgsqlDbType.Jsonb ||
            BaseDbType == NpgsqlDbType.Json ||
            BaseDbType == NpgsqlDbType.JsonPath;

        NeedsEscape = BaseDbType == NpgsqlDbType.Xml ||
            BaseDbType == NpgsqlDbType.Text ||
            BaseDbType == NpgsqlDbType.Varchar ||
            BaseDbType == NpgsqlDbType.Char ||
            BaseDbType == NpgsqlDbType.Name ||
            BaseDbType == NpgsqlDbType.JsonPath ||
            BaseDbType == NpgsqlDbType.Bytea ||
            BaseDbType == NpgsqlDbType.TsQuery ||
            BaseDbType == NpgsqlDbType.TsVector ||
            BaseDbType == NpgsqlDbType.Citext ||
            BaseDbType == NpgsqlDbType.LQuery ||
            BaseDbType == NpgsqlDbType.LTree ||
            BaseDbType == NpgsqlDbType.LTxtQuery ||
            BaseDbType == NpgsqlDbType.TsVector ||
            BaseDbType == NpgsqlDbType.Hstore;

        CustomType = customType;
        CustomTypePosition = customTypePosition;
        OriginalParameterName = originalParameterName;
        IsBinary = BaseDbType == NpgsqlDbType.Bytea;
        CustomTypeName = customTypeName;
    }

    internal void SetHasDefault()
    {
        HasDefault = true;
    }

    public bool IsCastToText() => CastToText(BaseDbType);

    private static bool CastToText(NpgsqlDbType type) => type switch
    {
        NpgsqlDbType.Interval => true,
        NpgsqlDbType.Bit => true,
        NpgsqlDbType.Varbit => true,
        NpgsqlDbType.Bytea => true,
        NpgsqlDbType.Inet => true,
        NpgsqlDbType.MacAddr => true,
        NpgsqlDbType.Cidr => true,
        NpgsqlDbType.MacAddr8 => true,
        NpgsqlDbType.TsQuery => true,
        NpgsqlDbType.TsVector => true,
        NpgsqlDbType.Box => true,
        NpgsqlDbType.Circle => true,
        NpgsqlDbType.Line => true,
        NpgsqlDbType.LSeg => true,
        NpgsqlDbType.Path => true,
        NpgsqlDbType.Point => true,
        NpgsqlDbType.Polygon => true,
        NpgsqlDbType.Oid => true,
        NpgsqlDbType.Xid => true,
        NpgsqlDbType.Xid8 => true,
        NpgsqlDbType.Cid => true,
        NpgsqlDbType.Regtype => true,
        NpgsqlDbType.Regconfig => true,
        NpgsqlDbType.IntegerRange => true,
        NpgsqlDbType.BigIntRange => true,
        NpgsqlDbType.NumericRange => true,
        NpgsqlDbType.TimestampRange => true,
        NpgsqlDbType.TimestampTzRange => true,
        NpgsqlDbType.DateRange => true,
        NpgsqlDbType.IntegerMultirange => true,
        NpgsqlDbType.BigIntMultirange => true,
        NpgsqlDbType.NumericMultirange => true,
        NpgsqlDbType.TimestampMultirange => true,
        NpgsqlDbType.TimestampTzMultirange => true,
        NpgsqlDbType.DateMultirange => true,
        NpgsqlDbType.Int2Vector => true,
        NpgsqlDbType.Oidvector => true,
        NpgsqlDbType.PgLsn => true,
        NpgsqlDbType.Tid => true,
        NpgsqlDbType.Citext => true,
        NpgsqlDbType.LQuery => true,
        NpgsqlDbType.LTree => true,
        NpgsqlDbType.LTxtQuery => true,
        NpgsqlDbType.Hstore => true,
        NpgsqlDbType.Geometry => true,
        NpgsqlDbType.Geography => true,
        _ => false
    };

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
