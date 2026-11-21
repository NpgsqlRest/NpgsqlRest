using System.Text;
using Npgsql;

namespace NpgsqlRest;

public class RoutineSource(
    string? schemaSimilarTo = null,
    string? schemaNotSimilarTo = null,
    string[]? includeSchemas = null,
    string[]? excludeSchemas = null,
    string? nameSimilarTo = null,
    string? nameNotSimilarTo = null,
    string[]? includeNames = null,
    string[]? excludeNames = null,
    string? query = null,
    CommentsMode? commentsMode = null,
    string? customTypeParameterSeparator = "_",
    string[]? includeLanguages = null,
    string[]? excludeLanguages = null,
    bool nestedJsonForCompositeTypes = false,
    bool resolveNestedCompositeTypes = true) : IRoutineSource
{
    public string? SchemaSimilarTo { get; set; } = schemaSimilarTo;
    public string? SchemaNotSimilarTo { get; set; } = schemaNotSimilarTo;
    public string[]? IncludeSchemas { get; set; } = includeSchemas;
    public string[]? ExcludeSchemas { get; set; } = excludeSchemas;
    public string? NameSimilarTo { get; set; } = nameSimilarTo;
    public string? NameNotSimilarTo { get; set; } = nameNotSimilarTo;
    public string[]? IncludeNames { get; set; } = includeNames;
    public string[]? ExcludeNames { get; set; } = excludeNames;
    public string? Query { get; set; } = query ?? RoutineSourceQuery.Query;
    public CommentsMode? CommentsMode { get; set; } = commentsMode;
    public string? CustomTypeParameterSeparator { get; set; } = customTypeParameterSeparator;
    public string[]? IncludeLanguages { get; set; } = includeLanguages;
    public string[]? ExcludeLanguages { get; set; } = excludeLanguages;
    public bool NestedJsonForCompositeTypes { get; set; } = nestedJsonForCompositeTypes;

    /// <summary>
    /// Named connection (a DataSources/ConnectionStrings key) this source discovers against - and the
    /// execution default for every endpoint it yields (an explicit `connection` annotation wins).
    /// Null (the default) resolves through MetadataQueryConnectionName and the default connection.
    /// </summary>
    public string? ConnectionName { get; set; }

    /// <summary>
    /// When true, nested composite types and arrays of composite types within composite fields
    /// are serialized as JSON objects/arrays instead of PostgreSQL tuple strings.
    /// For example, a nested composite "(1,x)" becomes {"id":1,"name":"x"}.
    /// Default: true (nested composites are serialized as proper JSON objects).
    /// Set to false for backwards compatibility or to reduce startup overhead for schemas
    /// with thousands of composite types.
    /// </summary>
    public bool ResolveNestedCompositeTypes { get; set; } = resolveNestedCompositeTypes;

    /// <summary>
    /// The discovery command (query text + the ten filter parameters, each resolved against this source
    /// or the global options) — shared by <see cref="Read"/> and <see cref="CreateFingerprintCommand"/>.
    /// With <paramref name="fingerprint"/> the SAME query is wrapped in an order-independent server-side
    /// hash of its result rows: if the hash changes, the discovered endpoints changed — by definition.
    /// </summary>
    private NpgsqlCommand CreateSourceCommand(NpgsqlConnection connection, bool fingerprint)
    {
        var command = connection.CreateCommand();
        Query ??= RoutineSourceQuery.Query;
        var text = Query.Contains(Consts.Space) is false
            ? string.Concat("select * from ", Query, "($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)")
            : Query;
        command.CommandText = fingerprint
            ? string.Concat("select coalesce(sum(hashtextextended(q::text, 0)), 0)::text from (", text, ") q")
            : text;

        command.AddParameter(SchemaSimilarTo ?? Options.SchemaSimilarTo); // $1
        command.AddParameter(SchemaNotSimilarTo ?? Options.SchemaNotSimilarTo); // $2
        command.AddParameter(IncludeSchemas ?? Options.IncludeSchemas, true); // $3
        command.AddParameter(ExcludeSchemas ?? Options.ExcludeSchemas, true); // $4
        command.AddParameter(NameSimilarTo ?? Options.NameSimilarTo); // $5
        command.AddParameter(NameNotSimilarTo ?? Options.NameNotSimilarTo); // $6
        command.AddParameter(IncludeNames ?? Options.IncludeNames, true); // $7
        command.AddParameter(ExcludeNames ?? Options.ExcludeNames, true); // $8
        command.AddParameter(IncludeLanguages, true); // $9
        command.AddParameter(ExcludeLanguages is null ? ["c", "internal"] : ExcludeLanguages, true); // $10
        return command;
    }

    // Routines whose comment asks for an endpoint (an HTTP line) and that the filters of this source leave out,
    // each with the first filter that leaves it out. Same ten parameters as the discovery query.
    private const string FilteredOutQuery = """
    select
        lower(r.routine_type),
        quote_ident(r.specific_schema),
        quote_ident(r.routine_name),
        pg_get_function_identity_arguments(p.oid),
        case
            when has_schema_privilege(current_user, n.nspname, 'USAGE') is false then 'privilege'
            when $1 is not null and n.nspname not similar to $1 then 'SchemaSimilarTo'
            when $2 is not null and n.nspname similar to $2 then 'SchemaNotSimilarTo'
            when $3 is not null and not n.nspname = any($3) then 'IncludeSchemas'
            when $4 is not null and n.nspname = any($4) then 'ExcludeSchemas'
            when $5 is not null and r.routine_name not similar to $5 then 'NameSimilarTo'
            when $6 is not null and r.routine_name similar to $6 then 'NameNotSimilarTo'
            when $7 is not null and not r.routine_name = any($7) then 'IncludeNames'
            when $8 is not null and r.routine_name = any($8) then 'ExcludeNames'
            when $9 is not null and not lower(r.external_language) = any($9) then 'IncludeLanguages'
            when $10 is not null and lower(r.external_language) = any($10) then 'ExcludeLanguages'
        end,
        lower(r.external_language)
    from information_schema.routines r
    join pg_catalog.pg_proc p on r.specific_name = p.proname || '_' || p.oid
    join pg_catalog.pg_namespace n on n.oid = p.pronamespace
    join pg_catalog.pg_description d on d.objoid = p.oid and d.classoid = 'pg_catalog.pg_proc'::regclass and d.objsubid = 0
    where
        n.nspname not like 'pg_%'
        and n.nspname <> 'information_schema'
        and r.routine_type in ('FUNCTION', 'PROCEDURE')
        and coalesce(r.type_udt_name, '') <> 'trigger'
        and d.description ~* '(^|\n)[ \t]*@?http([ \t\r]|$|\n)'
        and (
            has_schema_privilege(current_user, n.nspname, 'USAGE') is false
            or ($1 is not null and n.nspname not similar to $1)
            or ($2 is not null and n.nspname similar to $2)
            or ($3 is not null and not n.nspname = any($3))
            or ($4 is not null and n.nspname = any($4))
            or ($5 is not null and r.routine_name not similar to $5)
            or ($6 is not null and r.routine_name similar to $6)
            or ($7 is not null and not r.routine_name = any($7))
            or ($8 is not null and r.routine_name = any($8))
            or ($9 is not null and not lower(r.external_language) = any($9))
            or ($10 is not null and lower(r.external_language) = any($10))
        )
    order by 2, 3
    """;

    /// <summary>
    /// For tools (EndpointReport.IncludeFilteredOut): the routines that have an HTTP annotation and are not read
    /// at all, because a schema, name or language filter of this source leaves them out. Not done for a custom
    /// discovery query, whose filters are its own.
    /// </summary>
    private void ReportFilteredOut(NpgsqlConnection connection)
    {
        if (EndpointReport.IncludeFilteredOut is false || (Query is not null && ReferenceEquals(Query, RoutineSourceQuery.Query) is false))
        {
            return;
        }
        try
        {
            using var command = CreateSourceCommand(connection, fingerprint: false);
            command.CommandText = FilteredOutQuery;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var filter = reader.IsDBNull(4) ? "a filter" : reader.GetString(4);
                var schema = reader.GetString(1);
                var name = reader.GetString(2);
                var language = reader.IsDBNull(5) ? "" : reader.GetString(5);
                EndpointReport.AddNotExposed(new NotExposedEntry
                {
                    Kind = reader.GetString(0),
                    Schema = schema,
                    Name = name,
                    Definition = string.Concat(reader.GetString(0), " ", schema, ".", name, "(", reader.IsDBNull(3) ? "" : reader.GetString(3), ")"),
                    Reason = "excluded_by_filter",
                    Message = filter switch
                    {
                        "privilege" => $"The connection's user has no USAGE privilege on the schema {schema}.",
                        "SchemaSimilarTo" or "SchemaNotSimilarTo" or "IncludeSchemas" or "ExcludeSchemas"
                            => $"Its schema, {schema}, is left out by the {filter} setting.",
                        "IncludeLanguages" or "ExcludeLanguages"
                            => $"Its language, {language}, is left out by the {filter} setting.",
                        _ => $"Its name is left out by the {filter} setting."
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Logger?.LogDebug("RoutineSource: the routines left out by filters could not be listed: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// A command returning one text value: a hash of this source's ENTIRE discovery result (same query,
    /// same configured filters). Watch mode polls it to detect any change to the discovered endpoints —
    /// routines, their comments/annotations, and the composite/table types their signatures use.
    /// </summary>
    public NpgsqlCommand CreateFingerprintCommand(NpgsqlConnection connection)
        => CreateSourceCommand(connection, fingerprint: true);

    public IEnumerable<(Routine, IRoutineSourceParameterFormatter)> Read(IServiceProvider? serviceProvider, RetryStrategy? retryStrategy)
    {
        bool shouldDispose = true;
        NpgsqlConnection? connection = null;
        try
        {
            Options.CreateAndOpenSourceConnection(serviceProvider, ref connection, ref shouldDispose, nameof(RoutineSource), ConnectionName);

            if (connection is null)
            {
                yield break;
            }

            // Initialize composite type cache if deep nested type resolution is enabled
            if (ResolveNestedCompositeTypes)
            {
                CompositeTypeCache.Initialize(connection, Options.NameConverter);
            }

            using var command = CreateSourceCommand(connection, fingerprint: false);
            command.LogCommand(nameof(RoutineSource));
            using NpgsqlDataReader reader = command.ExecuteReaderWithRetry(retryStrategy);
            while (reader.Read())
            {
                var type = reader.Get<string>(0);//"type");
                var paramTypes = reader.Get<string[]>(14);// "param_types");
                var returnType = reader.Get<string>(7);// "return_type");
                var name = reader.Get<string>(2);// "name");

                var volatility = reader.Get<char>(5);//"volatility_option");

                var hasGet =
                    name.Contains("_get_", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("get_", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("_get", StringComparison.OrdinalIgnoreCase);
                var crudType = hasGet ? CrudType.Select : (volatility == 'v' ? CrudType.Update : CrudType.Select);

                var originalParamNames = reader.Get<string[]>(13);//"param_names");
                var isVoid = string.Equals(returnType, "void", StringComparison.Ordinal);
                var schema = reader.Get<string>(1);//"schema");
                var returnsSet = reader.Get<bool>(6);//"returns_set");

                var returnRecordNames = reader.Get<string[]>(9);//"return_record_names");

                string[] convertedRecordNames = new string[returnRecordNames.Length];
                for (int i = 0; i < returnRecordNames.Length; i++)
                {
                    convertedRecordNames[i] = Options.NameConverter(returnRecordNames[i]) ?? returnRecordNames[i];
                }

                var returnRecordTypes = reader.Get<string[]>(10);//"return_record_types");
                var isUnnamedRecord = reader.Get<bool>(11);// "is_unnamed_record");

                string[] expNames = new string[returnRecordNames.Length];
                Dictionary<int, (string[] FieldNames, TypeDescriptor[] FieldDescriptors, string OriginalColumnName, int[] ExpandedColumnIndices)>? compositeColumnInfo = null;
                var customRecTypeNames = reader.Get<string?[]>(21); //custom_rec_type_names
                var compositeOutParamNames = reader.Get<string[]?>(23); //composite_out_param_names

                if (customRecTypeNames is not null && customRecTypeNames.Length > 0)
                {
                    var customRecTypeTypes = reader.Get<string?[]>(22); //custom_rec_type_types

                    // Track composite column metadata for potential nested JSON serialization
                    // Group fields by their original column (they share the same returnRecordNames prefix)
                    Dictionary<string, List<(int OriginalIndex, string FieldName, string FieldType)>>? fieldsByColumn = null;

                    for (var i = 0; i < convertedRecordNames.Length; i++)
                    {
                        var customName = customRecTypeNames[i];
                        if (customName is not null)
                        {
                            expNames[i] = string.Concat("(", returnRecordNames[i], ").", customName);
                            convertedRecordNames[i] = Options.NameConverter(customName) ?? customName;
                            returnRecordTypes[i] = customRecTypeTypes[i] ?? returnRecordTypes[i];

                            // Build composite metadata in single pass
                            fieldsByColumn ??= new();
                            var columnName = returnRecordNames[i];
                            if (!fieldsByColumn.TryGetValue(columnName, out var fieldList))
                            {
                                fieldList = new List<(int, string, string)>();
                                fieldsByColumn[columnName] = fieldList;
                            }
                            fieldList.Add((i, customName, customRecTypeTypes[i] ?? returnRecordTypes[i]));
                        }
                        else
                        {
                            expNames[i] = returnRecordNames[i];
                        }
                    }

                    if (fieldsByColumn is not null)
                    {
                        // Build composite info without LINQ allocations
                        compositeColumnInfo = new(fieldsByColumn.Count);
                        foreach (var (columnName, fields) in fieldsByColumn)
                        {
                            var count = fields.Count;
                            var fieldNames = new string[count];
                            var fieldDescriptors = new TypeDescriptor[count];
                            var expandedIndices = new int[count];

                            for (var j = 0; j < count; j++)
                            {
                                var field = fields[j];
                                fieldNames[j] = Options.NameConverter(field.FieldName) ?? field.FieldName;
                                fieldDescriptors[j] = new TypeDescriptor(field.FieldType);
                                expandedIndices[j] = field.OriginalIndex;
                            }

                            compositeColumnInfo[fields[0].OriginalIndex] = (
                                fieldNames,
                                fieldDescriptors,
                                Options.NameConverter(columnName) ?? columnName,
                                expandedIndices
                            );
                        }
                    }
                }
                else if (compositeOutParamNames is not null && compositeOutParamNames.Length > 0)
                {
                    // Handle case where return is composite type with named OUT params
                    // e.g., returns table (req nested_request) - all columns belong to one composite
                    // returnRecordNames contains the composite field names, compositeOutParamNames has the column name
                    for (var i = 0; i < returnRecordNames.Length; i++)
                    {
                        expNames[i] = returnRecordNames[i];
                    }

                    // Group all fields under each OUT param name
                    // For single composite column: compositeOutParamNames = ['req'], returnRecordNames = ['id', 'text_value', 'flag']
                    if (compositeOutParamNames.Length == 1)
                    {
                        // All fields belong to single composite column
                        var columnName = compositeOutParamNames[0];
                        var count = returnRecordNames.Length;
                        var fieldNames = new string[count];
                        var fieldDescriptors = new TypeDescriptor[count];
                        var expandedIndices = new int[count];

                        for (var j = 0; j < count; j++)
                        {
                            fieldNames[j] = convertedRecordNames[j];
                            fieldDescriptors[j] = new TypeDescriptor(returnRecordTypes[j]);
                            expandedIndices[j] = j;
                        }

                        compositeColumnInfo = new(1)
                        {
                            [0] = (fieldNames, fieldDescriptors, Options.NameConverter(columnName) ?? columnName, expandedIndices)
                        };
                    }
                }
                else if (string.Equals(returnType, "record", StringComparison.OrdinalIgnoreCase) && !isUnnamedRecord && !returnsSet && returnRecordNames.Length >= 1)
                {
                    // Handle case where function returns a named composite type directly (not via OUT params)
                    // e.g., CREATE FUNCTION get_single_bool_field_type() RETURNS single_bool_field_type
                    // In this case, returnType is "record" and all returnRecordNames belong to the composite
                    // We use the FROM syntax: select field1, field2 from schema.function()
                    // This expands the composite fields so they can be serialized individually
                    for (var i = 0; i < returnRecordNames.Length; i++)
                    {
                        expNames[i] = returnRecordNames[i];
                    }
                    // Note: We intentionally do NOT set compositeColumnInfo here because we want the fields
                    // to be serialized directly as a flat JSON object, not nested under a composite column name
                }

                // Read array composite type info (columns 25, 26, 27)
                // These are populated when OUT params include arrays of composite types
                Dictionary<int, (string[] FieldNames, TypeDescriptor[] FieldDescriptors)>? arrayCompositeColumnInfo = null;
                var arrayColumnIndices = reader.Get<int[]?>(25); // array_column_indices (1-based from SQL)
                if (arrayColumnIndices is not null && arrayColumnIndices.Length > 0)
                {
                    var arrayFieldNamesJson = reader.Get<string?>(26); // array_field_names_json (JSON string)
                    var arrayFieldTypesJson = reader.Get<string?>(27); // array_field_types_json (JSON string)

                    if (arrayFieldNamesJson is not null && arrayFieldTypesJson is not null)
                    {
                        // Parse JSON arrays: [["field1","field2"],["field1","field2"]]
                        var arrayFieldNames = System.Text.Json.JsonSerializer.Deserialize(arrayFieldNamesJson, NpgsqlRestSerializerContext.Default.StringArrayArray);
                        var arrayFieldTypes = System.Text.Json.JsonSerializer.Deserialize(arrayFieldTypesJson, NpgsqlRestSerializerContext.Default.StringArrayArray);

                        if (arrayFieldNames is not null && arrayFieldTypes is not null)
                        {
                            arrayCompositeColumnInfo = new(arrayColumnIndices.Length);
                            for (var i = 0; i < arrayColumnIndices.Length; i++)
                            {
                                var colIndex = arrayColumnIndices[i] - 1; // Convert to 0-based
                                var fieldNames = arrayFieldNames[i];
                                var fieldTypes = arrayFieldTypes[i];

                                var convertedFieldNames = new string[fieldNames.Length];
                                var fieldDescriptors = new TypeDescriptor[fieldNames.Length];
                                for (var j = 0; j < fieldNames.Length; j++)
                                {
                                    convertedFieldNames[j] = Options.NameConverter(fieldNames[j]) ?? fieldNames[j];
                                    fieldDescriptors[j] = new TypeDescriptor(fieldTypes[j]);

                                    // Resolve nested composite types if option is enabled
                                    if (ResolveNestedCompositeTypes)
                                    {
                                        ResolveNestedTypesForDescriptor(fieldDescriptors[j]);
                                    }
                                }

                                arrayCompositeColumnInfo[colIndex] = (convertedFieldNames, fieldDescriptors);
                            }
                        }
                    }
                }

                TypeDescriptor[] returnTypeDescriptor;
                if (isVoid)
                {
                    returnTypeDescriptor = [];
                }
                else
                {
                    returnTypeDescriptor = [.. returnRecordTypes.Select(x => new TypeDescriptor(x))];
                }

                var routineType = type.GetEnum<RoutineType>();
                var callIdent = routineType == RoutineType.Procedure ? "call " : "select ";
                var paramCount = reader.Get<int>(12);// "param_count");
                var argumentDef = reader.Get<string>(15);

                string?[] paramDefaults = new string?[paramCount];
                bool[] hasParamDefaults = new bool[paramCount];

                if (string.IsNullOrEmpty(argumentDef) is false)
                {
                    const string defaultArgExp = " DEFAULT ";
                    var defParts = argumentDef.Split(Consts.Comma, StringSplitOptions.TrimEntries);
                    for (int i = 0; i < paramCount; i++)
                    {
                        string paramName = originalParamNames[i];
                        if (paramName is null)
                        {
                            continue;
                        }
                        if (i < defParts.Length)
                        { 
                            if (defParts[i].Contains(paramName) is false)
                            {
                                continue;
                            }
                            int idx = defParts[i].IndexOf(defaultArgExp);
                            if (idx != -1)
                            {
                                string defaultValue = defParts[i][(idx + defaultArgExp.Length)..];
                                paramDefaults[i] = defaultValue;
                                hasParamDefaults[i] = true;
                            }
                            else
                            {
                                paramDefaults[i] = null;
                                hasParamDefaults[i] = false;
                            }
                        }
                    }
                }

                var returnRecordCount = returnRecordNames.Length; // Use actual array length (may have changed for nested JSON)
                var variadic = reader.Get<bool>(16);// "has_variadic");
                string from;
                // When function returns a named composite type (returnType == "record" but not unnamed),
                // we need field expansion via FROM syntax even when there's only one field,
                // otherwise PostgreSQL returns the composite representation like "(value)"
                bool hasNamedCompositeReturn = string.Equals(returnType, "record", StringComparison.OrdinalIgnoreCase) && !isUnnamedRecord;
                if (isVoid || (returnRecordCount == 1 && !hasNamedCompositeReturn))
                {
                    from = callIdent;
                }
                else
                {
                    //from = string.Concat(callIdent, string.Join(",", returnRecordNames), " from ");
                    StringBuilder sb = new();
                    for (int i = 0; i < returnRecordCount; i++)
                    {
                        sb.Append(expNames[i] ?? returnRecordNames[i]);
                        if (i < returnRecordCount - 1)
                        {
                            sb.Append(Consts.Comma);
                        }
                    }
                    from = string.Concat(callIdent, sb.ToString(), " from ");
                }
                var expression = string.Concat(
                    from,
                    schema,
                    ".",
                    name,
                    "(",
                    variadic && paramCount > 0 ? "variadic " : "");

                var simpleDefinition = new StringBuilder();
                simpleDefinition.AppendLine(string.Concat(
                    routineType.ToString().ToLower(), " ",
                    schema, ".",
                    name, "(",
                    paramCount == 0 ? ")" : ""));


                var customTypeNames = reader.Get<string?[]>(18);
                var customTypeTypes = reader.Get<string?[]>(19);
                var customTypePositions = reader.Get<short?[]>(20);

                NpgsqlRestParameter[] parameters = new NpgsqlRestParameter[paramCount];
                bool hasCustomType = false;
                if (paramCount > 0)
                {
                    for (var i = 0; i < paramCount; i++)
                    {
                        var paramName = originalParamNames[i];
                        var originalParameterName = paramName;
                        var customTypeName = customTypeNames[i];
                        string? customType;
                        if (customTypeName != null)
                        {
                            customType = paramTypes[i];
                            paramTypes[i] = customTypeTypes[i] ?? customType;
                            paramName = string.Concat(paramName, CustomTypeParameterSeparator, customTypeName);
                            originalParamNames[i] = paramName;
                            if (hasCustomType is false)
                            {
                                hasCustomType = true;
                            }
                        }
                        else
                        {
                            customType = null;
                        }
                        var defaultValue = paramDefaults[i];
                        var paramType = paramTypes[i];
                        
                        var fullParamType = defaultValue == null ? paramType : $"{paramType} DEFAULT {defaultValue}";
                        simpleDefinition
                            .AppendLine(string.Concat("    ", paramName, " ", fullParamType, i == paramCount - 1 ? "" : ","));

                        var convertedName = Options.NameConverter(paramName);
                        if (string.IsNullOrEmpty(convertedName))
                        {
                            convertedName = $"${i + 1}";
                        }

                        var descriptor = new TypeDescriptor(
                            paramType,
                            hasDefault: hasParamDefaults[i], 
                            customType: customType,
                            customTypePosition: customTypePositions[i],
                            originalParameterName: originalParameterName,
                            customTypeName: customTypeName);

                        //parameters[i] = new NpgsqlRestParameter
                        //{
                        //    Ordinal = i,
                        //    NpgsqlDbType = descriptor.ActualDbType,
                        //    ConvertedName = convertedName,
                        //    ActualName = originalParameterName,
                        //    TypeDescriptor = descriptor
                        //};
                        parameters[i] = new NpgsqlRestParameter(
                            ordinal: i,
                            convertedName: convertedName,
                            actualName: originalParameterName,
                            typeDescriptor: descriptor);
                        // For an expanded composite field, ActualName stays the shared base
                        // (originalParameterName, e.g. "_response"); record the unique per-field name
                        // (paramName, e.g. "_response_body") as an alias so annotations that reference
                        // the name shown in the signature (e.g. @body_parameter_name) still resolve.
                        if (customTypeName != null)
                        {
                            parameters[i].ExpandedName = paramName;
                        }
                    }
                    simpleDefinition.AppendLine(")");
                }

                if (!returnsSet)
                {
                    simpleDefinition.AppendLine(string.Concat("returns ", returnType));
                }
                else
                {
                    if (isUnnamedRecord)
                    {
                        simpleDefinition.AppendLine(string.Concat($"returns setof {returnType}"));
                    }
                    else
                    {
                        simpleDefinition.AppendLine("returns table(");

                        for (var i = 0; i < returnRecordCount; i++)
                        {
                            var returnParamName = returnRecordNames[i];
                            var returnParamType = returnRecordTypes[i];
                            simpleDefinition
                                .AppendLine(string.Concat("    ", returnParamName, " ", returnParamType, i == returnRecordCount - 1 ? "" : ","));
                        }
                        simpleDefinition.AppendLine(")");
                    }
                }

                IRoutineSourceParameterFormatter formatter;
                if (hasCustomType is false)
                {
                    formatter = new RoutineSourceParameterFormatter();
                }
                else
                {
                    formatter = new RoutineSourceCustomTypesParameterFormatter();
                }

                string[] jsonColumnNames = new string[convertedRecordNames.Length];
                for (int i = 0; i < convertedRecordNames.Length; i++)
                {
                    jsonColumnNames[i] = PgConverters.SerializeString(convertedRecordNames[i]);
                }

                yield return (
                    new Routine
                    {
                        Type = routineType,
                        Schema = schema,
                        Name = name,
                        Comment = reader.Get<string>(3),//"comment"),
                        IsStrict = reader.Get<bool>(4),//"is_strict"),
                        CrudType = crudType,

                        ReturnsRecordType = string.Equals(returnType, "record", StringComparison.OrdinalIgnoreCase),
                        ReturnsSet = returnsSet,
                        ColumnCount = returnRecordCount,
                        OriginalColumnNames = returnRecordNames,
                        ColumnNames = convertedRecordNames,
                        JsonColumnNames = jsonColumnNames,
                        ReturnsUnnamedSet = isUnnamedRecord,
                        ColumnsTypeDescriptor = returnTypeDescriptor,
                        IsVoid = isVoid,

                        ParamCount = paramCount,
                        Parameters = parameters,
                        ParamsHash = [.. parameters.Select(p => p.ConvertedName)],
                        OriginalParamsHash = [.. parameters.Select(p => p.ActualName)],

                        Expression = expression,
                        FullDefinition = reader.Get<string>(17),//"definition"),
                        SimpleDefinition = simpleDefinition.ToString(),
                        Immutable = volatility == 'i',
                        Tags = [routineType.ToString().ToLowerInvariant(), volatility switch
                        {
                            'v' => "volatile",
                            's' => "stable",
                            'i' => "immutable",
                            _ => "other"
                        }],

                        FormatUrlPattern = null,
                        EndpointHandler = null,
                        Metadata = null,
                        CompositeColumnInfo = compositeColumnInfo,
                        ArrayCompositeColumnInfo = arrayCompositeColumnInfo
                    },
                    formatter);
            }
            reader.Close();
            ReportFilteredOut(connection);
        }
        finally
        {
            if (connection is not null && shouldDispose is true)
            {
                connection.Dispose();
            }
        }
    }

    /// <summary>
    /// Resolves nested composite type metadata for a field descriptor.
    /// Looks up the field type in the CompositeTypeCache and populates
    /// nested field names and descriptors if the field is a composite type.
    /// </summary>
    private static void ResolveNestedTypesForDescriptor(TypeDescriptor descriptor)
    {
        // Check if this is a composite type
        var compositeMetadata = CompositeTypeCache.GetType(descriptor.OriginalType);
        if (compositeMetadata != null)
        {
            descriptor.CompositeFieldNames = compositeMetadata.ConvertedFieldNames;
            descriptor.CompositeFieldDescriptors = compositeMetadata.FieldDescriptors;
            return;
        }

        // Check if this is an array of composite types
        if (descriptor.IsArray)
        {
            var elementMetadata = CompositeTypeCache.GetArrayElementType(descriptor.OriginalType);
            if (elementMetadata != null)
            {
                descriptor.ArrayCompositeFieldNames = elementMetadata.ConvertedFieldNames;
                descriptor.ArrayCompositeFieldDescriptors = elementMetadata.FieldDescriptors;
            }
        }
    }
}
