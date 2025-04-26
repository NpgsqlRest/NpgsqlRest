﻿using System.Collections.Frozen;

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
}
