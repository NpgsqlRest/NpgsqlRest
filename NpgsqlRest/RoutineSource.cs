using System.Text;
using Npgsql;
using NpgsqlTypes;

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
        string[]? excludeLanguages = null) : IRoutineSource
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

    public IEnumerable<(Routine, IRoutineSourceParameterFormatter)> Read(IServiceProvider? serviceProvider, RetryStrategy? retryStrategy)
    {
        bool shouldDispose = true;
        NpgsqlConnection? connection = null;
        try
        {
            Options.CreateAndOpenSourceConnection(serviceProvider, ref connection, ref shouldDispose);

            if (connection is null)
            {
                yield break;
            }

            using var command = connection.CreateCommand();
            Query ??= RoutineSourceQuery.Query;
            if (Query.Contains(Consts.Space) is false)
            {
                command.CommandText = string.Concat("select * from ", Query, "($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)");
            }
            else
            {
                command.CommandText = Query;
            }

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

            command.TraceCommand(nameof(RoutineSource));
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

                string[] expNames = new string[returnRecordNames.Length];
                var customRecTypeNames = reader.Get<string?[]>(21); //custom_rec_type_names
                if (customRecTypeNames is not null && customRecTypeNames.Length > 0)
                {
                    var customRecTypeTypes = reader.Get<string?[]>(22); //custom_rec_type_types
                    for (var i = 0; i < convertedRecordNames.Length; i++)
                    {
                        var customName = customRecTypeNames[i];
                        if (customName is not null)
                        {
                            //expNames[i] = string.Concat("(", returnRecordNames[i], "::", returnRecordTypes[i], ").", customName);
                            expNames[i] = string.Concat("(", returnRecordNames[i], ").", customName);
                            convertedRecordNames[i] = Options.NameConverter(customName) ?? customName;
                            returnRecordTypes[i] = customRecTypeTypes[i] ?? returnRecordTypes[i];
                        }
                        else
                        {
                            expNames[i] = returnRecordNames[i];
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
                
                bool isUnnamedRecord = reader.Get<bool>(11);// "is_unnamed_record");
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

                var returnRecordCount = reader.Get<int>(8);// "return_record_count");
                var variadic = reader.Get<bool>(16);// "has_variadic");
                string from;
                if (isVoid || returnRecordCount == 1)
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
                        Metadata = null
                    },
                    formatter);
            }
        }
        finally
        {
            if (connection is not null && shouldDispose is true)
            {
                connection.Dispose();
            }
        }
    }
}
