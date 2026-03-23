using Npgsql;

namespace NpgsqlRest.SqlFileSource;

/// <summary>
/// Routine source that scans SQL files matching a glob pattern and generates REST API endpoints.
/// Each SQL file must contain exactly one statement (multi-statement support planned for a future version).
/// </summary>
public class SqlFileSource(SqlFileSourceOptions options) : IEndpointSource
{
    private const string UnnamedColumnPrefix = "column";

    public CommentsMode? CommentsMode { get; set; } = options.CommentsMode; // Always has a value from options default

    public IEnumerable<(Routine, IRoutineSourceParameterFormatter)> Read(
        IServiceProvider? serviceProvider,
        RetryStrategy? retryStrategy)
    {
        if (string.IsNullOrEmpty(options.FilePattern))
        {
            yield break;
        }

        // Open a single connection for all Describe calls — same as RoutineSource and CrudSource
        NpgsqlConnection? connection = null;
        bool shouldDispose = true;
        try
        {
            NpgsqlRestOptions.Options.CreateAndOpenSourceConnection(serviceProvider, ref connection, ref shouldDispose);

            if (connection is null)
            {
                yield break;
            }

            var nameConverter = NpgsqlRestOptions.Options.NameConverter;

            // Initialize composite type cache for custom type column resolution
            CompositeTypeCache.Initialize(connection, nameConverter);

            foreach (var filePath in FindMatchingFiles(options.FilePattern))
            {
                (Routine, IRoutineSourceParameterFormatter)? result = null;
                try
                {
                    result = ProcessFile(filePath, connection, nameConverter, options);
                }
                catch (Exception ex)
                {
                    if (options.ErrorMode == ParseErrorMode.Throw)
                    {
                        throw;
                    }
                    NpgsqlRestOptions.Logger?.LogWarning("SqlFileSource: Error processing file {FilePath}: {Error}", filePath, ex.Message);
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
        NpgsqlConnection connection,
        Func<string?, string?> nameConverter,
        SqlFileSourceOptions options)
    {
        var content = File.ReadAllText(filePath);
        var parseResult = SqlFileParser.Parse(content, options.CommentScope);

        // Check for parse errors
        if (parseResult.Errors.Count > 0)
        {
            foreach (var error in parseResult.Errors)
            {
                if (options.ErrorMode == ParseErrorMode.Throw)
                {
                    throw new InvalidOperationException($"SqlFileSource: {filePath}: {error}");
                }
                NpgsqlRestOptions.Logger?.LogWarning("SqlFileSource: {FilePath}: {Error}", filePath, error);
            }
            return null;
        }

        if (parseResult.Statements.Count == 0)
        {
            return null;
        }

        bool isMultiCommand = parseResult.Statements.Count > 1;

        // Describe each statement individually and merge parameters
        int mergedMaxParam = 0;
        var paramTypesByIndex = new Dictionary<int, string>(); // $N index → type name
        var commandDescribes = new List<DescribeResult>();

        foreach (var stmt in parseResult.Statements)
        {
            int stmtParamCount = SqlFileDescriber.FindMaxParamIndex(stmt);
            if (stmtParamCount > mergedMaxParam) mergedMaxParam = stmtParamCount;

            var describeResult = SqlFileDescriber.Describe(connection, stmt, stmtParamCount);
            if (describeResult.HasError)
            {
                if (options.ErrorMode == ParseErrorMode.Throw)
                {
                    throw new InvalidOperationException($"SqlFileSource: {filePath}: Describe failed: {describeResult.Error}");
                }
                NpgsqlRestOptions.Logger?.LogWarning("SqlFileSource: {FilePath}: Describe failed: {Error}", filePath, describeResult.Error);
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
                            if (options.ErrorMode == ParseErrorMode.Throw)
                            {
                                throw new InvalidOperationException($"SqlFileSource: {filePath}: {error}");
                            }
                            NpgsqlRestOptions.Logger?.LogWarning("SqlFileSource: {FilePath}: {Error}", filePath, error);
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

        // Build merged parameters
        var parameters = new NpgsqlRestParameter[mergedMaxParam];
        for (int i = 0; i < mergedMaxParam; i++)
        {
            var typeName = paramTypesByIndex.GetValueOrDefault(i, "unknown");
            var typeDescriptor = new TypeDescriptor(typeName);
            var positionalName = $"${i + 1}";

            parameters[i] = new NpgsqlRestParameter(
                ordinal: i,
                convertedName: positionalName,
                actualName: positionalName,
                typeDescriptor: typeDescriptor);
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

        // Build multi-command info if needed
        MultiCommandInfo[]? multiCommandInfo = null;
        if (isMultiCommand)
        {
            multiCommandInfo = new MultiCommandInfo[commandDescribes.Count];
            for (int ci = 0; ci < commandDescribes.Count; ci++)
            {
                var cmdCols = commandDescribes[ci].Columns ?? [];
                var cmdColNames = new string[cmdCols.Length];
                var cmdColDescriptors = new TypeDescriptor[cmdCols.Length];
                for (int j = 0; j < cmdCols.Length; j++)
                {
                    cmdColNames[j] = nameConverter(cmdCols[j].Name) ?? cmdCols[j].Name;
                    cmdColDescriptors[j] = new TypeDescriptor(cmdCols[j].DataTypeName);
                    ResolveCompositeType(cmdColDescriptors[j]);
                }

                // Command name: annotation override or default pattern
                var annotatedName = (ci < parseResult.CommandNames.Count) ? parseResult.CommandNames[ci] : null;
                var commandName = annotatedName ?? string.Format(options.CommandNamePattern, ci + 1);

                multiCommandInfo[ci] = new MultiCommandInfo
                {
                    Name = commandName,
                    ColumnCount = cmdCols.Length,
                    ColumnNames = cmdColNames,
                    ColumnTypeDescriptors = cmdColDescriptors,
                };
            }
        }

        bool isVoid = columnCount == 0 && (multiCommandInfo?.All(c => c.ColumnCount == 0) ?? true);

        // Join all statements with ; for execution
        var fullSql = string.Join(";\n", parseResult.Statements);

        var fileName = Path.GetFileNameWithoutExtension(filePath);

        var routine = new Routine
        {
            Type = RoutineType.Other,
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
            ColumnsTypeDescriptor = columnTypeDescriptors,
            ReturnsUnnamedSet = false,
            IsVoid = isVoid,
            ParamCount = mergedMaxParam,
            Parameters = parameters,
            ParamsHash = [.. parameters.Select(p => p.ConvertedName)],
            OriginalParamsHash = [.. parameters.Select(p => p.ActualName)],
            Expression = fullSql,
            FullDefinition = $"-- SQL file: {filePath}",
            SimpleDefinition = $"SQL: {fileName}",
            FormatUrlPattern = null,
            Tags = null,
            EndpointHandler = null,
            Metadata = null,
        };

        if (arrayCompositeColumnInfo is not null)
        {
            routine.ArrayCompositeColumnInfo = arrayCompositeColumnInfo;
        }
        if (multiCommandInfo is not null)
        {
            routine.MultiCommandInfo = multiCommandInfo;
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
}
