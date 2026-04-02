using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlRest.HttpClientType;

namespace NpgsqlRest.SqlFileSource;

/// <summary>
/// Routine source that scans SQL files matching a glob pattern and generates REST API endpoints.
/// Each SQL file must contain exactly one statement (multi-statement support planned for a future version).
/// </summary>
public class SqlFileSource(SqlFileSourceOptions options) : IEndpointSource
{
    private const string UnnamedColumnPrefix = "column";

    public CommentsMode? CommentsMode { get; set; } = options.CommentsMode; // Always has a value from options default
    public bool NestedJsonForCompositeTypes { get; set; } = false;

    public IEnumerable<(Routine, IRoutineSourceParameterFormatter)> Read(
        IServiceProvider? serviceProvider,
        RetryStrategy? retryStrategy)
    {
        if (string.IsNullOrEmpty(options.FilePattern))
        {
            yield break;
        }

        var files = FindMatchingFiles(options.FilePattern).ToArray();
        if (files.Length == 0)
        {
            NpgsqlRestOptions.Logger?.LogWarning("SqlFileSource: No SQL files found matching pattern \"{FilePattern}\"", options.FilePattern);
            yield break;
        }

        // Compute the base directory from the file pattern for subdirectory-as-schema resolution
        var baseDir = GetBaseDirectory(options.FilePattern);

        // Open a single connection for all Describe calls — same as RoutineSource and CrudSource
        NpgsqlConnection? connection = null;
        bool shouldDispose = true;
        try
        {
            NpgsqlRestOptions.Options.CreateAndOpenSourceConnection(serviceProvider, ref connection, ref shouldDispose, nameof(SqlFileSource));

            if (connection is null)
            {
                yield break;
            }

            var nameConverter = NpgsqlRestOptions.Options.NameConverter;

            // Initialize composite type cache for custom type column resolution
            CompositeTypeCache.Initialize(connection, nameConverter);

            foreach (var filePath in files)
            {
                (Routine, IRoutineSourceParameterFormatter)? result = null;
                try
                {
                    result = ProcessFile(filePath, baseDir, connection, nameConverter, options);
                }
                catch (Exception ex)
                {
                    NpgsqlRestOptions.Logger?.LogError("SqlFileSource: Error processing file {FilePath}: {Error}", filePath, ex.Message);
                    if (options.ErrorMode == ParseErrorMode.Exit)
                    {
                        NpgsqlRestOptions.Logger?.LogCritical("SqlFileSource: Exiting due to SQL file error. Set ErrorMode to Skip to continue past errors.");
                        Environment.Exit(1);
                    }
                }

                if (result is not null)
                {
                    yield return result.Value;
                }
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

    private static (Routine, IRoutineSourceParameterFormatter)? ProcessFile(
        string filePath,
        string baseDir,
        NpgsqlConnection connection,
        Func<string?, string?> nameConverter,
        SqlFileSourceOptions options)
    {
        var content = File.ReadAllText(filePath);
        var parseResult = SqlFileParser.Parse(content, options.CommentScope);


        // When CommentsMode is OnlyWithHttpTag, skip files that don't have an HTTP tag
        // BEFORE attempting to describe — avoids errors on non-endpoint SQL files (migrations, utility scripts, etc.)
        if (options.CommentsMode == NpgsqlRest.CommentsMode.OnlyWithHttpTag && !HasHttpTag(parseResult.Comment))
        {
            return null;
        }

        // Check for parse errors
        if (parseResult.Errors.Count > 0)
        {
            foreach (var error in parseResult.Errors)
            {
                NpgsqlRestOptions.Logger?.LogError("SqlFileSource: {FilePath}: {Error}", filePath, error);
            }
            if (options.ErrorMode == ParseErrorMode.Exit)
            {
                NpgsqlRestOptions.Logger?.LogCritical("SqlFileSource: Exiting due to SQL file error. Set ErrorMode to Skip to continue past errors.");
                Environment.Exit(1);
            }
            return null;
        }

        if (parseResult.Statements.Count == 0)
        {
            return null;
        }

        bool isMultiCommand = parseResult.Statements.Count > 1;

        // Check @param annotations for HTTP client type parameters (single composite, no SQL rewriting)
        Dictionary<int, (string TypeName, string ParamName, string[] FieldNames, string[] FieldTypes)>? httpTypeParams = null;
        if (NpgsqlRestOptions.Options.HttpClientOptions.Enabled && HttpClientTypes.Definitions.Count > 0)
        {
            httpTypeParams = DetectHttpTypeParams(parseResult.Comment, connection);
        }

        // Check @param annotations for non-HTTP composite type parameters (single composite, no SQL rewriting)
        var compositeTypeParams = DetectCompositeTypeParams(parseResult.Comment, connection, httpTypeParams);

        // Describe each statement individually and merge parameters
        int mergedMaxParam = 0;
        var paramTypesByIndex = new Dictionary<int, string>(); // $N index → type name
        var commandDescribes = new List<DescribeResult>();
        var paramTypeHints = SqlFileParser.ExtractParamTypeHints(parseResult.Comment);

        for (int stmtIndex = 0; stmtIndex < parseResult.Statements.Count; stmtIndex++)
        {
            var stmt = parseResult.Statements[stmtIndex];
            int stmtParamCount = SqlFileDescriber.FindMaxParamIndex(stmt);
            if (stmtParamCount > mergedMaxParam) mergedMaxParam = stmtParamCount;

            // @returns type_name | void — skip Describe entirely
            if (parseResult.ReturnsTypeOverrides.TryGetValue(stmtIndex, out var returnsTypeName))
            {
                // @returns void — skip Describe, no columns (void command)
                if (string.Equals(returnsTypeName, "void", StringComparison.OrdinalIgnoreCase))
                {
                    commandDescribes.Add(new DescribeResult { Columns = [] });
                    continue;
                }

                // Check if it's a known scalar type
                var scalarDescriptor = new TypeDescriptor(returnsTypeName);
                if (scalarDescriptor.DbType != NpgsqlTypes.NpgsqlDbType.Unknown)
                {
                    // @returns scalar_type — single column with the given type
                    commandDescribes.Add(new DescribeResult
                    {
                        Columns = [new ColumnInfo { Name = returnsTypeName, DataTypeName = returnsTypeName }]
                    });
                    continue;
                }

                // Check if it's a known composite type
                var compositeType = CompositeTypeCache.GetType(returnsTypeName);
                if (compositeType is not null)
                {
                    var returnsCols = new ColumnInfo[compositeType.FieldNames.Length];
                    for (int fi = 0; fi < compositeType.FieldNames.Length; fi++)
                    {
                        returnsCols[fi] = new ColumnInfo
                        {
                            Name = compositeType.FieldNames[fi],
                            DataTypeName = compositeType.FieldTypeNames[fi],
                        };
                    }
                    commandDescribes.Add(new DescribeResult { Columns = returnsCols });
                    continue;
                }

                var error = $"@returns type '{returnsTypeName}' is not a recognized PostgreSQL type or composite type.";
                NpgsqlRestOptions.Logger?.LogError("SqlFileSource: {FilePath}: {Error}", filePath, error);
                if (options.ErrorMode == ParseErrorMode.Exit)
                {
                    NpgsqlRestOptions.Logger?.LogCritical("SqlFileSource: Exiting due to SQL file error. Set ErrorMode to Skip to continue past errors.");
                    Environment.Exit(1);
                }
                return null;
            }

            var describeResult = SqlFileDescriber.Describe(connection, stmt, stmtParamCount, paramTypeHints);
            if (describeResult.HasError)
            {
                NpgsqlRestOptions.Logger?.LogError("SqlFileSource: {FilePath}:\n{Error}", filePath, describeResult.Error);
                if (options.ErrorMode == ParseErrorMode.Exit)
                {
                    NpgsqlRestOptions.Logger?.LogCritical("SqlFileSource: Exiting due to SQL file error. Set ErrorMode to Skip to continue past errors.");
                    Environment.Exit(1);
                }
                return null;
            }
            commandDescribes.Add(describeResult);

            // Merge parameter types — if same $N has different types across statements, error
            if (describeResult.ParameterTypes is not null)
            {
                for (int i = 0; i < describeResult.ParameterTypes.Length; i++)
                {
                    var pType = describeResult.ParameterTypes[i];
                    if (pType == "unknown") continue;
                    if (paramTypesByIndex.TryGetValue(i, out var existing))
                    {
                        if (existing != "unknown" && existing != pType)
                        {
                            var error = $"Parameter ${i + 1} has conflicting types across statements: '{existing}' vs '{pType}'. Use @param annotation to override.";
                            NpgsqlRestOptions.Logger?.LogError("SqlFileSource: {FilePath}: {Error}", filePath, error);
                            if (options.ErrorMode == ParseErrorMode.Exit)
                            {
                                NpgsqlRestOptions.Logger?.LogCritical("SqlFileSource: Exiting due to SQL file error. Set ErrorMode to Skip to continue past errors.");
                                Environment.Exit(1);
                            }
                            return null;
                        }
                    }
                    else
                    {
                        paramTypesByIndex[i] = pType;
                    }
                }
            }
        }

        // Build merged parameters (real SQL params + virtual params from @define_param)
        var virtualParams = parseResult.VirtualParams;
        var totalParamCount = mergedMaxParam + virtualParams.Count;
        var parameters = new NpgsqlRestParameter[totalParamCount];
        for (int i = 0; i < mergedMaxParam; i++)
        {
            var typeName = paramTypesByIndex.GetValueOrDefault(i, "unknown");

            string? customType = null;
            string? customTypeName = null;
            short? customTypePosition = null;
            string? originalParameterName = null;
            string convertedName;

            // HTTP custom type params: single composite parameter, no expansion
            if (httpTypeParams is not null && httpTypeParams.TryGetValue(i, out var httpParam))
            {
                customType = httpParam.TypeName;
                originalParameterName = $"${i + 1}";
                convertedName = httpParam.ParamName;
                typeName = "text"; // composite sent as text representation
            }
            // Non-HTTP composite type params: single composite parameter, no expansion
            else if (compositeTypeParams is not null && compositeTypeParams.TryGetValue(i, out var compositeParam))
            {
                originalParameterName = $"${i + 1}";
                convertedName = compositeParam.ParamName;
                typeName = "text"; // composite sent as text representation
            }
            else
            {
                convertedName = $"${i + 1}";
            }

            var typeDescriptor = new TypeDescriptor(
                typeName,
                hasDefault: customType is not null,
                customType: customType,
                customTypePosition: customTypePosition,
                originalParameterName: originalParameterName,
                customTypeName: customTypeName);
            var positionalName = $"${i + 1}";

            var param = new NpgsqlRestParameter(
                ordinal: i,
                convertedName: convertedName,
                actualName: positionalName,
                typeDescriptor: typeDescriptor);

            // HTTP type params need a default value so they're added to command.Parameters
            // (otherwise HasDefault=true + no DefaultValue causes the parameter to be skipped)
            if (httpTypeParams is not null && httpTypeParams.TryGetValue(i, out var httpParamForFields))
            {
                param.DefaultValue = DBNull.Value;
                param.CompositeFieldNames = httpParamForFields.FieldNames;
            }

            parameters[i] = param;
        }
        // Add virtual parameters — exist for HTTP matching and claim mapping, not bound to PostgreSQL
        for (int i = 0; i < virtualParams.Count; i++)
        {
            var vp = virtualParams[i];
            var vpType = vp.Type ?? "text";
            parameters[mergedMaxParam + i] = new NpgsqlRestParameter(
                ordinal: mergedMaxParam + i,
                convertedName: vp.Name,
                actualName: vp.Name,
                typeDescriptor: new TypeDescriptor(vpType))
            {
                IsVirtual = true
            };
        }

        // For single-command: use first describe's columns
        // For multi-command: use first non-void describe's columns as the Routine columns
        //   (individual command columns stored in MultiCommandInfo)
        var primaryDescribe = commandDescribes[0];
        var columns = primaryDescribe.Columns ?? [];
        var columnCount = columns.Length;
        var originalColumnNames = new string[columnCount];
        var columnNames = new string[columnCount];
        var columnTypeDescriptors = new TypeDescriptor[columnCount];
        Dictionary<int, (string[] FieldNames, TypeDescriptor[] FieldDescriptors)>? arrayCompositeColumnInfo = null;

        var usedColumnNames = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < columnCount; i++)
        {
            var colName = columns[i].Name;

            // Replace unnamed/duplicate columns with unique names (column1, column2, ...)
            if (colName == "?column?" || !usedColumnNames.Add(colName))
            {
                colName = string.Concat(UnnamedColumnPrefix, (i + 1).ToString());
            }

            originalColumnNames[i] = colName;
            columnNames[i] = nameConverter(colName) ?? colName;
            columnTypeDescriptors[i] = new TypeDescriptor(columns[i].DataTypeName);
            ResolveCompositeType(columnTypeDescriptors[i]);

            if (columnTypeDescriptors[i].IsArrayOfCompositeType &&
                columnTypeDescriptors[i].ArrayCompositeFieldNames is not null &&
                columnTypeDescriptors[i].ArrayCompositeFieldDescriptors is not null)
            {
                arrayCompositeColumnInfo ??= new();
                arrayCompositeColumnInfo[i] = (
                    columnTypeDescriptors[i].ArrayCompositeFieldNames!,
                    columnTypeDescriptors[i].ArrayCompositeFieldDescriptors!);
            }
        }

        var jsonColumnNames = new string[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            jsonColumnNames[i] = PgConverters.SerializeString(columnNames[i]);
        }

        // Build multi-command info if needed
        MultiCommandInfo[]? multiCommandInfo = null;
        if (isMultiCommand)
        {
            multiCommandInfo = new MultiCommandInfo[commandDescribes.Count];
            int resultCounter = 0; // Only incremented for non-skipped commands
            for (int ci = 0; ci < commandDescribes.Count; ci++)
            {
                var cmdCols = commandDescribes[ci].Columns ?? [];
                var cmdColNames = new string[cmdCols.Length];
                var cmdJsonColNames = new string[cmdCols.Length];
                var cmdColDescriptors = new TypeDescriptor[cmdCols.Length];
                for (int j = 0; j < cmdCols.Length; j++)
                {
                    cmdColNames[j] = nameConverter(cmdCols[j].Name) ?? cmdCols[j].Name;
                    cmdJsonColNames[j] = PgConverters.SerializeString(cmdColNames[j]);
                    cmdColDescriptors[j] = new TypeDescriptor(cmdCols[j].DataTypeName);
                    ResolveCompositeType(cmdColDescriptors[j]);
                }

                // Determine if this command should be skipped
                bool isSkipped = parseResult.SkipCommands.Contains(ci) ||
                    (options.SkipNonQueryCommands && IsNonQueryCommand(parseResult.Statements[ci]));

                // Result name: only assign meaningful names to non-skipped commands
                string resultName;
                if (isSkipped)
                {
                    resultName = "";
                }
                else
                {
                    resultCounter++;
                    if (parseResult.PositionalResultNames.TryGetValue(ci, out var positionalName))
                    {
                        resultName = positionalName;
                        NpgsqlRestOptions.Logger?.LogDebug(
                            "SqlFileSource: {FilePath} result{Index} renamed to \"{Name}\" by positional @result annotation",
                            filePath, resultCounter, positionalName);
                    }
                    else
                    {
                        resultName = string.Concat(options.ResultPrefix, resultCounter.ToString());
                    }
                }

                multiCommandInfo[ci] = new MultiCommandInfo
                {
                    Name = resultName,
                    JsonName = isSkipped ? "" : PgConverters.SerializeString(resultName),
                    Statement = parseResult.Statements[ci],
                    ParamCount = SqlFileDescriber.FindMaxParamIndex(parseResult.Statements[ci]),
                    ColumnCount = cmdCols.Length,
                    ColumnNames = cmdColNames,
                    JsonColumnNames = cmdJsonColNames,
                    ColumnTypeDescriptors = cmdColDescriptors,
                    ReturnsUnnamedSet = options.UnnamedSingleColumnSet && cmdCols.Length == 1
                        && !cmdColDescriptors[0].IsCompositeType,
                    IsSingle = parseResult.SingleCommands.Contains(ci),
                    IsSkipped = isSkipped,
                };
            }
        }

        // Multi-command is never void — always returns JSON object (with nulls for void commands)
        bool isVoid = !isMultiCommand && columnCount == 0;

        var fileName = Path.GetFileNameWithoutExtension(filePath);

        var routine = new Routine
        {
            Type = RoutineType.SqlFile,
            Schema = "public",
            Name = fileName,
            Comment = string.IsNullOrEmpty(parseResult.Comment) ? null : parseResult.Comment,
            IsStrict = false,
            CrudType = parseResult.AutoHttpMethod switch
            {
                Method.PUT => CrudType.Insert,
                Method.POST => CrudType.Update,
                Method.DELETE => CrudType.Delete,
                _ => CrudType.Select,
            },
            ReturnsRecordType = false,
            ReturnsSet = !isVoid,
            ColumnCount = columnCount,
            OriginalColumnNames = originalColumnNames,
            ColumnNames = columnNames,
            JsonColumnNames = jsonColumnNames,
            ColumnsTypeDescriptor = columnTypeDescriptors,
            ReturnsUnnamedSet = options.UnnamedSingleColumnSet && columnCount == 1 && !isMultiCommand
                && !columnTypeDescriptors[0].IsCompositeType,
            IsVoid = isVoid,
            ParamCount = totalParamCount,
            Parameters = parameters,
            ParamsHash = [.. parameters.Select(p => p.ConvertedName)],
            OriginalParamsHash = [.. parameters.Select(p => p.ActualName)],
            Expression = parseResult.Statements[0], // first statement for display/logging; batch uses individual statements
            FullDefinition = $"-- SQL file: {filePath}",
            SimpleDefinition = $"SQL file: {filePath}",
            FormatUrlPattern = null,
            Tags = null,
            EndpointHandler = null,
            Metadata = DeriveTsClientModule(filePath, baseDir),
        };

        if (arrayCompositeColumnInfo is not null)
        {
            routine.ArrayCompositeColumnInfo = arrayCompositeColumnInfo;
        }
        if (multiCommandInfo is not null)
        {
            routine.MultiCommandInfo = multiCommandInfo;
            routine.LogCommandText = options.LogCommandText;
        }

        return (routine, SqlFileParameterFormatter.Instance);
    }

    /// <summary>
    /// Resolve composite type metadata for a column TypeDescriptor.
    /// Delegates to CompositeTypeCache.ResolveTypeDescriptor which can set internal properties.
    /// </summary>
    private static void ResolveCompositeType(TypeDescriptor descriptor)
    {
        CompositeTypeCache.ResolveTypeDescriptor(descriptor);
    }

    /// <summary>
    /// Scan @param annotations in the comment for HTTP client type parameters.
    /// Returns null if no HTTP types found, otherwise a dictionary keyed by 0-based param index.
    /// </summary>
    private static Dictionary<int, (string TypeName, string ParamName, string[] FieldNames, string[] FieldTypes)>?
        DetectHttpTypeParams(string comment, NpgsqlConnection connection)
    {
        Dictionary<int, (string TypeName, string ParamName, string[] FieldNames, string[] FieldTypes)>? result = null;

        // Match @param $N name type patterns
        foreach (Match match in Regex.Matches(comment, @"@param\s+\$(\d+)\s+(\w+)\s+(\S+)", RegexOptions.IgnoreCase))
        {
            var paramIndex = int.Parse(match.Groups[1].Value) - 1; // 0-based
            var paramName = match.Groups[2].Value;
            var typeName = match.Groups[3].Value;

            // Check if this type is an HTTP client type (try with and without public. prefix)
            string? resolvedTypeName = null;
            if (HttpClientTypes.Definitions.ContainsKey(typeName))
            {
                resolvedTypeName = typeName;
            }
            else if (HttpClientTypes.Definitions.ContainsKey($"public.{typeName}"))
            {
                resolvedTypeName = $"public.{typeName}";
            }

            if (resolvedTypeName is null)
            {
                continue;
            }

            // Query composite type fields from pg_catalog
            var (fieldNames, fieldTypes) = QueryCompositeFields(connection, typeName);
            if (fieldNames.Length == 0)
            {
                continue;
            }

            result ??= new();
            result[paramIndex] = (resolvedTypeName, paramName, fieldNames, fieldTypes);
        }

        return result;
    }

    /// <summary>
    /// Query composite type field names and types from pg_catalog.
    /// </summary>
    private static (string[] FieldNames, string[] FieldTypes) QueryCompositeFields(NpgsqlConnection connection, string typeName)
    {
        using var cmd = connection.CreateCommand();
        // Handle schema-qualified names
        var parts = typeName.Split('.');
        var schema = parts.Length > 1 ? parts[0] : "public";
        var name = parts.Length > 1 ? parts[1] : parts[0];

        cmd.CommandText = @"
            select a.attname, format_type(a.atttypid, a.atttypmod)
            from pg_catalog.pg_attribute a
            join pg_catalog.pg_type t on a.attrelid = t.typrelid
            join pg_catalog.pg_namespace n on t.typnamespace = n.oid
            where t.typname = $1 and n.nspname = $2
            and a.attnum > 0 and not a.attisdropped
            order by a.attnum";
        cmd.Parameters.AddWithValue(name);
        cmd.Parameters.AddWithValue(schema);
        cmd.LogCommand(nameof(SqlFileSource));

        var fieldNames = new List<string>();
        var fieldTypes = new List<string>();

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            fieldNames.Add(reader.GetString(0));
            fieldTypes.Add(reader.GetString(1));
        }

        return (fieldNames.ToArray(), fieldTypes.ToArray());
    }


    /// <summary>
    /// Detect @param annotations with composite types that are NOT HTTP client types.
    /// These are client-sent composite parameters that need ROW() expansion.
    /// </summary>
    private static Dictionary<int, (string TypeName, string ParamName, string[] FieldNames, string[] FieldTypes)>?
        DetectCompositeTypeParams(
            string comment,
            NpgsqlConnection connection,
            Dictionary<int, (string TypeName, string ParamName, string[] FieldNames, string[] FieldTypes)>? httpTypeParams)
    {
        Dictionary<int, (string TypeName, string ParamName, string[] FieldNames, string[] FieldTypes)>? result = null;

        foreach (Match match in Regex.Matches(comment, @"@param\s+\$(\d+)\s+(\w+)\s+(\S+)", RegexOptions.IgnoreCase))
        {
            var paramIndex = int.Parse(match.Groups[1].Value) - 1; // 0-based
            var paramName = match.Groups[2].Value;
            var typeName = match.Groups[3].Value;

            // Skip if already detected as HTTP type
            if (httpTypeParams is not null && httpTypeParams.ContainsKey(paramIndex))
            {
                continue;
            }

            // Check if this type is a known composite type
            if (CompositeTypeCache.GetType(typeName) is null)
            {
                continue;
            }

            // Query composite type fields from pg_catalog
            var (fieldNames, fieldTypes) = QueryCompositeFields(connection, typeName);
            if (fieldNames.Length == 0)
            {
                continue;
            }

            result ??= new();
            result[paramIndex] = (typeName, paramName, fieldNames, fieldTypes);
        }

        return result;
    }

    /// <summary>
    /// Find files matching the glob pattern. Splits the pattern into a base directory
    /// and a file pattern, then lazily enumerates matching files.
    /// </summary>
    internal static IEnumerable<string> FindMatchingFiles(string filePattern)
    {
        // Find the base directory (everything before the first wildcard)
        int firstWildcard = filePattern.IndexOfAny(['*', '?']);
        if (firstWildcard < 0)
        {
            // No wildcards — treat as exact file path
            if (File.Exists(filePattern))
            {
                yield return Path.GetFullPath(filePattern);
            }
            yield break;
        }

        // Find the last / before the first wildcard to get the base directory
        int lastSlash = filePattern.LastIndexOf('/', firstWildcard);
        string baseDir;
        string pattern;

        if (lastSlash >= 0)
        {
            baseDir = filePattern[..lastSlash];
            pattern = filePattern;
        }
        else
        {
            baseDir = ".";
            pattern = filePattern;
        }

        if (!Directory.Exists(baseDir))
        {
            yield break;
        }

        // Lazily enumerate files and filter with IsPatternMatch
        bool isRecursive = filePattern.Contains("**");
        var searchOption = isRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        foreach (var file in Directory.EnumerateFiles(baseDir, "*", searchOption))
        {
            var normalizedFile = file.Replace('\\', '/');
            if (Parser.IsPatternMatch(normalizedFile, pattern))
            {
                yield return file;
            }
        }
    }

    /// <summary>
    /// Keywords for non-query commands that produce no meaningful result in multi-command responses.
    /// </summary>
    private static readonly HashSet<string> NonQueryKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "BEGIN", "COMMIT", "END", "ROLLBACK", "SAVEPOINT", "RELEASE",
        "SET", "RESET", "DISCARD", "LOCK", "LISTEN", "NOTIFY", "DEALLOCATE"
    };

    /// <summary>
    /// Check if a statement is a non-query command (transaction control, session, DO block, etc.)
    /// that should be skipped from multi-command results.
    /// </summary>
    private static bool IsNonQueryCommand(string statement)
    {
        var trimmed = statement.AsSpan().TrimStart();
        int end = 0;
        while (end < trimmed.Length && char.IsLetter(trimmed[end]))
            end++;
        if (end == 0) return false;
        var keyword = trimmed[..end].ToString();

        // DO blocks: must be followed by whitespace or $ (to avoid matching identifiers starting with DO)
        if (string.Equals(keyword, "DO", StringComparison.OrdinalIgnoreCase))
        {
            return end >= trimmed.Length || char.IsWhiteSpace(trimmed[end]) || trimmed[end] == '$';
        }

        return NonQueryKeywords.Contains(keyword);
    }

    /// <summary>
    /// Extract the base directory from a file pattern (everything before the first wildcard).
    /// Returns the full path of the base directory.
    /// </summary>
    private static string GetBaseDirectory(string filePattern)
    {
        int firstWildcard = filePattern.IndexOfAny(['*', '?']);
        if (firstWildcard < 0)
        {
            return Path.GetFullPath(Path.GetDirectoryName(filePattern) ?? ".");
        }
        int lastSlash = filePattern.LastIndexOf('/', firstWildcard);
        return Path.GetFullPath(lastSlash >= 0 ? filePattern[..lastSlash] : ".");
    }

    /// <summary>
    /// Check whether the parsed comment contains an HTTP tag (a line starting with "http").
    /// Mirrors the detection logic in DefaultCommentParser.
    /// </summary>
    private static bool HasHttpTag(string comment)
    {
        if (string.IsNullOrEmpty(comment))
        {
            return false;
        }
        foreach (var line in comment.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length >= 4 &&
                trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                (trimmed.Length == 4 || trimmed[4] == ' ' || trimmed[4] == '\t'))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Derive a module name from the file's position relative to the base scan directory.
    /// Stored in Routine.Metadata as the raw directory name.
    /// - sql/get-order.sql → "sql" (base dir name)
    /// - sql/orders/get-order.sql → "orders" (first subdirectory)
    /// - sql/my_orders/get-order.sql → "my_orders"
    /// </summary>
    private static string? DeriveTsClientModule(string filePath, string baseDir)
    {
        var fullPath = Path.GetFullPath(filePath);
        var fullBase = Path.GetFullPath(baseDir);

        var fileDir = Path.GetDirectoryName(fullPath) ?? fullBase;
        var relative = Path.GetRelativePath(fullBase, fileDir).Replace('\\', '/');

        if (relative == ".")
        {
            // File is directly in the base directory — use the base dir name
            return Path.GetFileName(fullBase) ?? "sql";
        }

        // Use the first subdirectory segment
        var firstSlash = relative.IndexOf('/');
        return firstSlash >= 0 ? relative[..firstSlash] : relative;
    }
}
