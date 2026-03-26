namespace NpgsqlRest;

public class Routine
{
    /// <summary>
    /// Routine type: Function, Procedure, Table, View, or other.
    /// </summary>
    public required RoutineType Type { get; init; }

    /// <summary>
    /// Schema name (parsed by quote_ident).
    /// </summary>
    public required string Schema { get; init; }

    /// <summary>
    /// PostgreSQL object name (parsed by quote_ident).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// PostgreSQL object comment.
    /// </summary>
    public required string? Comment { get; init; }

    /// <summary>
    /// Strict or non-strict function. Strict functions will return NULL if any parameter is NULL.
    /// </summary>
    public required bool IsStrict { get; init; }

    /// <summary>
    /// The type of CRUD operation associated with the routine (Select, Insert, Update or Delete).
    /// </summary>
    public required CrudType CrudType { get; init; }

    /// <summary>
    /// Indicates whether the routine returns a PostgreSQL record type.
    /// </summary>
    public required bool ReturnsRecordType { get; init; }

    /// <summary>
    /// Indicates whether the routine returns a set of records or single record.
    /// </summary>
    public required bool ReturnsSet { get; init; }

    /// <summary>
    /// The number of columns returned.
    /// </summary>
    public required int ColumnCount { get; init; }

    /// <summary>
    /// The names of the returned columns.
    /// </summary>
    public required string[] OriginalColumnNames { get; init; }

    /// <summary>
    /// The converted names of the returned columns.
    /// </summary>
    public required string[] ColumnNames { get; init; }

    /// <summary>
    /// The type descriptors for the returned columns.
    /// </summary>
    public required TypeDescriptor[] ColumnsTypeDescriptor { get; init; }

    /// <summary>
    /// Indicates whether the routine returns an unnamed set. 
    /// </summary>
    public required bool ReturnsUnnamedSet { get; init; }

    /// <summary>
    /// Indicates whether the routine returns void.
    /// </summary>
    public required bool IsVoid { get; init; }

    /// <summary>
    /// The number of parameters.
    /// </summary>
    public required int ParamCount { get; init; }

    /// <summary>
    /// Parameters associated with the routine in the order they appear in the routine.
    /// </summary>
    public required NpgsqlRestParameter[] Parameters { get; init; }

    /// <summary>
    /// The hash of the parameters associated with the routine.
    /// </summary>
    public required HashSet<string> ParamsHash { get; init; }

    /// <summary>
    /// The hash of the parameters associated with the routine.
    /// </summary>
    public required HashSet<string> OriginalParamsHash { get; init; }

    /// <summary>
    /// The expression associated with the routine.
    /// </summary>
    public required string Expression { get; init; }

    /// <summary>
    /// The full definition of the routine. (Used for code-gen comments)
    /// </summary>
    public required string FullDefinition { get; init; }

    /// <summary>
    /// The simple definition of the routine. (Used for code-gen comments)
    /// </summary>
    public required string SimpleDefinition { get; init; }

    /// <summary>
    /// The format URL pattern for the routine. If used (not null), placeholder {0} is used to replace the default URL.
    /// </summary>
    public required string? FormatUrlPattern { get; init; }

    /// <summary>
    /// The tags associated with the routine.
    /// </summary>
    public required string[]? Tags { get; init; }

    /// <summary>
    /// The endpoint handler for the routine.
    /// </summary>
    public required Func<RoutineEndpoint?, RoutineEndpoint?>? EndpointHandler { get; init; }

    /// <summary>
    /// Routine is immutable. Will return same results for same inputs.
    /// </summary>
    public bool Immutable { get; init; } = false;
    /// <summary>
    /// The meta data associated with the routine.
    /// </summary>
    public required object? Metadata { get; init; }

    internal bool[]? UnknownResultTypeList { get; set; } = null;

    /// <summary>
    /// When NestedJsonForCompositeTypes is enabled via comment annotation, this contains information
    /// about which columns are composite types and their field names/types for nested JSON serialization.
    /// Key: first expanded column index for the composite type
    /// Value: (fieldNames, fieldDescriptors, convertedColumnName, expandedColumnIndices)
    /// </summary>
    public Dictionary<int, (string[] FieldNames, TypeDescriptor[] FieldDescriptors, string ConvertedColumnName, int[] ExpandedColumnIndices)>? CompositeColumnInfo { get; set; } = null;

    /// <summary>
    /// When NestedJsonForCompositeTypes is enabled, this contains information about columns that are
    /// arrays of composite types. These arrays need special serialization to convert from PostgreSQL
    /// tuple format {"(f1,f2,f3)"} to JSON array of objects [{"field1":f1,"field2":f2}].
    /// Key: column index (0-based)
    /// Value: (fieldNames for the composite element type, fieldDescriptors for each field)
    /// </summary>
    public Dictionary<int, (string[] FieldNames, TypeDescriptor[] FieldDescriptors)>? ArrayCompositeColumnInfo { get; set; } = null;

    /// <summary>
    /// For multi-command SQL files: per-command column metadata and command names.
    /// When not null, the rendering uses NpgsqlBatch + NextResult() to iterate result sets
    /// and wraps them in a JSON object {resultName: [rows], ...}. Void commands get null.
    /// </summary>
    public MultiCommandInfo[]? MultiCommandInfo { get; set; } = null;

    /// <summary>
    /// True if this routine uses multi-command rendering (JSON object with named result sets).
    /// </summary>
    public bool IsMultiCommand => MultiCommandInfo is not null;
}

/// <summary>
/// Metadata for one command in a multi-command SQL file endpoint.
/// </summary>
public class MultiCommandInfo
{
    /// <summary>
    /// Result key name in the JSON response. From @resultN annotation or default pattern.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The SQL statement for this command.
    /// </summary>
    public required string Statement { get; init; }

    /// <summary>
    /// Number of parameters this command uses (max $N index).
    /// </summary>
    public required int ParamCount { get; init; }

    /// <summary>
    /// Column count for this command's result set. 0 for void commands.
    /// </summary>
    public required int ColumnCount { get; init; }

    /// <summary>
    /// Column names for this command's result set (camelCase converted).
    /// </summary>
    public required string[] ColumnNames { get; init; }

    /// <summary>
    /// Type descriptors for this command's result columns.
    /// </summary>
    public required TypeDescriptor[] ColumnTypeDescriptors { get; init; }

    /// <summary>
    /// When true, this command returns a flat array of values instead of objects.
    /// </summary>
    public bool ReturnsUnnamedSet { get; init; }
}
