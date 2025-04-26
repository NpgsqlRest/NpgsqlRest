﻿using System.Text;
using Npgsql;
using NpgsqlTypes;
using NpgsqlRest.Extensions;
using System.Collections.Frozen;
using Microsoft.Extensions.DependencyInjection;

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
        string[]? includeLanguagues = null,
        string[]? excludeLanguagues = null) : IRoutineSource
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
    public string[]? IncludeLanguagues { get; set; } = includeLanguagues;
    public string[]? ExcludeLanguagues { get; set; } = excludeLanguagues;

    public IEnumerable<(Routine, IRoutineSourceParameterFormatter)> Read(NpgsqlRestOptions options, IServiceProvider? serviceProvider)
    {
        NpgsqlConnection? connection = null;
        bool shouldDispose = true;
        try
        {
            if (serviceProvider is not null && options.ServiceProviderMode != ServiceProviderObject.None)
            {
                if (options.ServiceProviderMode == ServiceProviderObject.NpgsqlDataSource)
                {
                    connection = serviceProvider.GetRequiredService<NpgsqlDataSource>().OpenConnection();
                }
                else if (options.ServiceProviderMode == ServiceProviderObject.NpgsqlConnection)
                {
                    shouldDispose = false;
                    connection = serviceProvider.GetRequiredService<NpgsqlConnection>();
                }
            }
            else
            {
                if (options.DataSource is not null)
                {
                    connection = options.DataSource.CreateConnection();
                }
                else
                {
                    connection = new(options.ConnectionString);
                }
            }

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

            AddParameter(command, SchemaSimilarTo ?? options.SchemaSimilarTo); // $1
            AddParameter(command, SchemaNotSimilarTo ?? options.SchemaNotSimilarTo); // $2
            AddParameter(command, IncludeSchemas ?? options.IncludeSchemas, true); // $3
            AddParameter(command, ExcludeSchemas ?? options.ExcludeSchemas, true); // $4
            AddParameter(command, NameSimilarTo ?? options.NameSimilarTo); // $5
            AddParameter(command, NameNotSimilarTo ?? options.NameNotSimilarTo); // $6
            AddParameter(command, IncludeNames ?? options.IncludeNames, true); // $7
            AddParameter(command, ExcludeNames ?? options.ExcludeNames, true); // $8
            AddParameter(command, IncludeLanguagues?.Select(l => l.ToLowerInvariant()), true); // $9
            AddParameter(command, ExcludeLanguagues is null ? ["c", "internal"] : ExcludeLanguagues.Select(l => l.ToLowerInvariant()), true); // $10

            connection.Open();
            using NpgsqlDataReader reader = command.ExecuteReader();
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
                    convertedRecordNames[i] = options.NameConverter(returnRecordNames[i]) ?? returnRecordNames[i];
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
                            expNames[i] = string.Concat("(", returnRecordNames[i], "::", returnRecordTypes[i], ").", customName);
                            convertedRecordNames[i] = options.NameConverter(customName) ?? customName;
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

                if (string.IsNullOrEmpty(argumentDef))
                {
                    for (int i = 0; i < paramCount; i++)
                    {
                        paramDefaults[i] = null;
                        hasParamDefaults[i] = false;
                    }
                }
                else
                {
                    const string defaultArgExp = " DEFAULT ";
                    for (int i = 0; i < paramCount; i++)
                    {
                        string paramName = originalParamNames[i] ?? "";
                        string? nextParamName = i < paramCount - 1 ? originalParamNames[i + 1] : null;

                        int startIndex = argumentDef.IndexOf(paramName);
                        int endIndex = nextParamName != null ? argumentDef.IndexOf(string.Concat(", " + nextParamName, " ")) : argumentDef.Length;

                        if (startIndex != -1 && endIndex != -1 && startIndex < endIndex)
                        {
                            string paramDef = argumentDef[startIndex..endIndex];

                            int defaultIndex = paramDef.IndexOf(defaultArgExp);
                            if (defaultIndex != -1)
                            {
                                string defaultValue = paramDef[(defaultIndex + 9)..].Trim();

                                if (defaultValue.EndsWith(Consts.Comma))
                                {
                                    defaultValue = defaultValue[..^1].Trim();
                                }

                                paramDefaults[i] = defaultValue;
                                hasParamDefaults[i] = true;
                            }
                            else
                            {
                                paramDefaults[i] = null;
                                hasParamDefaults[i] = false;
                            }
                        }
                        else
                        {
                            paramDefaults[i] = null;
                            hasParamDefaults[i] = false;
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

                        var convertedName = options.NameConverter(paramName);
                        if (string.IsNullOrEmpty(convertedName))
                        {
                            convertedName = $"${i + 1}";
                        }

                        var descriptor = new TypeDescriptor(
                            paramType,
                            hasDefault: hasParamDefaults[i],
                            customType: customType,
                            customTypePosition: customTypePositions[i],
                            originalParameterName: originalParameterName);

                        parameters[i] = new NpgsqlRestParameter
                        {
                            Ordinal = i,
                            NpgsqlDbType = descriptor.ActualDbType,
                            ConvertedName = convertedName,
                            ActualName = originalParameterName,
                            TypeDescriptor = descriptor
                        };
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
