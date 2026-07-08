using System.Text;
using System.Text.RegularExpressions;
using NpgsqlTypes;
using static NpgsqlRest.NpgsqlRestOptions;

namespace NpgsqlRest.DartClient;

public partial class DartClient(DartClientOptions options) : IEndpointCreateHandler
{
    private IApplicationBuilder _builder = default!;
    private NpgsqlRestOptions? _npgsqlRestoptions;

    private const string Enabled = "dartclient";
    private const string Module = "dartclient_module";
    // SQL-file directory grouping writes tsclient_module — reuse it so both generators group identically.
    private const string TsModuleFallback = "tsclient_module";
    private const string IncludeStatusCode = "dartclient_status_code";
    private const string SseEvents = "dartclient_events";
    private const string IncludeParseUrl = "dartclient_parse_url";
    private const string IncludeParseRequest = "dartclient_parse_request";
    private const string ExportUrl = "dartclient_export_url";
    private const string UrlOnly = "dartclient_url_only";

    public void Setup(IApplicationBuilder builder, NpgsqlRestOptions npgsqlRestoptions)
    {
        _builder = builder;
        _npgsqlRestoptions = npgsqlRestoptions;
    }

    private int _filesCreated;

    private static string? GetModule(RoutineEndpoint endpoint) =>
        endpoint.CustomParameters?.GetValueOrDefault(Module) ??
        endpoint.CustomParameters?.GetValueOrDefault(TsModuleFallback);

    public void Cleanup(RoutineEndpoint[] endpoints)
    {
        if (options.FilePath is null)
        {
            return;
        }
        _filesCreated = 0;

        var containsModuleParam = endpoints.Any(e => GetModule(e) is not null);
        if (!options.BySchema && containsModuleParam)
        {
            Run(endpoints, options.FilePath);
        }
        else
        {
            if (!options.FilePath.Contains("{0}"))
            {
                Logger?.LogError("DartClient Option FilePath doesn't contain {{0}} formatter and BySchema options is true. Some files may be overwritten! Existing...");
                return;
            }

            HashSet<string> processedModules = [];
            if (containsModuleParam)
            {
                foreach (var group in endpoints.GroupBy(GetModule))
                {
                    if (group.Key is null)
                    {
                        continue;
                    }
                    if (!processedModules.Contains(group.Key))
                    {
                        processedModules.Add(group.Key);
                    }
                    var filename = string.Format(options.FilePath, group.Key);
                    Run([.. group], filename);
                }
            }

            foreach (var group in endpoints.GroupBy(e => e.Routine.Schema))
            {
                var filename = string.Format(options.FilePath, ConvertToSnakeCase(group.Key));
                RoutineEndpoint[] groupArray = [.. group.Where(g =>
                    GetModule(g) is null ||
                    !processedModules.Contains(GetModule(g) ?? "")
                )];
                if (groupArray.Length == 0)
                {
                    continue;
                }
                Run([.. groupArray], filename);
            }
        }

        if (_filesCreated > 0)
        {
            Logger?.LogDebug("DartClient: Created {count} Dart file(s)", _filesCreated);
        }
    }

    private sealed record DartField(
        string DartName,
        string JsonKey,
        string DartType,
        string ReadExpr,
        string WriteExpr,
        bool OmitNullInJson);

    private void Run(RoutineEndpoint[] endpoints, string? fileName)
    {
        if (fileName is null)
        {
            return;
        }

        // Internal-only endpoints have no public HTTP route (404), so a generated client function for one
        // would be dead — e.g. a bare-`@mcp` MCP-only routine. Exclude them from the REST client.
        RoutineEndpoint[] filtered = [.. endpoints.Where(e => e.InternalOnly is false && e.CustomParameters.ParameterEnabled(Enabled) is not false)];

        Dictionary<string, string> modelsDict = [];
        Dictionary<string, int> names = [];
        // Track generated composite type model classes to avoid duplicates
        // Key: composite field signature, Value: generated class name
        Dictionary<string, string> compositeTypeModels = [];
        // Reserve the status/scaffold class names so a model class can never collide with them.
        HashSet<string> usedModelNames = [options.ErrorTypeName, options.ResultTypeName, "SseSubscription"];
        List<string> compositeModels = [];
        List<string> models = [];
        List<string> urlFunctions = [];
        List<string> sseFactories = [];
        List<string> functions = [];
        bool needsStatusTypes = false;
        bool needsQuery = false;
        bool needsHttp = false;
        bool needsSend = false;
        bool needsSendParse = false;
        bool needsSendMultipart = false;
        bool needsSse = false;
        bool needsConvert = false;

        bool handled = false;
        foreach (var endpoint in filtered
            .Where(e => e.Routine.Type == RoutineType.Table || e.Routine.Type == RoutineType.View)
            .OrderBy(e => e.Routine.Schema)
            .ThenBy(e => e.Routine.Type)
            .ThenBy(e => e.Routine.Name))
        {
            if (Handle(endpoint) && !handled)
            {
                handled = true;
            }
        }

        foreach (var endpoint in filtered
            .Where(e => !(e.Routine.Type == RoutineType.Table || e.Routine.Type == RoutineType.View))
            .OrderBy(e => e.Routine.Schema)
            .ThenBy(e => e.Routine.Name))
        {
            if (Handle(endpoint) && !handled)
            {
                handled = true;
            }
        }

        if (!handled)
        {
            if (filtered.Length == 0 && options.FileOverwrite)
            {
                if (File.Exists(fileName))
                {
                    try
                    {
                        File.Delete(fileName);
                        Logger?.LogTrace("Deleted file: {fileName}", fileName);
                    }
                    catch (Exception ex)
                    {
                        Logger?.LogError(ex, "Failed to delete file: {fileName}", fileName);

                        try
                        {
                            File.WriteAllText(fileName, "// No endpoints found.");
                        }
                        catch (Exception ex2)
                        {
                            Logger?.LogError(ex2, "Failed to empty file: {fileName}", fileName);
                        }
                    }
                }
            }
            return;
        }

        if (!options.FileOverwrite && File.Exists(fileName))
        {
            return;
        }

        var dir = Path.GetDirectoryName(fileName);
        if (dir is not null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        List<string> modelBlocks = [];
        if (needsStatusTypes)
        {
            modelBlocks.Add(GetStatusClasses());
        }
        modelBlocks.AddRange(compositeModels);
        modelBlocks.AddRange(models);

        var baseNoExt = Path.GetFileNameWithoutExtension(fileName);
        var separateModels = options.SeparateModelsFile && modelBlocks.Count > 0;

        if (needsSse)
        {
            // The SSE scaffold uses utf8.decoder and LineSplitter.
            needsConvert = true;
        }

        List<string> imports = [];
        if (needsSse)
        {
            imports.Add("import 'dart:async';");
        }
        if (needsConvert)
        {
            imports.Add("import 'dart:convert';");
        }
        if (needsSse)
        {
            imports.Add("import 'dart:math' as math;");
        }
        if (needsHttp)
        {
            imports.Add("import 'package:http/http.dart' as http;");
        }
        foreach (var import in options.CustomImports)
        {
            imports.Add(import);
        }
        if (options.ImportBaseUrlFrom is not null)
        {
            imports.Add($"import '{options.ImportBaseUrlFrom}';");
        }
        if (separateModels)
        {
            imports.Add($"import '{baseNoExt}_models.dart';");
            imports.Add($"export '{baseNoExt}_models.dart';");
        }

        List<string> blocks = [];
        var headerBlock = GetHeaderBlock();
        if (headerBlock is not null)
        {
            blocks.Add(headerBlock);
        }
        if (imports.Count > 0)
        {
            blocks.Add(string.Join(Environment.NewLine, imports));
        }
        if (options.ImportBaseUrlFrom is null)
        {
            blocks.Add($"String baseUrl = '{GetHost()}';");
        }
        if (needsHttp)
        {
            blocks.Add(ClientScaffold);
        }
        if (needsSend)
        {
            blocks.Add(needsSendParse ? SendScaffoldWithParse : SendScaffold);
        }
        if (needsSendMultipart)
        {
            blocks.Add(SendMultipartScaffold);
        }
        if (needsQuery)
        {
            blocks.Add(QueryScaffold);
        }
        if (needsSse)
        {
            blocks.Add(SseScaffold);
        }
        if (needsStatusTypes)
        {
            blocks.Add(GetErrorHelper());
        }
        blocks.AddRange(urlFunctions);
        blocks.AddRange(sseFactories);
        if (!separateModels)
        {
            blocks.AddRange(modelBlocks);
        }
        blocks.AddRange(functions);

        File.WriteAllText(fileName, string.Concat(string.Join(string.Concat(Environment.NewLine, Environment.NewLine), blocks), Environment.NewLine));
        _filesCreated++;
        Logger?.LogTrace("Created Dart file: {fileName}", fileName);

        if (separateModels)
        {
            var modelsFileName = Path.Combine(dir ?? "", string.Concat(baseNoExt, "_models.dart"));
            List<string> modelFileBlocks = [];
            if (headerBlock is not null)
            {
                modelFileBlocks.Add(headerBlock);
            }
            modelFileBlocks.AddRange(modelBlocks);
            File.WriteAllText(modelsFileName, string.Concat(string.Join(string.Concat(Environment.NewLine, Environment.NewLine), modelFileBlocks), Environment.NewLine));
            _filesCreated++;
            Logger?.LogTrace("Created Dart models file: {modelsFileName}", modelsFileName);
        }
        return;

        string? GetHeaderBlock()
        {
            if (options.HeaderLines.Count == 0)
            {
                return null;
            }
            var now = DateTime.Now.ToString("O");
            var text = string.Join(Environment.NewLine, options.HeaderLines.Select(l => string.Format(l, now).Trim())).Trim();
            return text.Length == 0 ? null : text;
        }

        bool Handle(RoutineEndpoint endpoint)
        {
            Routine routine = endpoint.Routine;

            var eventsStreamingEnabled = endpoint.SseEventsPath is not null;
            if (endpoint.CustomParameters.ParameterEnabled(SseEvents) is false)
            {
                eventsStreamingEnabled = false;
            }
            var includeParseUrlParam = endpoint.CustomParameters.ParameterEnabled(IncludeParseUrl) ?? options.IncludeParseUrlParam;
            var includeParseRequestParam = endpoint.CustomParameters.ParameterEnabled(IncludeParseRequest) ?? options.IncludeParseRequestParam;
            var includeStatusCode = endpoint.CustomParameters.ParameterEnabled(IncludeStatusCode) ?? options.IncludeStatusCode;
            var exportUrl = endpoint.CustomParameters.ParameterEnabled(ExportUrl) ?? options.ExportUrls;
            var urlOnly = endpoint.CustomParameters.ParameterEnabled(UrlOnly) is true;
            if (urlOnly)
            {
                exportUrl = true;
            }

            if (options.SkipRoutineNames.Contains(routine.Name))
            {
                return false;
            }
            if (options.SkipSchemas.Contains(routine.Schema))
            {
                return false;
            }
            if (options.SkipPaths.Contains(endpoint.Path))
            {
                return false;
            }

            string? name;
            try
            {
                if (options.UseRoutineNameInsteadOfEndpoint)
                {
                    name = options.IncludeSchemaInNames ? string.Concat(routine.Schema, "/", routine.Name) : routine.Name;
                }
                else
                {
                    string pathName;
                    if (string.IsNullOrEmpty(_npgsqlRestoptions?.UrlPathPrefix) || _npgsqlRestoptions.UrlPathPrefix.Length > endpoint.Path.Length)
                    {
                        pathName = endpoint.Path;
                    }
                    else
                    {
                        pathName = endpoint.Path[_npgsqlRestoptions.UrlPathPrefix.Length..];
                    }
                    name = options.IncludeSchemaInNames ? string.Concat(routine.Schema, "/", pathName) : pathName;
                }
            }
            catch
            {
                name = options.IncludeSchemaInNames ? string.Concat(routine.Schema, "/", routine.Name) : routine.Name;
            }
            if (name.Length < 3)
            {
                name = options.IncludeSchemaInNames ? string.Concat(routine.Schema, "/", routine.Name) : routine.Name;
            }

            var routineType = routine.Type;
            var paramCount = routine.ParamCount;
            var isVoid = routine.IsVoid;
            var returnsSet = routine.ReturnsSet;
            var columnCount = routine.ColumnCount;
            var returnsRecordType = routine.ReturnsRecordType;
            var columnsTypeDescriptor = routine.ColumnsTypeDescriptor;
            var returnsUnnamedSet = routine.ReturnsUnnamedSet;

            if (endpoint.Login)
            {
                isVoid = false;
                returnsSet = false;
                columnCount = 1;
                returnsRecordType = false;
                columnsTypeDescriptor = [new TypeDescriptor("text")];
            }

            if (endpoint.Logout)
            {
                isVoid = true;
            }

            if (routineType == RoutineType.Table || routineType == RoutineType.View)
            {
                name = string.Concat(name, "-", endpoint.Method.ToString().ToLowerInvariant());
            }

            if (names.TryGetValue(name, out var count))
            {
                names[name] = count + 1;
                name = string.Concat(name, "-", count);
            }
            else
            {
                names.Add(name, 1);
            }
            name = SanitizeDartName(name);
            var pascal = ConvertToPascalCase(name);
            var camel = EscapeDartIdentifier(ConvertToCamelCase(name));
            // A top-level function must not collide with the module-scaffold declarations.
            if (camel is "baseUrl" or "httpClient")
            {
                camel = string.Concat(camel, "_");
            }

            if (options.SkipFunctionNames.Contains(camel))
            {
                return false;
            }

            // Request fields
            string? requestName = null;
            List<(DartField Field, bool IsPathParam, bool IsBodyParam, bool HasDefault)> requestFields = [];
            Dictionary<string, string> paramFieldByName = new(StringComparer.OrdinalIgnoreCase);
            var seenFieldNames = new HashSet<string>(StringComparer.Ordinal);
            string? bodyParameterDartName = null;
            string? bodyParameterDartType = null;
            int requestParamCount = 0;
            for (var i = 0; i < paramCount; i++)
            {
                var parameter = routine.Parameters[i];
                var descriptor = parameter.TypeDescriptor;
                if (options.OmitAutomaticParameters && endpoint.OmitParameterFromGeneratedRequest(parameter))
                {
                    continue;
                }
                var dartName = EscapeDartIdentifier(ConvertToCamelCase(SanitizeDartName(parameter.ConvertedName)));
                // Skip duplicate parameter names (e.g., when multiple HTTP custom types share field names)
                if (!seenFieldNames.Add(dartName))
                {
                    continue;
                }
                requestParamCount++;
                paramFieldByName[parameter.ConvertedName] = dartName;
                paramFieldByName.TryAdd(parameter.ActualName, dartName);

                var hasDefault = descriptor.HasDefault || descriptor.CustomType is not null;
                var isPathParam = endpoint.PathParameters is not null &&
                    (endpoint.PathParameters.Contains(parameter.ConvertedName, StringComparer.OrdinalIgnoreCase) ||
                     endpoint.PathParameters.Contains(parameter.ActualName, StringComparer.OrdinalIgnoreCase));
                var isBodyParam = endpoint.IsBodyParameter(parameter);

                var dartType = GetDartType(descriptor);
                var field = new DartField(
                    dartName,
                    parameter.ConvertedName,
                    NullableType(dartType),
                    GetReadExpr(descriptor, parameter.ConvertedName),
                    GetWriteExpr(descriptor, dartName),
                    hasDefault);
                requestFields.Add((field, isPathParam, isBodyParam, hasDefault));

                if (isBodyParam)
                {
                    bodyParameterDartName = dartName;
                    bodyParameterDartType = dartType;
                }
            }
            if (requestParamCount > 0)
            {
                requestName = EscapeDartClassName(string.Concat(options.ModelPrefix, pascal, "Request", options.ModelSuffix));
                requestName = AddModel(requestName, [.. requestFields.Select(f => f.Field)]);
            }

            // Response
            string responseName = "void";
            bool json = false;
            bool responseIsRaw = false;
            string[]? payloadLines = null;
            string JsonDecodeExpr = "jsonDecode(utf8.decode(response.bodyBytes))";

            // proxy_out: always returns the raw upstream response (function runs first, then proxies
            // to upstream). Void proxy pass-through likewise. There is no typed Dart shape for those,
            // so the generated function returns http.Response directly. Transform proxies (non-void
            // @proxy routines) return processed data and fall through to normal handling.
            if ((endpoint.IsProxyOut || (endpoint.IsProxy && routine.IsVoid)) && !urlOnly)
            {
                responseIsRaw = true;
                includeStatusCode = false;
                responseName = "http.Response";
                payloadLines = ["response"];
            }
            else if (routine.IsMultiCommand && routine.MultiCommandInfo is not null && !urlOnly)
            {
                // Multi-command SQL file: generate response class with one field per command result
                List<DartField> mcFields = [];
                foreach (var cmdInfo in routine.MultiCommandInfo)
                {
                    if (cmdInfo.IsSkipped)
                    {
                        continue;
                    }
                    var fieldName = EscapeDartIdentifier(ConvertToCamelCase(SanitizeDartName(cmdInfo.Name)));
                    if (cmdInfo.ColumnCount == 0)
                    {
                        // Void command → rows affected count
                        mcFields.Add(new DartField(fieldName, cmdInfo.Name, "int?",
                            $"(json['{cmdInfo.Name}'] as num?)?.toInt()", fieldName, false));
                    }
                    else if (cmdInfo.ColumnCount == 1 && cmdInfo.ReturnsUnnamedSet)
                    {
                        // Single column with UnnamedSingleColumnSet — flat list or scalar with @single
                        var elem = GetElementInfo(cmdInfo.ColumnTypeDescriptors[0]);
                        if (cmdInfo.IsSingle)
                        {
                            mcFields.Add(new DartField(fieldName, cmdInfo.Name, NullableType(elem.Type),
                                GetReadExpr(cmdInfo.ColumnTypeDescriptors[0], cmdInfo.Name, forceScalar: true),
                                fieldName, false));
                        }
                        else
                        {
                            mcFields.Add(new DartField(fieldName, cmdInfo.Name, NullableType($"List<{elem.Type}>"),
                                elem.Conversion is null
                                    ? $"json['{cmdInfo.Name}'] as List?"
                                    : $"(json['{cmdInfo.Name}'] as List?)?.map((e) => {elem.Conversion}).toList()",
                                fieldName, false));
                        }
                    }
                    else
                    {
                        // Named columns — nested class, list or single object with @single
                        List<DartField> cmdFields = [];
                        for (int ci = 0; ci < cmdInfo.ColumnCount; ci++)
                        {
                            var colName = cmdInfo.ColumnNames[ci];
                            var colDartName = EscapeDartIdentifier(ConvertToCamelCase(SanitizeDartName(colName)));
                            var colDescriptor = cmdInfo.ColumnTypeDescriptors[ci];
                            cmdFields.Add(new DartField(colDartName, colName,
                                NullableType(GetDartType(colDescriptor)),
                                GetReadExpr(colDescriptor, colName),
                                GetWriteExpr(colDescriptor, colDartName), false));
                        }
                        var cmdClassName = EscapeDartClassName(string.Concat(
                            options.ModelPrefix, pascal, ConvertToPascalCase(SanitizeDartName(cmdInfo.Name)), "Result", options.ModelSuffix));
                        cmdClassName = AddModel(cmdClassName, cmdFields);
                        if (cmdInfo.IsSingle)
                        {
                            mcFields.Add(new DartField(fieldName, cmdInfo.Name, string.Concat(cmdClassName, "?"),
                                $"json['{cmdInfo.Name}'] == null ? null : {cmdClassName}.fromJson(json['{cmdInfo.Name}'] as Map<String, dynamic>)",
                                $"{fieldName}?.toJson()", false));
                        }
                        else
                        {
                            mcFields.Add(new DartField(fieldName, cmdInfo.Name, $"List<{cmdClassName}>?",
                                $"(json['{cmdInfo.Name}'] as List?)?.map((e) => {cmdClassName}.fromJson(e as Map<String, dynamic>)).toList()",
                                $"{fieldName}?.map((e) => e.toJson()).toList()", false));
                        }
                    }
                }
                responseName = EscapeDartClassName(string.Concat(options.ModelPrefix, pascal, "Response", options.ModelSuffix));
                responseName = AddModel(responseName, mcFields);
                json = true;
                payloadLines = [$"{responseName}.fromJson({JsonDecodeExpr} as Map<String, dynamic>)"];
            }
            else if (!isVoid && !urlOnly)
            {
                if (endpoint.Upload)
                {
                    var uploadName = EscapeDartClassName(string.Concat(options.ModelPrefix, pascal, "Response", options.ModelSuffix));
                    if (!usedModelNames.Contains(uploadName))
                    {
                        usedModelNames.Add(uploadName);
                        models.Add(RenderUploadModel(uploadName));
                    }
                    responseName = $"List<{uploadName}>";
                    // Note: json flag stays false because upload endpoints use multipart form data,
                    // and the Content-Type header is set by the multipart request itself.
                    payloadLines =
                    [
                        $"({JsonDecodeExpr} as List)",
                        $".map((e) => {uploadName}.fromJson(e as Map<String, dynamic>))",
                        ".toList()",
                    ];
                }
                else if (returnsSet == false && columnCount == 1 && !returnsRecordType)
                {
                    var descriptor = columnsTypeDescriptor[0];
                    if (descriptor.IsArray)
                    {
                        json = true;
                        var elem = GetElementInfo(descriptor);
                        responseName = $"List<{elem.Type}>";
                        payloadLines = elem.Conversion is null
                            ? [$"{JsonDecodeExpr} as List"]
                            :
                            [
                                $"({JsonDecodeExpr} as List)",
                                $".map((e) => {elem.Conversion})",
                                ".toList()",
                            ];
                    }
                    else
                    {
                        if ((descriptor.IsDate || descriptor.IsDateTime) && options.UseDateTimeType)
                        {
                            responseName = "DateTime";
                            payloadLines = ["DateTime.parse(utf8.decode(response.bodyBytes))"];
                        }
                        else if (descriptor.IsNumeric)
                        {
                            responseName = IsIntegerFamily(descriptor) ? "int" : "double";
                            payloadLines = [$"{responseName}.parse(utf8.decode(response.bodyBytes))"];
                        }
                        else if (descriptor.IsBoolean)
                        {
                            responseName = "bool";
                            payloadLines = ["utf8.decode(response.bodyBytes) == 't'"];
                        }
                        else if (descriptor.IsJson)
                        {
                            responseName = options.DefaultJsonType;
                            payloadLines = [JsonDecodeExpr];
                        }
                        else
                        {
                            responseName = "String";
                            payloadLines = ["utf8.decode(response.bodyBytes)"];
                        }
                    }
                }
                else
                {
                    json = true;
                    if (returnsUnnamedSet)
                    {
                        var descriptor = columnCount > 0 ? columnsTypeDescriptor[0] : new TypeDescriptor("text");
                        var elem = GetElementInfo(descriptor);
                        if (returnsSet && !endpoint.ReturnSingleRecord)
                        {
                            responseName = $"List<{elem.Type}>";
                            payloadLines = elem.Conversion is null
                                ? [$"{JsonDecodeExpr} as List"]
                                :
                                [
                                    $"({JsonDecodeExpr} as List)",
                                    $".map((e) => {elem.Conversion})",
                                    ".toList()",
                                ];
                        }
                        else
                        {
                            responseName = elem.Type;
                            payloadLines = [GetScalarJsonExpr(descriptor, JsonDecodeExpr)];
                        }
                    }
                    else
                    {
                        List<DartField> responseFields = [];

                        // Check if nested JSON for composite types is enabled
                        // When false (default), composite fields are flattened in the JSON response
                        // When true, composite fields are nested under their column name
                        var useNestedCompositeTypes = endpoint.NestedJsonForCompositeTypes == true;

                        // Collect column indices to skip (expanded composite columns) - only when using nested types
                        HashSet<int> skipIndices = [];
                        if (useNestedCompositeTypes && routine.CompositeColumnInfo is not null)
                        {
                            foreach (var kvp in routine.CompositeColumnInfo)
                            {
                                // Skip all expanded column indices except the first one (which becomes the composite property)
                                foreach (var idx in kvp.Value.ExpandedColumnIndices.Skip(1))
                                {
                                    skipIndices.Add(idx);
                                }
                            }
                        }

                        for (var i = 0; i < columnCount; i++)
                        {
                            // Skip expanded composite columns (only when nested types are enabled)
                            if (skipIndices.Contains(i))
                            {
                                continue;
                            }

                            // Nested composite column - only generate a nested class when NestedJsonForCompositeTypes is true
                            if (useNestedCompositeTypes &&
                                routine.CompositeColumnInfo is not null &&
                                routine.CompositeColumnInfo.TryGetValue(i, out var compositeInfo))
                            {
                                var compositeClassName = GetOrCreateCompositeModel(
                                    compositeInfo.FieldNames,
                                    compositeInfo.FieldDescriptors,
                                    compositeInfo.ConvertedColumnName,
                                    compositeTypeModels,
                                    compositeModels);
                                responseFields.Add(CompositeField(compositeInfo.ConvertedColumnName, compositeClassName));
                                continue;
                            }

                            // Array of composite types
                            if (routine.ArrayCompositeColumnInfo is not null &&
                                routine.ArrayCompositeColumnInfo.TryGetValue(i, out var arrayCompositeInfo))
                            {
                                var compositeClassName = GetOrCreateCompositeModel(
                                    arrayCompositeInfo.FieldNames,
                                    arrayCompositeInfo.FieldDescriptors,
                                    routine.ColumnNames[i],
                                    compositeTypeModels,
                                    compositeModels);
                                responseFields.Add(CompositeArrayField(routine.ColumnNames[i], compositeClassName));
                                continue;
                            }

                            var descriptor = columnsTypeDescriptor[i];

                            // SQL file composite type column: expand fields to match actual JSON response
                            if (descriptor.IsCompositeType &&
                                descriptor.CompositeFieldNames is not null &&
                                descriptor.CompositeFieldDescriptors is not null)
                            {
                                if (useNestedCompositeTypes)
                                {
                                    // Nested mode: generate nested class under column name
                                    var compositeClassName = GetOrCreateCompositeModel(
                                        descriptor.CompositeFieldNames,
                                        descriptor.CompositeFieldDescriptors,
                                        routine.ColumnNames[i],
                                        compositeTypeModels,
                                        compositeModels);
                                    responseFields.Add(CompositeField(routine.ColumnNames[i], compositeClassName));
                                }
                                else
                                {
                                    // Flat mode: inline each composite field as a separate property
                                    for (var fi = 0; fi < descriptor.CompositeFieldNames.Length; fi++)
                                    {
                                        var fieldName = ConvertToCamelCase(descriptor.CompositeFieldNames[fi]);
                                        var fieldDescriptor = descriptor.CompositeFieldDescriptors[fi];

                                        // Handle nested composite fields
                                        if (fieldDescriptor.CompositeFieldNames is not null &&
                                            fieldDescriptor.CompositeFieldDescriptors is not null)
                                        {
                                            var nestedClassName = GetOrCreateCompositeModel(
                                                fieldDescriptor.CompositeFieldNames,
                                                fieldDescriptor.CompositeFieldDescriptors,
                                                fieldName,
                                                compositeTypeModels,
                                                compositeModels);
                                            responseFields.Add(CompositeField(fieldName, nestedClassName));
                                        }
                                        else if (fieldDescriptor.ArrayCompositeFieldNames is not null &&
                                                 fieldDescriptor.ArrayCompositeFieldDescriptors is not null)
                                        {
                                            var nestedClassName = GetOrCreateCompositeModel(
                                                fieldDescriptor.ArrayCompositeFieldNames,
                                                fieldDescriptor.ArrayCompositeFieldDescriptors,
                                                fieldName,
                                                compositeTypeModels,
                                                compositeModels);
                                            responseFields.Add(CompositeArrayField(fieldName, nestedClassName));
                                        }
                                        else
                                        {
                                            responseFields.Add(PlainField(fieldName, fieldDescriptor));
                                        }
                                    }
                                }
                                continue;
                            }

                            responseFields.Add(PlainField(routine.ColumnNames[i], descriptor));
                        }

                        responseName = EscapeDartClassName(string.Concat(options.ModelPrefix, pascal, "Response", options.ModelSuffix));
                        responseName = AddModel(responseName, responseFields);

                        if (returnsSet && !endpoint.ReturnSingleRecord)
                        {
                            payloadLines =
                            [
                                $"({JsonDecodeExpr} as List)",
                                $".map((e) => {responseName}.fromJson(e as Map<String, dynamic>))",
                                ".toList()",
                            ];
                            responseName = $"List<{responseName}>";
                        }
                        else
                        {
                            payloadLines = [$"{responseName}.fromJson({JsonDecodeExpr} as Map<String, dynamic>)"];
                        }
                    }
                }
            }

            if (includeStatusCode)
            {
                needsStatusTypes = true;
            }

            // Headers
            Dictionary<string, string> headersDict = [];
            if (json)
            {
                headersDict.Add("Content-Type", "'application/json'");
            }
            if (eventsStreamingEnabled && _npgsqlRestoptions?.ExecutionIdHeaderName is not null)
            {
                // Value is the Dart variable holding the execution id, not a string literal.
                headersDict.Add(_npgsqlRestoptions.ExecutionIdHeaderName, "executionId");
            }
            if (options.CustomHeaders.Count > 0)
            {
                foreach (var header in options.CustomHeaders)
                {
                    if (string.IsNullOrEmpty(header.Value))
                    {
                        headersDict.Remove(header.Key);
                    }
                    else
                    {
                        headersDict[header.Key] = header.Value;
                    }
                }
            }

            // Body
            string? bodyArg = null;
            if (endpoint.RequestParamType == RequestParamType.BodyJson && requestName is not null)
            {
                bodyArg = "jsonEncode(request.toJson())";
            }
            // Emit the request body for a designated body parameter only when the method can carry one.
            // A GET endpoint with @body_parameter_name still excludes that parameter from the query
            // string but does not send it as a body — e.g. when it is server-filled (an HTTP Custom
            // Type field) and forwarded by a proxy POST upstream.
            else if (bodyParameterDartName is not null && endpoint.Method != Method.GET)
            {
                bodyArg = bodyParameterDartType is "String" or "dynamic"
                    ? $"request.{bodyParameterDartName}"
                    : $"request.{bodyParameterDartName}?.toString()";
            }

            // Query string
            var hasPathParams = endpoint.HasPathParameters;
            var pathParamCount = endpoint.PathParameters?.Length ?? 0;
            var bodyParamCount = bodyParameterDartName is not null ? 1 : 0;
            // requestParamCount already excludes omitted (server-filled) parameters; path parameters are
            // never omitted, so subtracting them and the body parameter yields the query parameter count.
            var queryParamCount = requestParamCount - pathParamCount - bodyParamCount;

            List<string>? queryEntries = null;
            if (endpoint.RequestParamType == RequestParamType.QueryString && requestName is not null && queryParamCount > 0)
            {
                queryEntries = [];
                foreach (var (field, isPathParam, isBodyParam, hasDefault) in requestFields)
                {
                    if (isPathParam || isBodyParam)
                    {
                        continue;
                    }
                    queryEntries.Add(hasDefault
                        ? $"if (request.{field.DartName} != null) '{field.JsonKey}': request.{field.DartName},"
                        : $"'{field.JsonKey}': request.{field.DartName},");
                }
                needsQuery = true;
            }

            // URL
            var pathExpr = hasPathParams
                ? string.Concat("'$baseUrl", ConvertPathToInterpolation(endpoint.Path, paramFieldByName, routine), "'")
                : string.Concat("'$baseUrl", endpoint.Path, "'");

            string uriStatement;
            // The parseUrl hook reassigns the local, so it cannot be final.
            var uriDecl = includeParseUrlParam ? "var" : "final";
            var urlFuncName = string.Concat(camel, "Url");
            var urlFuncTakesRequest = requestName is not null && (hasPathParams || queryEntries is not null);
            if (exportUrl)
            {
                if (queryEntries is not null)
                {
                    StringBuilder uf = new();
                    uf.AppendLine($"String {urlFuncName}({(urlFuncTakesRequest ? $"{requestName} request" : "")}) {{");
                    uf.AppendLine($"  return {pathExpr} + _query({{");
                    foreach (var entry in queryEntries)
                    {
                        uf.AppendLine($"    {entry}");
                    }
                    uf.AppendLine("  });");
                    uf.Append('}');
                    urlFunctions.Add(uf.ToString());
                }
                else
                {
                    urlFunctions.Add($"String {urlFuncName}({(urlFuncTakesRequest ? $"{requestName} request" : "")}) => {pathExpr};");
                }
                uriStatement = $"  {uriDecl} uri = Uri.parse({urlFuncName}({(urlFuncTakesRequest ? "request" : "")}));";
            }
            else
            {
                if (queryEntries is not null)
                {
                    StringBuilder us = new();
                    us.AppendLine($"  {uriDecl} uri = Uri.parse({pathExpr} + _query({{");
                    foreach (var entry in queryEntries)
                    {
                        us.AppendLine($"    {entry}");
                    }
                    us.Append("  }));");
                    uriStatement = us.ToString();
                }
                else
                {
                    uriStatement = $"  {uriDecl} uri = Uri.parse({pathExpr});";
                }
            }
            if (urlOnly)
            {
                return true;
            }

            needsHttp = true;

            // SSE event source factory
            string? eventSourceFunc = null;
            if (eventsStreamingEnabled)
            {
                needsSse = true;
                eventSourceFunc = string.Concat(options.ExportEventSources ? "create" : "_create", pascal, "EventSource");
                StringBuilder ef = new();
                ef.AppendLine($"Future<SseSubscription> {eventSourceFunc}(");
                ef.AppendLine("  void Function(String message) onMessage, {");
                ef.AppendLine("  String id = '',");
                ef.AppendLine("}) {");
                ef.AppendLine(string.Concat("  return _sse(Uri.parse('$baseUrl", endpoint.SseEventsPath, "?$id'), onMessage);"));
                ef.Append('}');
                sseFactories.Add(ef.ToString());
            }

            // Result type
            string resultType;
            if (includeStatusCode)
            {
                resultType = $"{options.ResultTypeName}<{responseName}>";
            }
            else
            {
                resultType = responseName;
            }

            if ((payloadLines is not null && !responseIsRaw) || bodyArg == "jsonEncode(request.toJson())" || includeStatusCode)
            {
                needsConvert = true;
            }

            // Optional named parameters and doc comment
            List<string> namedParams = [];
            List<(string name, string desc)> paramComments = [];
            if (endpoint.Upload)
            {
                paramComments.Add(("files", "Multipart files to upload, sent as form field \"file\"."));
            }
            if (requestName is not null)
            {
                paramComments.Add(("request", "Carries the endpoint parameters."));
            }
            if (endpoint.Upload)
            {
                namedParams.Add("void Function(int loaded, int total)? progress");
                paramComments.Add(("progress", "Optional callback reporting upload progress in bytes."));
            }
            if (eventsStreamingEnabled)
            {
                namedParams.Add("void Function(String message)? onMessage");
                paramComments.Add(("onMessage", "Optional callback function to handle incoming SSE messages."));
                namedParams.Add("String? id");
                paramComments.Add(("id", "Optional execution ID for the SSE connection. When supplied, only event streams opened with this ID in the query string will receive events."));
                namedParams.Add("int closeAfterMs = 1000");
                paramComments.Add(("closeAfterMs", "Time in milliseconds to wait before closing the SSE connection. Used only when onMessage callback is provided."));
                namedParams.Add("int awaitConnectionMs = 0");
                paramComments.Add(("awaitConnectionMs", "Time in milliseconds to wait after opening the SSE connection before sending the request. Used only when onMessage callback is provided."));
            }
            if (endpoint.Upload && options.XsrfTokenHeaderName is not null)
            {
                namedParams.Add("String? xsrfToken");
                paramComments.Add(("xsrfToken", $"Optional value for the \"{options.XsrfTokenHeaderName}\" request header."));
            }
            if (includeParseUrlParam)
            {
                namedParams.Add("Uri Function(Uri uri)? parseUrl");
                paramComments.Add(("parseUrl", "Optional function to rewrite the constructed URI before making the request."));
            }
            if (includeParseRequestParam)
            {
                namedParams.Add(endpoint.Upload
                    ? "http.MultipartRequest Function(http.MultipartRequest multipart)? parseRequest"
                    : "http.Request Function(http.Request request)? parseRequest");
                paramComments.Add(("parseRequest", "Optional function to rewrite the constructed request before it is sent."));
                if (!endpoint.Upload)
                {
                    needsSendParse = true;
                }
            }
            var docComment = GetComment(routine, resultType, paramComments);

            StringBuilder fn = new();
            fn.AppendLine(docComment);

            if (endpoint.Upload)
            {
                // Multipart upload function
                needsSendMultipart = true;
                needsConvert = true;
                fn.AppendLine($"Future<{resultType}> {camel}(");
                fn.Append("  List<http.MultipartFile> files");
                if (requestName is not null)
                {
                    fn.AppendLine(",");
                    fn.Append($"  {requestName} request");
                }
                fn.AppendLine(", {");
                foreach (var namedParam in namedParams)
                {
                    fn.AppendLine($"  {namedParam},");
                }
                fn.AppendLine("}) async {");
                fn.AppendLine(uriStatement);
                if (includeParseUrlParam)
                {
                    fn.AppendLine("  if (parseUrl != null) {");
                    fn.AppendLine("    uri = parseUrl(uri);");
                    fn.AppendLine("  }");
                }
                if (eventsStreamingEnabled)
                {
                    fn.AppendLine("  final executionId = id ?? _randomId();");
                }
                fn.AppendLine($"  {(includeParseRequestParam ? "var" : "final")} multipart = http.MultipartRequest('POST', uri);");
                fn.AppendLine("  multipart.files.addAll(files);");
                foreach (var header in headersDict)
                {
                    fn.AppendLine($"  multipart.headers[{DartQuote(header.Key)}] = {header.Value};");
                }
                if (options.XsrfTokenHeaderName is not null)
                {
                    fn.AppendLine("  if (xsrfToken != null) {");
                    fn.AppendLine($"    multipart.headers[{DartQuote(options.XsrfTokenHeaderName)}] = xsrfToken;");
                    fn.AppendLine("  }");
                }
                if (includeParseRequestParam)
                {
                    fn.AppendLine("  if (parseRequest != null) {");
                    fn.AppendLine("    multipart = parseRequest(multipart);");
                    fn.AppendLine("  }");
                }

                StringBuilder core = new();
                core.AppendLine("  final response = await _sendMultipart(multipart, progress: progress);");
                if (includeStatusCode)
                {
                    core.Append(BuildStatusReturnBlock(payloadLines, responseName));
                }
                else
                {
                    core.AppendLine("  if (response.statusCode < 200 || response.statusCode >= 300) {");
                    core.AppendLine("    throw http.ClientException(utf8.decode(response.bodyBytes), uri);");
                    core.Append("  }");
                    if (payloadLines is not null)
                    {
                        core.AppendLine();
                        core.Append(BuildPlainReturnBlock(payloadLines));
                    }
                }
                AppendFunctionCore(fn, core.ToString(), eventsStreamingEnabled, eventSourceFunc);
                fn.Append('}');
            }
            else
            {
                needsSend = true;
                if (namedParams.Count == 0)
                {
                    fn.AppendLine($"Future<{resultType}> {camel}({(requestName is not null ? $"{requestName} request" : "")}) async {{");
                }
                else
                {
                    if (requestName is not null)
                    {
                        fn.AppendLine($"Future<{resultType}> {camel}(");
                        fn.AppendLine($"  {requestName} request, {{");
                    }
                    else
                    {
                        fn.AppendLine($"Future<{resultType}> {camel}({{");
                    }
                    foreach (var namedParam in namedParams)
                    {
                        fn.AppendLine($"  {namedParam},");
                    }
                    fn.AppendLine("}) async {");
                }
                fn.AppendLine(uriStatement);
                if (includeParseUrlParam)
                {
                    fn.AppendLine("  if (parseUrl != null) {");
                    fn.AppendLine("    uri = parseUrl(uri);");
                    fn.AppendLine("  }");
                }
                if (eventsStreamingEnabled)
                {
                    fn.AppendLine("  final executionId = id ?? _randomId();");
                }

                StringBuilder core = new();
                var methodStr = endpoint.Method.ToString().ToUpperInvariant();
                var hasResponseVar = payloadLines is not null || includeStatusCode;
                var responseVar = hasResponseVar ? "final response = " : "";
                if (headersDict.Count == 0 && bodyArg is null && !includeParseRequestParam)
                {
                    core.Append($"  {responseVar}await _send('{methodStr}', uri);");
                }
                else
                {
                    core.AppendLine($"  {responseVar}await _send(");
                    core.AppendLine($"    '{methodStr}',");
                    core.Append("    uri");
                    if (headersDict.Count > 0)
                    {
                        core.AppendLine(",");
                        core.AppendLine("    headers: {");
                        foreach (var header in headersDict)
                        {
                            core.AppendLine($"      {DartQuote(header.Key)}: {header.Value},");
                        }
                        core.Append("    }");
                    }
                    if (bodyArg is not null)
                    {
                        core.AppendLine(",");
                        core.Append($"    body: {bodyArg}");
                    }
                    if (includeParseRequestParam)
                    {
                        core.AppendLine(",");
                        core.Append("    parseRequest: parseRequest");
                    }
                    core.AppendLine(",");
                    core.Append("  );");
                }
                if (includeStatusCode)
                {
                    core.AppendLine();
                    core.Append(BuildStatusReturnBlock(payloadLines, responseName));
                }
                else if (payloadLines is not null)
                {
                    core.AppendLine();
                    core.Append(BuildPlainReturnBlock(payloadLines));
                }
                AppendFunctionCore(fn, core.ToString(), eventsStreamingEnabled, eventSourceFunc);
                fn.Append('}');
            }
            functions.Add(fn.ToString());
            return true;
        } // bool Handle

        string AddModel(string modelName, List<DartField> fields)
        {
            var signature = string.Join(";", fields.Select(f => $"{f.DartName}{(f.OmitNullInJson ? "?" : "")}:{f.DartType}"));
            if (modelsDict.TryGetValue(signature, out var existingName))
            {
                return existingName;
            }
            var baseName = modelName;
            var counter = 1;
            while (!usedModelNames.Add(modelName))
            {
                modelName = $"{baseName}{counter++}";
            }
            if (options.UniqueModels)
            {
                modelsDict.Add(signature, modelName);
            }
            models.Add(RenderModel(modelName, fields));
            return modelName;
        }

        DartField PlainField(string columnName, TypeDescriptor descriptor)
        {
            var dartName = EscapeDartIdentifier(ConvertToCamelCase(SanitizeDartName(columnName)));
            return new DartField(
                dartName,
                columnName,
                NullableType(GetDartType(descriptor)),
                GetReadExpr(descriptor, columnName),
                GetWriteExpr(descriptor, dartName),
                false);
        }

        DartField CompositeField(string columnName, string className)
        {
            var dartName = EscapeDartIdentifier(ConvertToCamelCase(SanitizeDartName(columnName)));
            return new DartField(
                dartName,
                columnName,
                string.Concat(className, "?"),
                $"json['{columnName}'] == null ? null : {className}.fromJson(json['{columnName}'] as Map<String, dynamic>)",
                $"{dartName}?.toJson()",
                false);
        }

        DartField CompositeArrayField(string columnName, string className)
        {
            var dartName = EscapeDartIdentifier(ConvertToCamelCase(SanitizeDartName(columnName)));
            return new DartField(
                dartName,
                columnName,
                $"List<{className}>?",
                $"(json['{columnName}'] as List?)?.map((e) => {className}.fromJson(e as Map<String, dynamic>)).toList()",
                $"{dartName}?.map((e) => e.toJson()).toList()",
                false);
        }

        string GetOrCreateCompositeModel(
            string[] fieldNames,
            TypeDescriptor[] fieldDescriptors,
            string columnName,
            Dictionary<string, string> compositeTypeModelsDict,
            List<string> compositeModelsList)
        {
            // Create a unique key based on field names and types (including nested composite info)
            var keyParts = new List<string>();
            for (var i = 0; i < fieldNames.Length; i++)
            {
                var descriptor = fieldDescriptors[i];
                var typePart = descriptor.Type;
                // Include nested composite info in key for uniqueness
                if (descriptor.CompositeFieldNames != null)
                {
                    typePart += $"[composite:{string.Join(";", descriptor.CompositeFieldNames)}]";
                }
                if (descriptor.ArrayCompositeFieldNames != null)
                {
                    typePart += $"[array_composite:{string.Join(";", descriptor.ArrayCompositeFieldNames)}]";
                }
                keyParts.Add($"{fieldNames[i]}:{typePart}");
            }
            var key = string.Join(",", keyParts);

            if (compositeTypeModelsDict.TryGetValue(key, out var existingName))
            {
                return existingName;
            }

            // Generate class name from column name
            var className = EscapeDartClassName(string.Concat(options.ModelPrefix, ConvertToPascalCase(SanitizeDartName(columnName)), options.ModelSuffix));

            // Ensure unique class name
            var baseName = className;
            var counter = 1;
            while (!usedModelNames.Add(className))
            {
                className = $"{baseName}{counter++}";
            }

            // Register the class name early to handle circular references
            compositeTypeModelsDict[key] = className;

            List<DartField> fields = [];
            for (var i = 0; i < fieldNames.Length; i++)
            {
                var fieldName = ConvertToCamelCase(fieldNames[i]);
                var descriptor = fieldDescriptors[i];

                // Check if this field is a nested composite type
                if (descriptor.CompositeFieldNames != null && descriptor.CompositeFieldDescriptors != null)
                {
                    var nestedClassName = GetOrCreateCompositeModel(
                        descriptor.CompositeFieldNames,
                        descriptor.CompositeFieldDescriptors,
                        fieldNames[i],
                        compositeTypeModelsDict,
                        compositeModelsList);
                    fields.Add(CompositeField(fieldName, nestedClassName));
                }
                // Check if this field is an array of composite types
                else if (descriptor.ArrayCompositeFieldNames != null && descriptor.ArrayCompositeFieldDescriptors != null)
                {
                    var elementClassName = GetOrCreateCompositeModel(
                        descriptor.ArrayCompositeFieldNames,
                        descriptor.ArrayCompositeFieldDescriptors,
                        fieldNames[i],
                        compositeTypeModelsDict,
                        compositeModelsList);
                    fields.Add(CompositeArrayField(fieldName, elementClassName));
                }
                else
                {
                    fields.Add(PlainField(fieldName, descriptor));
                }
            }

            compositeModelsList.Add(RenderModel(className, fields));
            return className;
        }
    }

    /// <summary>
    /// Appends the function core (send + return statements, at 2-space indent) to the function body.
    /// For SSE endpoints the core is wrapped in the event-source lifecycle: subscribe on demand,
    /// await connection, run the request in try, schedule the subscription close in finally.
    /// </summary>
    private static void AppendFunctionCore(StringBuilder fn, string core, bool eventsStreamingEnabled, string? eventSourceFunc)
    {
        core = core.TrimEnd('\r', '\n');
        if (!eventsStreamingEnabled)
        {
            fn.AppendLine(core);
            return;
        }
        fn.AppendLine("  SseSubscription? events;");
        fn.AppendLine("  if (onMessage != null) {");
        fn.AppendLine($"    events = await {eventSourceFunc}(onMessage, id: executionId);");
        fn.AppendLine("    await Future<void>.delayed(Duration(milliseconds: awaitConnectionMs));");
        fn.AppendLine("  }");
        fn.AppendLine("  try {");
        fn.AppendLine(IndentLines(core, "  "));
        fn.AppendLine("  } finally {");
        fn.AppendLine("    if (events != null) {");
        fn.AppendLine("      final subscription = events;");
        fn.AppendLine("      unawaited(");
        fn.AppendLine("        Future<void>.delayed(Duration(milliseconds: closeAfterMs), subscription.close),");
        fn.AppendLine("      );");
        fn.AppendLine("    }");
        fn.AppendLine("  }");
    }

    private static string IndentLines(string text, string pad)
    {
        var lines = text.Split('\n');
        StringBuilder sb = new();
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                sb.Append('\n');
            }
            var line = lines[i].TrimEnd('\r');
            if (line.Length > 0)
            {
                sb.Append(pad);
                sb.Append(line);
            }
        }
        return sb.ToString();
    }

    private string BuildStatusReturnBlock(string[]? payloadLines, string responseName)
    {
        StringBuilder rb = new();
        if (payloadLines is not null)
        {
            rb.AppendLine("  final ok = response.statusCode >= 200 && response.statusCode < 300;");
        }
        rb.AppendLine($"  return {options.ResultTypeName}<{responseName}>(");
        rb.AppendLine("    status: response.statusCode,");
        if (payloadLines is not null)
        {
            if (payloadLines.Length == 1)
            {
                rb.AppendLine($"    response: ok ? {payloadLines[0]} : null,");
            }
            else
            {
                rb.AppendLine("    response: ok");
                rb.AppendLine($"        ? {payloadLines[0]}");
                foreach (var line in payloadLines[1..])
                {
                    rb.AppendLine($"            {line}");
                }
                rb.AppendLine("        : null,");
            }
        }
        rb.AppendLine("    error: _error(response),");
        rb.Append("  );");
        return rb.ToString();
    }

    private static string BuildPlainReturnBlock(string[] payloadLines)
    {
        if (payloadLines.Length == 1)
        {
            return $"  return {payloadLines[0]};";
        }
        StringBuilder rb = new();
        rb.Append($"  return {payloadLines[0]}");
        for (var i = 1; i < payloadLines.Length; i++)
        {
            rb.AppendLine();
            rb.Append($"      {payloadLines[i]}");
        }
        rb.Append(';');
        return rb.ToString();
    }

    private static string RenderModel(string className, List<DartField> fields)
    {
        StringBuilder sb = new();
        sb.AppendLine($"class {className} {{");
        foreach (var field in fields)
        {
            sb.AppendLine($"  final {field.DartType} {field.DartName};");
        }
        sb.AppendLine();
        if (fields.Count == 0)
        {
            sb.AppendLine($"  const {className}();");
        }
        else
        {
            sb.AppendLine($"  const {className}({{");
            foreach (var field in fields)
            {
                sb.AppendLine($"    this.{field.DartName},");
            }
            sb.AppendLine("  });");
        }
        sb.AppendLine();
        sb.AppendLine($"  factory {className}.fromJson(Map<String, dynamic> json) {{");
        if (fields.Count == 0)
        {
            sb.AppendLine($"    return const {className}();");
        }
        else
        {
            sb.AppendLine($"    return {className}(");
            foreach (var field in fields)
            {
                sb.AppendLine($"      {field.DartName}: {field.ReadExpr},");
            }
            sb.AppendLine("    );");
        }
        sb.AppendLine("  }");
        sb.AppendLine();
        sb.AppendLine("  Map<String, dynamic> toJson() {");
        sb.AppendLine("    return {");
        foreach (var field in fields)
        {
            sb.AppendLine(field.OmitNullInJson
                ? $"      if ({field.DartName} != null) '{field.JsonKey}': {field.WriteExpr},"
                : $"      '{field.JsonKey}': {field.WriteExpr},");
        }
        sb.AppendLine("    };");
        sb.AppendLine("  }");
        sb.Append('}');
        return sb.ToString();
    }

    private static string RenderUploadModel(string className)
    {
        StringBuilder sb = new();
        sb.AppendLine($"class {className} {{");
        sb.AppendLine("  final String? type;");
        sb.AppendLine("  final String? fileName;");
        sb.AppendLine("  final String? contentType;");
        sb.AppendLine("  final int? size;");
        sb.AppendLine("  final bool? success;");
        sb.AppendLine("  final String? status;");
        sb.AppendLine("  final Map<String, dynamic> raw;");
        sb.AppendLine();
        sb.AppendLine($"  const {className}({{");
        sb.AppendLine("    this.type,");
        sb.AppendLine("    this.fileName,");
        sb.AppendLine("    this.contentType,");
        sb.AppendLine("    this.size,");
        sb.AppendLine("    this.success,");
        sb.AppendLine("    this.status,");
        sb.AppendLine("    this.raw = const {},");
        sb.AppendLine("  });");
        sb.AppendLine();
        sb.AppendLine($"  factory {className}.fromJson(Map<String, dynamic> json) {{");
        sb.AppendLine($"    return {className}(");
        sb.AppendLine("      type: json['type'] as String?,");
        sb.AppendLine("      fileName: json['fileName'] as String?,");
        sb.AppendLine("      contentType: json['contentType'] as String?,");
        sb.AppendLine("      size: (json['size'] as num?)?.toInt(),");
        sb.AppendLine("      success: json['success'] as bool?,");
        sb.AppendLine("      status: json['status'] as String?,");
        sb.AppendLine("      raw: json,");
        sb.AppendLine("    );");
        sb.AppendLine("  }");
        sb.AppendLine();
        sb.AppendLine("  Map<String, dynamic> toJson() {");
        sb.AppendLine("    return raw;");
        sb.AppendLine("  }");
        sb.Append('}');
        return sb.ToString();
    }

    private string GetComment(Routine routine, string resultType, List<(string name, string desc)> paramComments)
    {
        List<string> lines = [];
        if (options.CommentHeader != CommentHeader.None)
        {
            var comment = options.CommentHeader switch
            {
                CommentHeader.Simple => routine.SimpleDefinition,
                CommentHeader.Full => routine.FullDefinition,
                _ => "",
            };
            foreach (var line in comment.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line == "\r")
                {
                    continue;
                }
                lines.Add(line.TrimEnd('\r'));
            }

            if (options.CommentHeaderIncludeComments && !string.IsNullOrEmpty(routine.Comment?.Trim()))
            {
                lines.Add("");
                var commentLines = routine
                    .Comment
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries);

                if (routine.Type == RoutineType.SqlFile)
                {
                    foreach (var line in commentLines)
                    {
                        if (line == "\r")
                        {
                            continue;
                        }
                        lines.Add(line.TrimEnd('\r'));
                    }
                }
                else
                {
                    // Functions/procedures: wrap in COMMENT ON statement
                    foreach (var (line, index) in commentLines.Select((l, i) => (l, i)))
                    {
                        if (line == "\r" && index > 0)
                        {
                            continue;
                        }
                        var commentLine = line.Replace("'", "''").TrimEnd('\r');
                        if (index == 0)
                        {
                            commentLine = string.Concat($"comment on {routine.Type.ToString().ToLowerInvariant()} {routine.Schema}.{routine.Name} is '", commentLine);
                        }
                        if (index == commentLines.Length - 1)
                        {
                            commentLine = string.Concat(commentLine, "';");
                        }
                        lines.Add(commentLine);
                    }
                }
            }
        }

        List<string> footer = [];
        foreach (var (name, desc) in paramComments)
        {
            footer.Add($"[{name}] {desc}");
        }
        if (!string.IsNullOrEmpty(resultType) && resultType != "void")
        {
            footer.Add($"Returns `{resultType}`.");
        }
        if (footer.Count > 0)
        {
            if (lines.Count > 0)
            {
                lines.Add("");
            }
            lines.AddRange(footer);
        }
        if (routine.Type != RoutineType.SqlFile)
        {
            if (lines.Count > 0)
            {
                lines.Add("");
            }
            lines.Add($"See {routine.Type.ToString().ToUpperInvariant()} {routine.Schema}.{routine.Name}");
        }

        return string.Join(Environment.NewLine, lines.Select(l => l.Length == 0 ? "///" : string.Concat("/// ", l)));
    }

    private static bool IsIntegerFamily(TypeDescriptor descriptor) =>
        descriptor.BaseDbType is NpgsqlDbType.Smallint or NpgsqlDbType.Integer or NpgsqlDbType.Bigint;

    /// <summary>
    /// Returns the base (non-nullable) Dart type for a descriptor: int, double, bool,
    /// DateTime, String, the configured JSON type, or List&lt;T&gt; for arrays.
    /// </summary>
    private string GetDartType(TypeDescriptor descriptor)
    {
        string type;
        if (descriptor.IsNumeric)
        {
            type = IsIntegerFamily(descriptor) ? "int" : "double";
        }
        else if (descriptor.IsBoolean)
        {
            type = "bool";
        }
        else if (descriptor.IsJson)
        {
            type = options.DefaultJsonType;
        }
        else if ((descriptor.IsDate || descriptor.IsDateTime) && options.UseDateTimeType)
        {
            type = "DateTime";
        }
        else
        {
            type = "String";
        }
        if (descriptor.IsArray)
        {
            type = $"List<{type}>";
        }
        return type;
    }

    private static string NullableType(string type) => type == "dynamic" ? type : string.Concat(type, "?");

    /// <summary>
    /// Element type and per-element conversion expression (over variable `e`) for array values.
    /// Conversion is null when no mapping is needed (dynamic elements).
    /// </summary>
    private (string Type, string? Conversion) GetElementInfo(TypeDescriptor descriptor)
    {
        if (descriptor.IsNumeric)
        {
            return IsIntegerFamily(descriptor) ? ("int", "(e as num).toInt()") : ("double", "(e as num).toDouble()");
        }
        if (descriptor.IsBoolean)
        {
            return ("bool", "e as bool");
        }
        if (descriptor.IsJson)
        {
            return (options.DefaultJsonType, null);
        }
        if ((descriptor.IsDate || descriptor.IsDateTime) && options.UseDateTimeType)
        {
            return ("DateTime", "DateTime.parse(e as String)");
        }
        return ("String", "e as String");
    }

    /// <summary>
    /// fromJson right-hand side for a model field read from json[key].
    /// </summary>
    private string GetReadExpr(TypeDescriptor descriptor, string jsonKey, bool forceScalar = false)
    {
        var access = $"json['{jsonKey}']";
        if (descriptor.IsArray && !forceScalar)
        {
            var (_, conversion) = GetElementInfo(descriptor);
            return conversion is null
                ? $"{access} as List?"
                : $"({access} as List?)?.map((e) => {conversion}).toList()";
        }
        if (descriptor.IsNumeric)
        {
            return IsIntegerFamily(descriptor) ? $"({access} as num?)?.toInt()" : $"({access} as num?)?.toDouble()";
        }
        if (descriptor.IsBoolean)
        {
            return $"{access} as bool?";
        }
        if (descriptor.IsJson)
        {
            return options.DefaultJsonType == "dynamic" ? access : $"{access} as {options.DefaultJsonType}?";
        }
        if ((descriptor.IsDate || descriptor.IsDateTime) && options.UseDateTimeType)
        {
            return $"{access} == null ? null : DateTime.parse({access} as String)";
        }
        return $"{access} as String?";
    }

    /// <summary>
    /// toJson right-hand side for a model field.
    /// </summary>
    private string GetWriteExpr(TypeDescriptor descriptor, string dartName)
    {
        var isDateTime = (descriptor.IsDate || descriptor.IsDateTime) && options.UseDateTimeType;
        if (!isDateTime)
        {
            return dartName;
        }
        return descriptor.IsArray
            ? $"{dartName}?.map((e) => e.toIso8601String()).toList()"
            : $"{dartName}?.toIso8601String()";
    }

    /// <summary>
    /// Cast expression for a single scalar value taken from a decoded JSON body.
    /// </summary>
    private string GetScalarJsonExpr(TypeDescriptor descriptor, string jsonDecodeExpr)
    {
        if (descriptor.IsNumeric)
        {
            return IsIntegerFamily(descriptor)
                ? $"({jsonDecodeExpr} as num).toInt()"
                : $"({jsonDecodeExpr} as num).toDouble()";
        }
        if (descriptor.IsBoolean)
        {
            return $"{jsonDecodeExpr} as bool";
        }
        if (descriptor.IsJson)
        {
            return jsonDecodeExpr;
        }
        if ((descriptor.IsDate || descriptor.IsDateTime) && options.UseDateTimeType)
        {
            return $"DateTime.parse({jsonDecodeExpr} as String)";
        }
        return $"{jsonDecodeExpr} as String";
    }

    private string GetStatusClasses() => $$"""
class {{options.ErrorTypeName}} {
  final int? status;
  final String? title;
  final String? detail;

  const {{options.ErrorTypeName}}({this.status, this.title, this.detail});

  factory {{options.ErrorTypeName}}.fromJson(Map<String, dynamic> json) {
    return {{options.ErrorTypeName}}(
      status: (json['status'] as num?)?.toInt(),
      title: json['title'] as String?,
      detail: json['detail'] as String?,
    );
  }
}

class {{options.ResultTypeName}}<T> {
  final int status;
  final T? response;
  final {{options.ErrorTypeName}}? error;

  const {{options.ResultTypeName}}({required this.status, this.response, this.error});

  bool get ok => status >= 200 && status < 300;
}
""";

    private string GetErrorHelper() => $$"""
{{options.ErrorTypeName}}? _error(http.Response response) {
  if (response.statusCode >= 200 && response.statusCode < 300) {
    return null;
  }
  if (response.headers['content-length'] == '0') {
    return null;
  }
  return {{options.ErrorTypeName}}.fromJson(
    jsonDecode(utf8.decode(response.bodyBytes)) as Map<String, dynamic>,
  );
}
""";

    private const string ClientScaffold = """
/// Override to inject a custom [http.Client] (e.g. MockClient in tests).
http.Client? httpClient;

http.Client get _client => httpClient ??= http.Client();
""";

    private const string SendScaffold = """
Future<http.Response> _send(
  String method,
  Uri uri, {
  Map<String, String>? headers,
  String? body,
}) async {
  final request = http.Request(method, uri);
  if (headers != null) {
    request.headers.addAll(headers);
  }
  if (body != null) {
    request.body = body;
  }
  return http.Response.fromStream(await _client.send(request));
}
""";

    private const string SendScaffoldWithParse = """
Future<http.Response> _send(
  String method,
  Uri uri, {
  Map<String, String>? headers,
  String? body,
  http.Request Function(http.Request request)? parseRequest,
}) async {
  var request = http.Request(method, uri);
  if (headers != null) {
    request.headers.addAll(headers);
  }
  if (body != null) {
    request.body = body;
  }
  if (parseRequest != null) {
    request = parseRequest(request);
  }
  return http.Response.fromStream(await _client.send(request));
}
""";

    private const string SendMultipartScaffold = """
Future<http.Response> _sendMultipart(
  http.MultipartRequest multipart, {
  void Function(int loaded, int total)? progress,
}) async {
  if (progress == null) {
    return http.Response.fromStream(await _client.send(multipart));
  }
  final total = multipart.contentLength;
  final bytes = multipart.finalize();
  final request = http.StreamedRequest(multipart.method, multipart.url);
  request.headers.addAll(multipart.headers);
  request.contentLength = total;
  var loaded = 0;
  bytes.listen(
    (chunk) {
      loaded += chunk.length;
      progress(loaded, total);
      request.sink.add(chunk);
    },
    onDone: request.sink.close,
    onError: request.sink.addError,
  );
  return http.Response.fromStream(await _client.send(request));
}
""";

    private const string SseScaffold = """
String _randomId() {
  final random = math.Random();
  return List.generate(32, (_) => random.nextInt(16).toRadixString(16)).join();
}

/// Active server-sent events subscription. Call [close] to stop listening.
class SseSubscription {
  final http.Client _sseClient;
  final StreamSubscription<String> _lines;

  SseSubscription._(this._sseClient, this._lines);

  Future<void> close() async {
    await _lines.cancel();
    _sseClient.close();
  }
}

Future<SseSubscription> _sse(
  Uri uri,
  void Function(String message) onMessage,
) async {
  final client = http.Client();
  final request = http.Request('GET', uri);
  request.headers['Accept'] = 'text/event-stream';
  final response = await client.send(request);
  final data = StringBuffer();
  final lines = response.stream
      .transform(utf8.decoder)
      .transform(const LineSplitter())
      .listen((line) {
    if (line.startsWith('data:')) {
      if (data.isNotEmpty) {
        data.write('\n');
      }
      data.write(line.startsWith('data: ') ? line.substring(6) : line.substring(5));
    } else if (line.isEmpty && data.isNotEmpty) {
      onMessage(data.toString());
      data.clear();
    }
  });
  return SseSubscription._(client, lines);
}
""";

    private const string QueryScaffold = """
String _query(Map<String, Object?> query) {
  final parts = <String>[];
  query.forEach((key, value) {
    if (value is List) {
      for (final v in value) {
        parts.add(v == null ? '$key=' : '$key=${Uri.encodeQueryComponent(_str(v))}');
      }
    } else if (value == null) {
      parts.add('$key=');
    } else {
      parts.add('$key=${Uri.encodeQueryComponent(_str(value))}');
    }
  });
  return '?${parts.join('&')}';
}

String _str(Object value) =>
    value is DateTime ? value.toIso8601String() : value.toString();
""";

    private static readonly char[] separator = ['_', '-', '/', '\\'];

    // Dart reserved words and builtin identifiers that cannot (or should not) be used bare,
    // plus contextual keywords escaped defensively. Escaped with a trailing underscore.
    private static readonly HashSet<string> DartReservedWords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "assert", "async", "await", "base", "break", "case", "catch", "class",
        "const", "continue", "covariant", "default", "deferred", "do", "dynamic", "else", "enum",
        "export", "extends", "extension", "external", "factory", "false", "final", "finally",
        "for", "get", "hide", "if", "implements", "import", "in", "interface", "is", "late",
        "library", "mixin", "new", "null", "of", "on", "operator", "out", "part", "required",
        "rethrow", "return", "sealed", "set", "show", "static", "super", "switch", "sync",
        "this", "throw", "true", "try", "type", "typedef", "var", "void", "when", "while",
        "with", "yield",
    };

    // dart:core type names that generated identifiers must not shadow. A field named `double`
    // makes every other `double`-typed sibling field in the class fail to resolve.
    private static readonly HashSet<string> DartCoreTypeNames = new(StringComparer.Ordinal)
    {
        "String", "int", "double", "num", "bool", "List", "Map", "Set", "Object", "DateTime",
        "Function", "Never", "Null", "Iterable", "Future", "Stream", "Uri", "Type", "Symbol",
        "Record", "Enum", "Duration", "RegExp", "Exception", "Error",
    };

    // Object member names: a field with one of these names conflicts with the generated toJson()
    // method or the inherited Object members.
    private static readonly HashSet<string> DartMemberNames = new(StringComparer.Ordinal)
    {
        "toJson", "fromJson", "toString", "hashCode", "runtimeType", "noSuchMethod",
    };

    public static string EscapeDartIdentifier(string name) =>
        DartReservedWords.Contains(name) || DartCoreTypeNames.Contains(name) || DartMemberNames.Contains(name)
            ? string.Concat(name, "_")
            : name;

    public static string EscapeDartClassName(string name) =>
        DartReservedWords.Contains(name) || DartCoreTypeNames.Contains(name) ? string.Concat(name, "_") : name;

    public static string SanitizeDartName(string name)
    {
        // Replace invalid starting characters with underscore
        name = InvalidChars1().Replace(name, "_");

        // Replace any other invalid characters with underscore
        name = InvalidChars2().Replace(name, "_");

        return name;
    }

    public static string ConvertToPascalCase(string value)
    {
        return value
            .Split(separator, StringSplitOptions.RemoveEmptyEntries)
            .Select((s) =>
                string.Concat(char.ToUpperInvariant(s[0]), s[1..]))
            .Aggregate(string.Empty, string.Concat)
            .Trim('"');
    }

    public static string ConvertToCamelCase(string value)
    {
        return value
            .Split(separator, StringSplitOptions.RemoveEmptyEntries)
            .Select((s, i) =>
                string.Concat(i == 0 ? char.ToLowerInvariant(s[0]) : char.ToUpperInvariant(s[0]), s[1..]))
            .Aggregate(string.Empty, string.Concat)
            .Trim('"');
    }

    public static string ConvertToSnakeCase(string value)
    {
        var sanitized = SanitizeDartName(value);
        StringBuilder sb = new();
        for (var i = 0; i < sanitized.Length; i++)
        {
            var ch = sanitized[i];
            if (char.IsUpper(ch))
            {
                if (i > 0 && sanitized[i - 1] != '_')
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    private static string DartQuote(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "''";
        }
        if ((name.StartsWith('\'') && name.EndsWith('\'')) ||
            (name.StartsWith('"') && name.EndsWith('"')))
        {
            return name;
        }
        return $"'{name}'";
    }

    private string GetHost()
    {
        if (!options.IncludeHost)
        {
            return "";
        }
        if (options.CustomHost is not null)
        {
            return options.CustomHost;
        }
        string? host = null;
        if (_builder is WebApplication app)
        {
            if (app.Urls.Count != 0)
            {
                host = app.Urls.FirstOrDefault();
            }
            else
            {
                var section = app.Configuration.GetSection("ASPNETCORE_URLS");
                if (section.Value is not null)
                {
                    host = section.Value.Split(";").LastOrDefault();
                }
                if (host is null && app.Configuration["urls"] is not null)
                {
                    host = app.Configuration["urls"];
                }
            }
        }
        // default, assumed host
        host ??= "http://localhost:5000";
        return host;
    }

    /// <summary>
    /// Converts a path with {param} placeholders to a Dart interpolated string body,
    /// resolving each placeholder to the generated request field name.
    /// Example: "/products/{p_id}" => "/products/${request.pId}"
    /// </summary>
    private string ConvertPathToInterpolation(string path, Dictionary<string, string> paramFieldByName, Routine routine)
    {
        return PathParamRegex().Replace(path, match =>
        {
            var placeholder = match.Groups[1].Value;
            if (paramFieldByName.TryGetValue(placeholder, out var dartName))
            {
                return string.Concat("${request.", dartName, "}");
            }
            Logger?.LogWarning(
                "DartClient: could not resolve path parameter {placeholder} to a request field for {schema}.{name}; generated code may not compile",
                placeholder, routine.Schema, routine.Name);
            return string.Concat("${request.", placeholder, "}");
        });
    }

    [GeneratedRegex("^[^a-zA-Z_]")]
    private static partial Regex InvalidChars1();
    [GeneratedRegex("[^a-zA-Z0-9_]")]
    private static partial Regex InvalidChars2();
    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PathParamRegex();
}
