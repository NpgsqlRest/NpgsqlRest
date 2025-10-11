using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace NpgsqlRest.CrudSource;

[Flags]
public enum CrudCommandType
{
    Select = 0,
    Update = 1,
    UpdateReturning = 2,
    Insert = 4,
    InsertReturning = 8,
    InsertOnConflictDoNothing = 16,
    InsertOnConflictDoUpdate = 32,
    InsertOnConflictDoNothingReturning = 64,
    InsertOnConflictDoUpdateReturning = 128,
    Delete = 256,
    DeleteReturning = 512,
    All = Select | 
        Update | 
        UpdateReturning | 
        Insert | 
        InsertReturning | 
        InsertOnConflictDoNothing | 
        InsertOnConflictDoUpdate | 
        InsertOnConflictDoNothingReturning | 
        InsertOnConflictDoUpdateReturning | 
        Delete | 
        DeleteReturning
}

public class CrudSource(
    string? schemaSimilarTo = null,
    string? schemaNotSimilarTo = null,
    string[]? includeSchemas = null,
    string[]? excludeSchemas = null,
    string? nameSimilarTo = null,
    string? nameNotSimilarTo = null,
    string[]? includeNames = null,
    string[]? excludeNames = null,
    string? query = null,
    CrudCommandType crudTypes = CrudCommandType.All,
    string returningUrlPattern = "{0}/returning",
    string onConflictDoNothingUrlPattern = "{0}/on-conflict-do-nothing",
    string onConflictDoNothingReturningUrlPattern = "{0}/on-conflict-do-nothing/returning",
    string onConflictDoUpdateUrlPattern = "{0}/on-conflict-do-update",
    string onConflictDoUpdateReturningUrlPattern = "{0}/on-conflict-do-update/returning",
    Func<Routine, CrudCommandType, bool>? created = null,
    CommentsMode? commentsMode = null) : IRoutineSource
{
    //
    // When not NULL, overrides the main option SchemaSimilarTo. It filters schemas similar to this or null to ignore this parameter.
    //
    public string? SchemaSimilarTo { get; set; } = schemaSimilarTo;
    //
    // When not NULL, overrides the main option SchemaNotSimilarTo. It filters schemas not similar to this or null to ignore this parameter.
    //
    public string? SchemaNotSimilarTo { get; set; } = schemaNotSimilarTo;
    //
    // When not NULL, overrides the main option IncludeSchemas. List of schema names to be included or null to ignore this parameter.
    //
    public string[]? IncludeSchemas { get; set; } = includeSchemas;
    //
    // When not NULL, overrides the main option ExcludeSchemas. List of schema names to be excluded or null to ignore this parameter.
    //
    public string[]? ExcludeSchemas { get; set; } = excludeSchemas;
    //
    // When not NULL, overrides the main option NameSimilarTo. It filters names similar to this or null to ignore this parameter.
    //
    public string? NameSimilarTo { get; set; } = nameSimilarTo;
    //
    // When not NULL, overrides the main option NameNotSimilarTo. It filters names not similar to this or null to ignore this parameter.
    //
    public string? NameNotSimilarTo { get; set; } = nameNotSimilarTo;
    //
    // 	When not NULL, overrides the main option IncludeNames. List of names to be included or null to ignore this parameter.
    //
    public string[]? IncludeNames { get; set; } = includeNames;
    //
    // 	When not NULL, overrides the main option ExcludeNames. List of names to be excluded or null to ignore this parameter.
    //
    public string[]? ExcludeNames { get; set; } = excludeNames;
    //
    // Custom query instead of the default one. See default in CrudSourceQuery.cs
    //
    public string? Query { get; set; } = query ?? CrudSourceQuery.Query;
    //
    // Type of CRUD queries and commands to create.
    //
    public CrudCommandType CrudTypes { get; init; } = crudTypes;
    //
    // URL pattern for all "returning" endpoints. Parameter is the original URL. Default is "{0}/returning".
    //
    public string ReturningUrlPattern { get; init; } = returningUrlPattern;
    //
    // URL pattern for all "do nothing" endpoints. Parameter is the original URL. Default is "{0}/on-conflict-do-nothing".
    //
    public string OnConflictDoNothingUrlPattern { get; init; } = onConflictDoNothingUrlPattern;
    //
    // URL pattern for all "do nothing returning " endpoints. Parameter is the original URL. Default is "{0}/on-conflict-do-nothing/returning".
    //
    public string OnConflictDoNothingReturningUrlPattern { get; init; } = onConflictDoNothingReturningUrlPattern;
    //
    // URL pattern for all "do update" endpoints. Parameter is the original URL. Default is "{0}/on-conflict-do-update".
    //
    public string OnConflictDoUpdateUrlPattern { get; init; } = onConflictDoUpdateUrlPattern;
    //
    // URL pattern for all "do update returning" endpoints. Parameter is the original URL. Default the "{0}/on-conflict-do-update/returning".
    //
    public string OnConflictDoUpdateReturningUrlPattern { get; init; } = onConflictDoUpdateReturningUrlPattern;
    //
    // Callback function, when not null it is evaluated when Routine object is created for a certain type. Return true or false to disable or enable endpoints.
    //
    public Func<Routine, CrudCommandType, bool>? Created { get; init; } = created;
    //
    // Comments mode (`Ignore`, `ParseAll`, `OnlyWithHttpTag`), when not null overrides the `CommentsMode` from the main options.
    //
    public CommentsMode? CommentsMode { get; set; } = commentsMode;

    private readonly IRoutineSourceParameterFormatter _selectParameterFormatter = new SelectParameterFormatter();
    private readonly IRoutineSourceParameterFormatter _updateParameterFormatter = new UpdateParameterFormatter();
    private readonly IRoutineSourceParameterFormatter _insertParameterFormatter = new InsertParameterFormatter();
    private readonly IRoutineSourceParameterFormatter _deleteParameterFormatter = new DeleteParameterFormatter();
    private readonly string NL = Environment.NewLine;

    private readonly string[] _selectTags = ["select", "read", "get"];
    private readonly string[] _updateTags = ["update", "post"];
    private readonly string[] _updateReturningTags = [
        "update", 
        "post",
        "update_returning",
        "returning"
    ];
    private readonly string[] _deleteTags = ["delete"];
    private readonly string[] _deleteReturningTags = [
        "delete",
        "delete_returning",
        "returning"
    ];
    private readonly string[] _insertTags = ["insert", "put", "create"];
    private readonly string[] _insertReturningTags = [
        "insert", 
        "put", 
        "create",
        "insert_returning",
        "returning"
    ];
    private readonly string[] _insertOnConflictDoNothingTags = [
        "insert", 
        "put", 
        "create",
        "insert_on_conflict_do_nothing",
        "on_conflict_do_nothing",
        "on_conflict",
    ];
    private readonly string[] _insertOnConflictDoNothingReturningTags = [
        "insert", 
        "put", 
        "create",
        "insert_on_conflict_do_nothing_returning",
        "on_conflict_do_nothing",
        "on_conflict",
        "returning"
    ];
    private readonly string[] _insertOnConflictDoUpdateTags = [
        "insert", 
        "put", 
        "create",
        "insert_on_conflict_do_update",
        "on_conflict",
        "on_conflict_do_update"
    ];
    private readonly string[] _insertOnConflictDoUpdateReturningTags = [
        "insert", 
        "put", 
        "create",
        "insert_on_conflict_do_update_returning",
        "on_conflict_do_update",
        "on_conflict",
        "returning"
    ];
    private bool Select { get => (CrudTypes & CrudCommandType.Select) == CrudCommandType.Select; }
    private bool Update { get => (CrudTypes & CrudCommandType.Update) == CrudCommandType.Update; }
    private bool UpdateReturning { get => (CrudTypes & CrudCommandType.UpdateReturning) == CrudCommandType.UpdateReturning; }
    private bool Insert { get => (CrudTypes & CrudCommandType.Insert) == CrudCommandType.Insert; }
    private bool InsertReturning { get => (CrudTypes & CrudCommandType.InsertReturning) == CrudCommandType.InsertReturning; }
    private bool InsertOnConflictDoNothing { get => (CrudTypes & CrudCommandType.InsertOnConflictDoNothing) == CrudCommandType.InsertOnConflictDoNothing; }
    private bool InsertOnConflictDoUpdate { get => (CrudTypes & CrudCommandType.InsertOnConflictDoUpdate) == CrudCommandType.InsertOnConflictDoUpdate; }
    private bool InsertOnConflictDoNothingReturning { get => (CrudTypes & CrudCommandType.InsertOnConflictDoNothingReturning) == CrudCommandType.InsertOnConflictDoNothingReturning; }
    private bool InsertOnConflictDoUpdateReturning { get => (CrudTypes & CrudCommandType.InsertOnConflictDoUpdateReturning) == CrudCommandType.InsertOnConflictDoUpdateReturning; }
    private bool Delete { get => (CrudTypes & CrudCommandType.Delete) == CrudCommandType.Delete; }
    private bool DeleteReturning { get => (CrudTypes & CrudCommandType.DeleteReturning) == CrudCommandType.DeleteReturning; }

    public IEnumerable<(Routine, IRoutineSourceParameterFormatter)> Read(
        NpgsqlRestOptions options, 
        IServiceProvider? serviceProvider, 
        RetryStrategy? retryStrategy, 
        ILogger? logger)
    {
        NpgsqlConnection? connection = null;
        bool shouldDispose = true;
        try
        {
            options.CreateAndOpenSourceConnection(serviceProvider, logger, ref connection, ref shouldDispose);
            
            if (connection is null)
            {
                yield break;
            }

            foreach (var (routine, formatter, type) in ReadInternal(options, connection, retryStrategy, logger))
            {
                if (Created is not null && !Created(routine, type))
                {
                    continue;
                }
                yield return (routine, formatter);
            }
        }
        finally
        {
            if (shouldDispose && connection is not null)
            {
                connection.Dispose();
            }
        }
    }

    private IEnumerable<(Routine routine, IRoutineSourceParameterFormatter formatter, CrudCommandType type)> ReadInternal(
        NpgsqlRestOptions options,
        NpgsqlConnection connection, 
        RetryStrategy? retryStrategy, 
        ILogger? logger)
    {
        using var command = connection.CreateCommand();
        Query ??= CrudSourceQuery.Query;
        if (Query.Contains(' ') is false)
        {
            command.CommandText = string.Concat("select * from ", Query, "($1,$2,$3,$4,$5,$6,$7,$8)");
        }
        else
        {
            command.CommandText = Query;
        }
        command.CommandText = Query;
        AddParameter(command, SchemaSimilarTo ?? options.SchemaSimilarTo); // $1
        AddParameter(command, SchemaNotSimilarTo ?? options.SchemaNotSimilarTo); // $2
        AddParameter(command, IncludeSchemas ?? options.IncludeSchemas, true); // $3
        AddParameter(command, ExcludeSchemas ?? options.ExcludeSchemas, true); // $4
        AddParameter(command, NameSimilarTo ?? options.NameSimilarTo); // $5
        AddParameter(command, NameNotSimilarTo ?? options.NameNotSimilarTo); // $6
        AddParameter(command, IncludeNames ?? options.IncludeNames, true); // $7
        AddParameter(command, ExcludeNames ?? options.ExcludeNames, true); // $8
        
        logger.TraceCommand(command, nameof(CrudCommandType));
        using NpgsqlDataReader reader = command.ExecuteReaderWithRetry(retryStrategy, logger);
        while (reader.Read())
        {
            var type = reader.Get<string>(0) switch //"type") switch
            {
                "BASE TABLE" => RoutineType.Table,
                "VIEW" => RoutineType.View,
                _ => RoutineType.Other
            };
            var schema = reader.Get<string>(1);// "schema");
            var name = reader.Get<string>(2);//"name");
            var comment = reader.Get<string>(11);//"comment");

            var columnCount = reader.Get<int>(4);//"column_count");
            var columnNames = reader.Get<string[]>(5);//"column_names");

            string[] convertedColumnNames = new string[columnNames.Length];
            for (int i = 0; i < columnNames.Length; i++)
            {
                convertedColumnNames[i] = options.NameConverter(columnNames[i]) ?? columnNames[i];
            }

            var columnTypes = reader.Get<string[]>(6);//"column_types");

            var primaryKeys = new HashSet<string>(reader.Get<string[]>(10));//"primary_keys"));
            var identityColumns = reader.Get<bool[]>(8);//"identity_columns");

            var notPrimaryKeys = columnNames.Where(x => !primaryKeys.Contains(x)).ToArray();
            var descriptors = columnTypes
                .Select((x, i) => 
                    new TypeDescriptor(x, 
                        hasDefault: true, 
                        isPk: primaryKeys.Contains(columnNames[i]),
                        isIdentity: identityColumns[i]))
                .ToArray();
            
            var updatableColumns = reader.Get<bool[]>(7);//"updatable_columns");
            bool hasPks = primaryKeys.Count > 0;
            var isInsertable = reader.Get<bool>(3);//"is_insertable");
            bool isUpdatable = updatableColumns.Any(x => x) && hasPks;
            bool doesUpdates = (Update || UpdateReturning) && isUpdatable;
            bool doesInserts = (Insert ||
                InsertOnConflictDoNothing ||
                InsertOnConflictDoNothingReturning ||
                InsertOnConflictDoUpdate ||
                InsertOnConflictDoUpdateReturning ||
                InsertReturning) && isInsertable;
            bool doesReturnings = (UpdateReturning && doesUpdates) || 
                ((InsertOnConflictDoNothingReturning ||
                InsertOnConflictDoUpdateReturning ||
                InsertReturning) && doesInserts) || DeleteReturning;

            Routine CreateRoutine(
                CrudType crudType, 
                string expression, 
                string fullDefinition, 
                string simpleDefinition,
                bool isVoid,
                string? formatUrlPattern = null,
                TypeDescriptor[]? typeDescriptors = null,
                string[]? tags = null)
            {
                TypeDescriptor[] ts = typeDescriptors ?? descriptors;
                NpgsqlRestParameter[] parameters = new NpgsqlRestParameter[columnCount];
                if (columnCount > 0)
                {
                    for (var i = 0; i < columnCount; i++)
                    {
                        var descriptor = ts[i];
                        //parameters[i] = new NpgsqlRestParameter
                        //{
                        //    Ordinal = i,
                        //    NpgsqlDbType = descriptor.ActualDbType,
                        //    ConvertedName = convertedColumnNames[i],
                        //    ActualName = columnNames[i],
                        //    TypeDescriptor = descriptor
                        //};
                        parameters[i] = new NpgsqlRestParameter(
                            options,
                            ordinal: i,
                            convertedName: convertedColumnNames[i],
                            actualName: columnNames[i],
                            typeDescriptor: descriptor);
                    }
                }

                return new Routine
                {
                    Type = type,
                    Schema = schema,
                    Name = name,
                    Comment = comment,
                    IsStrict = false,
                    CrudType = crudType,
                    ReturnsRecordType = false,
                    ReturnsSet = true,
                    ColumnCount = columnCount,
                    OriginalColumnNames = columnNames,
                    ColumnNames = convertedColumnNames,
                    ReturnsUnnamedSet = false,
                    ColumnsTypeDescriptor = ts,
                    IsVoid = isVoid,

                    ParamCount = columnCount,
                    Parameters = parameters,
                    ParamsHash = [.. parameters.Select(p => p.ConvertedName)],
                    OriginalParamsHash = [.. parameters.Select(p => p.ActualName)],

                    Expression = expression,
                    FullDefinition = fullDefinition,
                    SimpleDefinition = simpleDefinition,

                    Tags = tags,

                    FormatUrlPattern = formatUrlPattern,
                    EndpointHandler = null,
                    Metadata = null
                };
            };

            if (Select)
            {
                var expression = string.Concat(
                    "select ",
                    NL, "    ",
                    string.Join(", ", columnNames),
                    NL,
                    " from ",
                    NL, "    ",
                    schema, ".", name,
                    NL);
                var fullDefinition = string.Concat(expression, 
                    "where", 
                    NL, "    ", 
                    columnNames.Select(n => $"{n} = ?"));
                var simpleDefinition = string.Concat("select ", schema, ".", name);

                yield return (
                    CreateRoutine(
                        CrudType.Select, 
                        expression, 
                        fullDefinition, 
                        simpleDefinition,
                        isVoid: false,
                        tags: _selectTags),
                    _selectParameterFormatter,
                    CrudCommandType.Select);
            }

            string updateExp = default!, updateDef = default!, updateSimple = default!;
            if (doesUpdates)
            {
                updateExp = string.Concat(
                    "update ", schema, ".", name,
                    NL,
                    "{0}",
                    NL,
                    "{1}");
                updateDef = string.Format(updateExp,
                    string.Concat("set", NL, "    ", string.Join(", ", notPrimaryKeys.Select(n => $"{n} = ?"))),
                    string.Concat("where", NL, "    ", string.Join(" and ", notPrimaryKeys.Select(n => $"{n} = ?"))));
                updateSimple = string.Concat("update ", schema, ".", name);
            }

            if (Update && doesUpdates)
            {
                yield return (
                    CreateRoutine(
                        CrudType.Update,
                        expression: updateExp,
                        fullDefinition: updateDef,
                        simpleDefinition: updateSimple,
                        isVoid: true,
                        tags: _updateTags), 
                    _updateParameterFormatter,
                    CrudCommandType.Update);
            }

            string returningExp = default!;
            if (doesReturnings)
            {
                returningExp = string.Concat(
                    NL,
                    "returning",
                    NL, "    ",
                    string.Join(", ", columnNames));
            }

            if (UpdateReturning && doesUpdates)
            {
                yield return (
                    CreateRoutine(
                        CrudType.Update,
                        expression: string.Concat(updateExp, returningExp),
                        fullDefinition: string.Concat(updateDef, returningExp),
                        simpleDefinition: string.Concat(updateSimple, returningExp),
                        isVoid: false,
                        formatUrlPattern: ReturningUrlPattern,
                        tags: _updateReturningTags),
                    _updateParameterFormatter,
                    CrudCommandType.UpdateReturning);
            }

            string deleteExp = default!, deleteDef = default!, deleteSimple = default!;
            if (Delete || DeleteReturning)
            {
                deleteExp = string.Concat("delete from ", schema, ".", name, NL, "{0}");
                deleteDef = string.Concat("delete from ", schema, ".", name);
                deleteSimple = string.Concat("delete from ", schema, ".", name);
            }

            if (Delete)
            {
                yield return (
                    CreateRoutine(
                        CrudType.Delete,
                        expression: deleteExp,
                        fullDefinition: deleteDef,
                        simpleDefinition: deleteDef,
                        isVoid: true,
                        tags: _deleteTags),
                    _deleteParameterFormatter,
                    CrudCommandType.Delete);
            }

            if (DeleteReturning)
            {
                yield return (
                    CreateRoutine(
                        CrudType.Delete,
                        expression: string.Concat(deleteExp, returningExp),
                        fullDefinition: string.Concat(deleteDef, returningExp),
                        simpleDefinition: string.Concat(deleteDef, returningExp),
                        isVoid: false,
                        formatUrlPattern: ReturningUrlPattern,
                        tags: _deleteReturningTags),
                    _deleteParameterFormatter,
                    CrudCommandType.DeleteReturning);
            }

            if (doesInserts is false)
            {
                yield break;
            }
            
            var insertExp = string.Concat("insert into ", schema, ".", name,
                NL, "({0})",
                "{1}",
                NL, "values",
                NL, "({2})");
            var insertDef = string.Format(insertExp,
                string.Join(", ", columnNames),
                "",
                string.Join(", ", columnNames.Select(c => "?")));
            var insertSimple = string.Concat("insert into ", schema, ".", name);

            var hasDefaults = reader.Get<bool[]>(9);//"has_defaults");
            var insertTypeDescriptors = columnTypes
                .Select((x, i) =>
                    new TypeDescriptor(x,
                        hasDefault: hasDefaults[i],
                        isPk: primaryKeys.Contains(columnNames[i]),
                        isIdentity: identityColumns[i]))
                .ToArray();

            if (Insert && doesInserts)
            {
                yield return (
                    CreateRoutine(
                        CrudType.Insert,
                        expression: insertExp,
                        fullDefinition: insertDef,
                        simpleDefinition: insertSimple,
                        isVoid: true,
                        typeDescriptors: insertTypeDescriptors,
                        tags: _insertTags),
                    _insertParameterFormatter,
                    CrudCommandType.Insert);
            }

            if (InsertReturning && doesInserts)
            {
                yield return (
                    CreateRoutine(
                        CrudType.Insert,
                        expression: string.Concat(insertExp, returningExp),
                        fullDefinition: string.Concat(insertDef, returningExp),
                        simpleDefinition: string.Concat(insertSimple, returningExp),
                        isVoid: false,
                        formatUrlPattern: ReturningUrlPattern,
                        tags: _insertReturningTags),
                    _insertParameterFormatter,
                    CrudCommandType.InsertReturning);
            }

            string onConflict = default!;
            if (doesInserts && hasPks && (InsertOnConflictDoNothing ||
                InsertOnConflictDoNothingReturning ||
                InsertOnConflictDoUpdate ||
                InsertOnConflictDoUpdateReturning))
            {
                onConflict = string.Concat(NL, "on conflict (", string.Join(", ", primaryKeys), ") ");
            }

            if (InsertOnConflictDoNothing && doesInserts && hasPks)
            {
                yield return (
                    CreateRoutine(
                        CrudType.Insert,
                        expression: string.Concat(insertExp, onConflict, "do nothing"),
                        fullDefinition: string.Concat(insertDef, onConflict, "do nothing"),
                        simpleDefinition: string.Concat(insertSimple, onConflict, "do nothing"),
                        isVoid: true,
                        formatUrlPattern: OnConflictDoNothingUrlPattern,
                        typeDescriptors: insertTypeDescriptors,
                        tags: _insertOnConflictDoNothingTags),
                    _insertParameterFormatter,
                    CrudCommandType.InsertOnConflictDoNothing);
            }

            if (InsertOnConflictDoNothingReturning && doesInserts && hasPks)
            {
                yield return (
                    CreateRoutine(
                        CrudType.Insert,
                        expression: string.Concat(insertExp, onConflict, "do nothing", returningExp),
                        fullDefinition: string.Concat(insertDef, onConflict, "do nothing", returningExp),
                        simpleDefinition: string.Concat(insertSimple, onConflict, "do nothing", returningExp),
                        isVoid: false,
                        formatUrlPattern: OnConflictDoNothingReturningUrlPattern,
                        typeDescriptors: insertTypeDescriptors,
                        tags: _insertOnConflictDoNothingReturningTags),
                    _insertParameterFormatter,
                    CrudCommandType.InsertOnConflictDoNothingReturning);
            }

            string doUpdate = default!;
            if (doesInserts && hasPks && (InsertOnConflictDoUpdate || InsertOnConflictDoUpdateReturning))
            {
                doUpdate = string.Concat(NL, "do update set ", string.Join(", ", notPrimaryKeys.Select(n => $"{NL}    {n} = excluded.{n}")));
            }

            if (InsertOnConflictDoUpdate && doesInserts && hasPks)
            {
                yield return (
                    CreateRoutine(
                        CrudType.Insert,
                        expression: string.Concat(insertExp, onConflict, doUpdate),
                        fullDefinition: string.Concat(insertDef, onConflict, doUpdate),
                        simpleDefinition: string.Concat(insertSimple, onConflict, "do update"),
                        isVoid: true,
                        formatUrlPattern: OnConflictDoUpdateUrlPattern,
                        typeDescriptors: insertTypeDescriptors,
                        tags: _insertOnConflictDoUpdateTags),
                    _insertParameterFormatter,
                    CrudCommandType.InsertOnConflictDoUpdate);
            }

            if (InsertOnConflictDoUpdateReturning && doesInserts && hasPks)
            {
                yield return (
                    CreateRoutine(
                        CrudType.Insert,
                        expression: string.Concat(insertExp, onConflict, doUpdate, returningExp),
                        fullDefinition: string.Concat(insertDef, onConflict, doUpdate, returningExp),
                        simpleDefinition: string.Concat(insertSimple, onConflict, "do update", returningExp),
                        isVoid: false,
                        formatUrlPattern: OnConflictDoUpdateReturningUrlPattern,
                        typeDescriptors: insertTypeDescriptors,
                        tags: _insertOnConflictDoUpdateReturningTags),
                    _insertParameterFormatter,
                    CrudCommandType.InsertOnConflictDoUpdateReturning);
            }
        }

        yield break;
    }

    private static void AddParameter(NpgsqlCommand command, object? value, bool isArray = false)
    {
        if (value is null)
        {
            value = DBNull.Value;
        }
        else if (isArray && value is string[] array)
        {
            if (array.Length == 0)
            {
                value = DBNull.Value;
            }
        }
        else if (!isArray && value is string str)
        {
            if (string.IsNullOrWhiteSpace(str))
            {
                value = DBNull.Value;
            }
        }
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = isArray ? NpgsqlDbType.Text | NpgsqlDbType.Array : NpgsqlDbType.Text,
            Value = value
        });
    }
}
