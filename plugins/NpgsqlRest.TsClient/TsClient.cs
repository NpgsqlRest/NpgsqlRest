using System.Text;
using System.Text.RegularExpressions;
using static NpgsqlRest.NpgsqlRestOptions;

namespace NpgsqlRest.TsClient;

public partial class TsClient(TsClientOptions options) : IEndpointCreateHandler
{
    private IApplicationBuilder _builder = default!;
    private NpgsqlRestOptions? _npgsqlRestoptions;

    private const string Enabled = "tsclient";
    private const string Module = "tsclient_module";
    
    private const string SseEvents = "tsclient_events";
    private const string IncludeParseUrl = "tsclient_parse_url";
    private const string IncludeParseRequest = "tsclient_parse_request";
    private const string IncludeStatusCode = "tsclient_status_code";
    
    public void Setup(IApplicationBuilder builder, NpgsqlRestOptions npgsqlRestoptions)
    {
        _builder = builder;
        _npgsqlRestoptions = npgsqlRestoptions;
    }

    public void Cleanup(RoutineEndpoint[] endpoints)
    {
        if (options.FilePath is null)
        {
            return;
        }
        
        var containsModuleParam = endpoints.Any(e => e.CustomParameters?.ContainsKey(Module) is true);
        if (!options.BySchema && containsModuleParam)
        {
            Run(endpoints, options.FilePath);
        }
        else
        {
            if (!options.FilePath.Contains("{0}"))
            {
                Logger?.LogError("TsClient Option FilePath doesn't contain {{0}} formatter and BySchema options is true. Some files may be overwritten! Existing...");
                return;
            }
            
            HashSet<string> processedModules = [];
            if (containsModuleParam)
            {
                foreach (var group in endpoints.GroupBy(e => e.CustomParameters?.GetValueOrDefault(Module)))
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
                    if (options.SkipTypes && filename.EndsWith(".ts"))
                    {
                        filename = filename[..^3] + ".js";
                    }
                    Run([.. group], filename);
                }
            }

            foreach (var group in endpoints.GroupBy(e => e.Routine.Schema))
            {
                var filename = string.Format(options.FilePath, ConvertToCamelCase(group.Key));
                if (options.SkipTypes && filename.EndsWith(".ts"))
                {
                    filename = filename[..^3] + ".js";
                }
                RoutineEndpoint[] groupArray = [.. group.Where(g => 
                    (g.CustomParameters?.ContainsKey(Module) is false) || 
                    (g.CustomParameters?.GetValueOrDefault(Module) is null) ||
                    (!processedModules.Contains(g.CustomParameters?.GetValueOrDefault(Module) ?? ""))
                )];
                if (groupArray.Length == 0)
                {
                    continue;
                }
                Run([.. groupArray], filename);
            }
        }
    }

    private void Run(RoutineEndpoint[] endpoints, string? fileName)
    {
        if (fileName is null)
        {
            return;
        }

        RoutineEndpoint[] filtered = [.. endpoints.Where(e => e.CustomParameters.ParameterEnabled(Enabled) is not false)];

        Dictionary<string, string> modelsDict = [];
        Dictionary<string, int> names = [];
        // Track generated composite type interfaces to avoid duplicates
        // Key: composite type identifier (schema.typename), Value: generated interface name
        Dictionary<string, string> compositeTypeInterfaces = [];
        StringBuilder contentHeader = new();
        StringBuilder content = new();
        StringBuilder interfaces = new();
        StringBuilder compositeInterfaces = new();

        foreach (var import in options.CustomImports)
        {
            contentHeader.AppendLine(import);
        }

        if (filtered.Where(e => e.RequestParamType == RequestParamType.QueryString).Any())
        {
            contentHeader.AppendLine(
                options.ImportBaseUrlFrom is not null ?
                    string.Format("import {{ baseUrl }} from \"{0}\";", options.ImportBaseUrlFrom) :
                    string.Format("const baseUrl = \"{0}\";", GetHost()));

            bool haveParseQuery = filtered
                .Where(e => e.RequestParamType == RequestParamType.QueryString &&
                            e.Routine.ParamCount > 0 &&
                            e.Routine.ParamCount > (e.PathParameters?.Length ?? 0))
                .Any();

            if (haveParseQuery)
            {
                if (!options.SkipTypes)
                {
                    contentHeader.AppendLine(options.ImportParseQueryFrom is not null ?
                        string.Format(
                        "import {{ parseQuery }} from \"{0}\";", options.ImportParseQueryFrom) :
                        """
                    const parseQuery = (query: Record<any, any>) => "?" + Object.keys(query ? query : {})
                        .map(key => {
                            const value = (query[key] != null ? query[key] : "") as string;
                            if (Array.isArray(value)) {
                                return value.map((s: string) => s ? `${key}=${encodeURIComponent(s)}` : `${key}=`).join("&");
                            }
                            return `${key}=${encodeURIComponent(value)}`;
                        })
                        .join("&");
                    """);
                }
                else
                {
                    contentHeader.AppendLine(options.ImportParseQueryFrom is not null ?
                        string.Format(
                        "import {{ parseQuery }} from \"{0}\";", options.ImportParseQueryFrom) :
                        """
                    const parseQuery = query => "?" + Object.keys(query ? query : {})
                        .map(key => {
                            const value = query[key] != null ? query[key] : "";
                            if (Array.isArray(value)) {
                                return value.map(s => s ? `${key}=${encodeURIComponent(s)}` : `${key}=`).join("&");
                            }
                            return `${key}=${encodeURIComponent(value)}`;
                        })
                        .join("&");
                    """);
                }
            }
        }
        else
        {
            contentHeader.AppendLine(
                options.ImportBaseUrlFrom is not null ?
                    string.Format("import {{ baseUrl }} from \"{0}\";", options.ImportBaseUrlFrom) :
                    string.Format("const baseUrl = \"{0}\";", GetHost()));
        }
        if (options.ExportUrls)
        {
            contentHeader.AppendLine();
        }

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
                        Logger?.LogDebug("Deleted file: {fileName}", fileName);
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
        // Insert composite type interfaces at the beginning of interfaces
        if (compositeInterfaces.Length > 0)
        {
            interfaces.Insert(0, compositeInterfaces.ToString());
        }

        if (!options.CreateSeparateTypeFile)
        {
            if (!options.SkipTypes)
            {
                interfaces.AppendLine(content.ToString());
                if (contentHeader.Length > 0)
                {
                    contentHeader.AppendLine();
                    interfaces.Insert(0, contentHeader.ToString());
                }
                AddHeader(interfaces);
                File.WriteAllText(fileName, interfaces.ToString());
                Logger?.LogDebug("Created Typescript file: {fileName}", fileName);
            }
        }
        else
        {
            if (!options.SkipTypes)
            {
                var typeFile = fileName.Replace(".ts", "Types.d.ts");
                AddHeader(interfaces);
                File.WriteAllText(typeFile, interfaces.ToString());
                Logger?.LogDebug("Created Typescript type file: {typeFile}", typeFile);
            }

            if (contentHeader.Length > 0)
            {
                content.Insert(0, contentHeader.ToString());
            }
            AddHeader(content);
            File.WriteAllText(fileName, content.ToString());
            if (!options.SkipTypes)
            {
                Logger?.LogDebug("Created Typescript file: {fileName}", fileName);
            }
            else
            {
                Logger?.LogDebug("Created Javascript file: {fileName}", fileName);
            }
        }
        return;

        void AddHeader(StringBuilder sb)
        {
            if (options.HeaderLines.Count == 0)
            {
                return;
            }
            var now = DateTime.Now.ToString("O");
            sb.Insert(0, string.Concat(string.Join(
                Environment.NewLine, 
                options.HeaderLines.Select(l => string.Format(l, now).Trim())), Environment.NewLine)
            );
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
            //var paramTypeDescriptors = routine.ParamTypeDescriptor;
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
            name = SanitizeJavaScriptVariableName(name);
            var pascal = ConvertToPascalCase(name);
            var camel = ConvertToCamelCase(name);

            if (options.SkipFunctionNames.Contains(camel))
            {
                return false;
            }

            content.AppendLine();

            string? requestName = null;
            string[] paramNames = new string[paramCount];
            string? bodyParameterName = null;
            for (var i = 0; i < paramCount; i++)
            {
                var parameter = routine.Parameters[i];
                var descriptor = parameter.TypeDescriptor;//paramTypeDescriptors[i];
                var nameSuffix = (descriptor.HasDefault || descriptor.CustomType is not null) ? "?" : "";
                paramNames[i] = QuoteJavaScriptVariableName($"{parameter.ConvertedName}{nameSuffix}");
                if (string.Equals(endpoint.BodyParameterName, parameter.ConvertedName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(endpoint.BodyParameterName, parameter.ActualName, StringComparison.OrdinalIgnoreCase))
                {
                    bodyParameterName = paramNames[i];
                }
            }
            string requestDesc = "";
            if (paramCount > 0)
            {
                StringBuilder req = new();
                requestName = $"I{pascal}Request";
                requestDesc = string.Concat(requestDesc, "{");
                for (var i = 0; i < paramCount; i++)
                {
                    var descriptor = routine.Parameters[i].TypeDescriptor;
                    var type = GetTsType(descriptor, false);
                    req.AppendLine($"    {paramNames[i]}: {type} | null;");
                    requestDesc = string.Concat(requestDesc, $"{paramNames[i]}: {type} | null;");
                }
                requestDesc = string.Concat(requestDesc, "}");
                if (modelsDict.TryGetValue(req.ToString(), out var newName))
                {
                    requestName = newName;
                }
                else
                {
                    if (!options.SkipTypes)
                    {
                        if (options.UniqueModels)
                        {
                            modelsDict.Add(req.ToString(), requestName);
                        }
                        req.Insert(0, $"interface {requestName} {{{Environment.NewLine}");
                        req.AppendLine("}");
                        req.AppendLine();
                        interfaces.Append(req);
                    }
                }
            }

            string responseName = "void";
            bool json = false;
            string? returnExp = null;
            string GetReturnExp(string responseExp)
            {
                if (includeStatusCode)
                {
                    var errorType = options.ErrorType.EndsWith(" | undefined")
                        ? options.ErrorType[..^12]  // Remove " | undefined" suffix for inline use
                        : options.ErrorType;
                    return string.Concat(
                        "return {",
                        Environment.NewLine,
                        "        status: response.status,",
                        Environment.NewLine,
                        "        response: ",
                        (responseExp == "await response.text()" ?
                            "response.ok ? " + responseExp + " : undefined!" :
                            string.Concat("response.ok ? ", responseExp, " : undefined!")),
                        ",",
                        Environment.NewLine,
                        $"        error: !response.ok && response.headers.get(\"content-length\") !== \"0\" ? {options.ErrorExpression} as {errorType} : undefined",
                        Environment.NewLine,
                        "    };");
                }
                return string.Concat("return ", responseExp, ";");
            }

            if (!isVoid)
            {
                if (endpoint.Upload)
                {
                    responseName = $"I{pascal}Response";
                    StringBuilder resp = new();

                    resp.AppendLine($"interface {responseName} {{");
                    resp.AppendLine("    type: string;");
                    resp.AppendLine("    fileName: string;");
                    resp.AppendLine("    contentType: string;");
                    resp.AppendLine("    size: number;");
                    resp.AppendLine("    success: boolean;");
                    resp.AppendLine("    status: string;");
                    resp.AppendLine("    [key: string]: string | number | boolean;");
                    resp.AppendLine("}");
                    resp.AppendLine();
                    interfaces.Append(resp);
                    responseName = string.Concat(responseName, "[]");
                    // Note: Don't set json = true here because upload endpoints use FormData,
                    // and the Content-Type header should be set automatically by the browser
                    if (!options.SkipTypes)
                    {
                        returnExp = GetReturnExp($"await response.json() as {responseName}");
                    }
                    else
                    {
                        returnExp = GetReturnExp("await response.json()");
                    }
                }
                else if (returnsSet == false && columnCount == 1 && !returnsRecordType)
                {
                    var descriptor = columnsTypeDescriptor[0];
                    responseName = descriptor.IsJson ? "any" : GetTsType(descriptor, true);
                    if (descriptor.IsArray)
                    {
                        json = true;
                        //if (options.SkipTypes is false)
                        //{
                        //    returnExp = GetReturnExp($"await response.json() as {responseName}[]");
                        //}
                        //else
                        //{
                        //    returnExp = GetReturnExp("await response.json()");
                        //}
                        returnExp = GetReturnExp("await response.json()");
                    }
                    else
                    {
                        if (descriptor.IsDate || descriptor.IsDateTime)
                        {
                            returnExp = GetReturnExp("new Date(await response.text())");
                        }
                        else if (descriptor.IsNumeric)
                        {
                            returnExp = GetReturnExp("Number(await response.text())");
                        }
                        else if (descriptor.IsBoolean)
                        {
                            returnExp = GetReturnExp("await response.text() == \"t\"");
                        }
                        else if (descriptor.IsJson)
                        {
                            returnExp = GetReturnExp("await response.json()");
                        }
                        else
                        {
                            returnExp = GetReturnExp("await response.text()");
                        }
                    }
                }
                else
                {
                    json = true;
                    if (returnsUnnamedSet)
                    {
                        if (columnCount > 0)
                        {
                            responseName = GetTsType(columnsTypeDescriptor[0], false);
                        }
                        else
                        {
                            responseName = "string[]";
                        }
                    }
                    else
                    {
                        StringBuilder resp = new();
                        responseName = $"I{pascal}Response";

                        // Collect column indices to skip (expanded composite columns)
                        HashSet<int> skipIndices = [];
                        if (routine.CompositeColumnInfo is not null)
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
                            // Skip expanded composite columns
                            if (skipIndices.Contains(i))
                            {
                                continue;
                            }

                            // Check if this is a nested composite column
                            if (routine.CompositeColumnInfo is not null &&
                                routine.CompositeColumnInfo.TryGetValue(i, out var compositeInfo))
                            {
                                // Generate interface for this composite type if not already done
                                var compositeInterfaceName = GetOrCreateCompositeInterface(
                                    compositeInfo.FieldNames,
                                    compositeInfo.FieldDescriptors,
                                    compositeInfo.ConvertedColumnName,
                                    compositeTypeInterfaces,
                                    compositeInterfaces);
                                resp.AppendLine($"    {compositeInfo.ConvertedColumnName}: {compositeInterfaceName} | null;");
                                continue;
                            }

                            // Check if this is an array of composite types
                            if (routine.ArrayCompositeColumnInfo is not null &&
                                routine.ArrayCompositeColumnInfo.TryGetValue(i, out var arrayCompositeInfo))
                            {
                                // Generate interface for this composite type if not already done
                                var compositeInterfaceName = GetOrCreateCompositeInterface(
                                    arrayCompositeInfo.FieldNames,
                                    arrayCompositeInfo.FieldDescriptors,
                                    routine.ColumnNames[i],
                                    compositeTypeInterfaces,
                                    compositeInterfaces);
                                resp.AppendLine($"    {routine.ColumnNames[i]}: {compositeInterfaceName}[] | null;");
                                continue;
                            }

                            var descriptor = columnsTypeDescriptor[i];
                            var type = GetTsType(descriptor, false);
                            if (descriptor.IsJson)
                            {
                                resp.AppendLine($"    {routine.ColumnNames[i]}: any; // JSON");
                            }
                            else
                            {
                                resp.AppendLine($"    {routine.ColumnNames[i]}: {type} | null;");
                            }
                        }

                        if (modelsDict.TryGetValue(resp.ToString(), out var newName))
                        {
                            responseName = newName;
                        }
                        else
                        {
                            if (!options.SkipTypes)
                            {
                                if (options.UniqueModels)
                                {
                                    modelsDict.Add(resp.ToString(), responseName);
                                }
                                resp.Insert(0, $"interface {responseName} {{{Environment.NewLine}");
                                resp.AppendLine("}");
                                resp.AppendLine();
                                interfaces.Append(resp);
                            }
                        }
                    }
                    if (returnsSet)
                    {
                        responseName = string.Concat(responseName, "[]");
                    }
                    if (!options.SkipTypes)
                    {
                        returnExp = GetReturnExp($"await response.json() as {responseName}");
                    }
                    else
                    {
                        returnExp = GetReturnExp("await response.json()");
                    }
                }
            }
            else
            {
                if (includeStatusCode)
                {
                    var errorType = options.ErrorType.EndsWith(" | undefined")
                        ? options.ErrorType[..^12]  // Remove " | undefined" suffix for inline use
                        : options.ErrorType;
                    returnExp = string.Concat(
                        "return {",
                        Environment.NewLine,
                        "        status: response.status,",
                        Environment.NewLine,
                        $"        error: !response.ok && response.headers.get(\"content-length\") !== \"0\" ? {options.ErrorExpression} as {errorType} : undefined",
                        Environment.NewLine,
                        "    };");
                }
            }

            string NewLine(string? input, int ident) =>
                input is null ? "" : string.Concat(Environment.NewLine, string.Concat(Enumerable.Repeat("    ", ident)), input);

            Dictionary<string, string> headersDict = [];
            if (json)
            {
                headersDict.Add("Content-Type", "\"application/json\"");
            }
            if (eventsStreamingEnabled && _npgsqlRestoptions?.ExecutionIdHeaderName is not null)
            {
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

            var body = endpoint.RequestParamType == RequestParamType.BodyJson && requestName is not null ?
                @"body: JSON.stringify(request)" : null;

            // For path parameters, we need to exclude them from the query string and from the body
            var hasPathParams = endpoint.HasPathParameters;
            var pathParamCount = endpoint.PathParameters?.Length ?? 0;
            var bodyParamCount = bodyParameterName is not null ? 1 : 0;
            var queryParamCount = paramCount - pathParamCount - bodyParamCount;

            string qs;
            if (endpoint.RequestParamType == RequestParamType.QueryString && requestName is not null && queryParamCount > 0)
            {
                if (hasPathParams || bodyParameterName is not null)
                {
                    // Exclude path parameters and body parameter from query string
                    var exclusion = CreatePathParamExclusionExpression(
                        endpoint.PathParameters ?? [],
                        bodyParameterName);
                    qs = $" + parseQuery(({exclusion})";
                }
                else
                {
                    qs = " + parseQuery(request)";
                }
            }
            else
            {
                qs = "";
            }

            string? parameters = null;
            List<(string name, string type, string desc)> paramComments = new();

            if (requestName is not null) 
            {
                if (!string.IsNullOrEmpty(parameters))
                {
                    parameters = string.Concat(parameters, ",", Environment.NewLine);
                }
                else
                {
                    parameters = string.Concat(parameters, Environment.NewLine);
                }
                
                if (!options.SkipTypes)
                {
                    parameters = string.Concat(parameters, "    request: ", requestName);
                }
                else
                {
                    parameters = string.Concat(parameters, "    request");
                }
                paramComments.Add(("request", requestDesc, "Object containing request parameters."));
            }
            if (eventsStreamingEnabled)
            {
                if (!string.IsNullOrEmpty(parameters))
                {
                    parameters = string.Concat(parameters, ",", Environment.NewLine);
                }
                else
                {
                    parameters = string.Concat(parameters, Environment.NewLine);
                }
                if (!options.SkipTypes)
                {
                    parameters = string.Concat(parameters, "    onMessage?: (message: string) => void");
                }
                else
                {
                    parameters = string.Concat(parameters, "    onMessage");
                }
                paramComments.Add(("onMessage", "(message: string) => void", "Optional callback function to handle incoming SSE messages."));
                parameters = string.Concat(parameters, ",", Environment.NewLine);
                if (!options.SkipTypes)
                {
                    parameters = string.Concat(parameters, "    id: string | undefined = undefined");
                }
                else
                {
                    parameters = string.Concat(parameters, "    id = undefined");
                }
                paramComments.Add(("id", "string | undefined", "Optional execution ID for SSE connection. When supplied, only EventSource object with this ID in query string will will receive events."));
                
                parameters = string.Concat(parameters, ",", Environment.NewLine);
                parameters = string.Concat(parameters, "    closeAfterMs = 1000");
                paramComments.Add(("closeAfterMs", "number", "Time in milliseconds to wait before closing the EventSource connection. Used only when onMessage callback is provided."));
                parameters = string.Concat(parameters, ",", Environment.NewLine);
                if (!options.SkipTypes)
                {
                    parameters = string.Concat(parameters, "    awaitConnectionMs: number | undefined = 0");
                }
                else
                {
                    parameters = string.Concat(parameters, "    awaitConnectionMs = 0");
                }
                paramComments.Add(("awaitConnectionMs", "number", "Time in milliseconds to wait after opening the EventSource connection before sending the request. Used only when onMessage callback is provided."));
            }
            if (includeParseUrlParam)
            {
                if (!string.IsNullOrEmpty(parameters))
                {
                    parameters = string.Concat(parameters, ",", Environment.NewLine);
                }
                else
                {
                    parameters = string.Concat(parameters, Environment.NewLine);
                }
                if (!options.SkipTypes)
                {
                    parameters = string.Concat(parameters, "    parseUrl: (url: string) => string = url => url");
                }
                else
                {
                    parameters = string.Concat(parameters, "    parseUrl = url => url");
                }
                paramComments.Add(("parseUrl", "(url: string) => string", "Optional function to parse constructed URL before making the request."));
            }
            if (includeParseRequestParam)
            {
                if (!string.IsNullOrEmpty(parameters))
                {
                    parameters = string.Concat(parameters, ",", Environment.NewLine);
                }
                else
                {
                    parameters = string.Concat(parameters, Environment.NewLine);
                }
                if (!options.SkipTypes)
                {
                    parameters = string.Concat(parameters, "    parseRequest: (request: RequestInit) => RequestInit = request => request");
                }
                else
                {
                    parameters = string.Concat(parameters, "    parseRequest = request => request");
                }
                paramComments.Add(("parseRequest", "(request: RequestInit) => RequestInit", "Optional function to parse constructed request before making the request."));
            }
            if (!string.IsNullOrEmpty(parameters))
            {
                parameters = string.Concat(parameters, Environment.NewLine);
            }

            string url;
            // Convert path for template literals if it has path parameters
            var pathForUrl = hasPathParams ? ConvertPathToTemplateLiteral(endpoint.Path) : endpoint.Path;

            if (!options.ExportUrls)
            {
                if (hasPathParams)
                {
                    // Use template literal for path parameters
                    url = includeParseUrlParam ?
                        string.Format("parseUrl(`${{baseUrl}}{0}`{1})", pathForUrl, qs) :
                        string.Format("`${{baseUrl}}{0}`{1}", pathForUrl, qs);
                }
                else
                {
                    url = includeParseUrlParam ?
                        string.Format("parseUrl(baseUrl + \"{0}\"{1})", endpoint.Path, qs) :
                        string.Format("baseUrl + \"{0}\"{1}", endpoint.Path, qs);
                }
            }
            else
            {
                url = includeParseUrlParam ?
                    (requestName is not null && body is null ? string.Format("parseUrl({0}Url(request))", camel) : string.Format("parseUrl({0}Url())", camel)) :
                    (requestName is not null && body is null ? string.Format("{0}Url(request)", camel) : string.Format("{0}Url()", camel));

                if (!options.SkipTypes)
                {
                    if (hasPathParams)
                    {
                        contentHeader.AppendLine(string.Format(
                            "export const {0}Url = {1} => `${{baseUrl}}{2}`{3};",
                            camel,
                            requestName is not null ? string.Format("(request: {0})", requestName) : "()",
                            pathForUrl,
                            qs));
                    }
                    else
                    {
                        contentHeader.AppendLine(string.Format(
                            "export const {0}Url = {1} => baseUrl + \"{2}\"{3};",
                            camel,
                            requestName is not null && body is null ? string.Format("(request: {0})", requestName) : "()",
                            endpoint.Path,
                            qs));
                    }
                }
                else
                {
                    if (hasPathParams)
                    {
                        contentHeader.AppendLine(string.Format(
                            "export const {0}Url = {1} => `${{baseUrl}}{2}`{3};",
                            camel,
                            requestName is not null ? "request" : "()",
                            pathForUrl,
                            qs));
                    }
                    else
                    {
                        contentHeader.AppendLine(string.Format(
                            "export const {0}Url = {1} => baseUrl + \"{2}\"{3};",
                            camel,
                            requestName is not null && body is null ? "request" : "()",
                            endpoint.Path,
                            qs));
                    }
                }
            }
            string? createEventSourceFunc = null;
            if (eventsStreamingEnabled)
            {
                createEventSourceFunc = string.Concat("create", pascal, "EventSource");
                contentHeader.AppendLine(string.Format(
                    "{0}const {1} = {2} => new EventSource(baseUrl + \"{3}?\" + id);",
                    options.ExportEventSources ? "export " : "", //0
                    createEventSourceFunc, //1
                    options.SkipTypes ? "(id = \"\")" : "(id: string = \"\")", //2
                    endpoint.SseEventsPath)); //3
            }

            if (body is null && bodyParameterName is not null)
            {
                body = $"body: request.{bodyParameterName}";
            }

            string resultType;
            if (string.Equals(responseName, "void", StringComparison.OrdinalIgnoreCase))
            {
                resultType = includeStatusCode ?
                    string.Concat("{status: number, error: ", options.ErrorType, "}") :
                    responseName;
            }
            else
            {
                resultType = includeStatusCode ?
                    string.Concat("{status: number, response: ", responseName, ", error: ", options.ErrorType, "}") :
                    responseName;
            }

            if (endpoint.Upload)
            {
                string onloadExp;
                if (!isVoid)
                {
                    var uploadErrorType = options.ErrorType.EndsWith(" | undefined")
                        ? options.ErrorType[..^12]  // Remove " | undefined" suffix for inline use
                        : options.ErrorType;
                    if (includeStatusCode)
                    {
                        if (!options.SkipTypes)
                        {
                            onloadExp = $$"""
                                    xhr.onload = function () {
                                        if (this.status >= 200 && this.status < 300) {
                                            resolve({status: this.status, response: JSON.parse(this.responseText) as {{responseName}}, error: undefined});
                                        } else {
                                            resolve({status: this.status, response: [], error: JSON.parse(this.responseText) as {{uploadErrorType}}});
                                        }
                                    };
                            """;
                        }
                        else
                        {
                            onloadExp =
                                """
                                    xhr.onload = function () {
                                        if (this.status >= 200 && this.status < 300) {
                                            resolve({status: this.status, response: JSON.parse(this.responseText), error: undefined});
                                        } else {
                                            resolve({status: this.status, response: [], error: JSON.parse(this.responseText)});
                                        }
                                    };
                            """;
                        }
                    }
                    else
                    {
                        if (!options.SkipTypes)
                        {
                            onloadExp = $$"""
                                    xhr.onload = function () {
                                        if (this.status >= 200 && this.status < 300) {
                                            resolve(JSON.parse(this.responseText) as {{responseName}});
                                        } else {
                                            throw new Error(this.responseText);
                                        }
                                    };
                            """;
                        }
                        else
                        {
                            onloadExp =
                                """
                                    xhr.onload = function () {
                                        if (this.status >= 200 && this.status < 300) {
                                            resolve(JSON.parse(this.responseText));
                                        } else {
                                            throw new Error(this.responseText);
                                        }
                                    };
                            """;
                        }
                    }
                }
                else
                {
                    onloadExp =
                    """
                            xhr.onload = function () {
                                resolve(this.status);
                            };
                    """;
                }
                string sendExp;
                if (eventsStreamingEnabled)
                {
                    sendExp = string.Format(
                    """
                            let eventSource: EventSource;
                            if (onMessage) {{
                                eventSource = {0}(executionId);
                                eventSource.onmessage = {1} => {{
                                    onMessage(event.data);
                                }};
                                if (awaitConnectionMs !== undefined) {{
                                    await new Promise(resolve => setTimeout(resolve, awaitConnectionMs));
                                }}
                            }}
                            try {{
                                xhr.send(formData);
                            }}
                            finally {{
                                if (onMessage) {{
                                    setTimeout(() => eventSource.close(), closeAfterMs);
                                }}
                            }}
                    """,
                    createEventSourceFunc, // 0
                    options.SkipTypes ? "event" : "(event: MessageEvent)"); // 1;
                }
                else
                {
                    sendExp =
                    """
                            xhr.send(formData);
                    """;
                }
                string? headers = null;
                if (headersDict.Count > 0)
                {
                    headers = string.Join("", headersDict.Select(h => NewLine($"xhr.setRequestHeader({Quote(h.Key)}, {h.Value});", 2)));
                }
                if (!options.SkipTypes)
                {
                    //returnExp = $"{{status: this.status, response: JSON.parse(this.responseText) as {responseName}}}";
                    parameters = (parameters ?? "").Trim('\n', '\r').Replace("RequestInit", "XMLHttpRequest");
                    content.AppendLine(string.Format(
                    """
                    /**
                    {0}
                    */
                    export async function {1}(
                        files: FileList | null,
                    {2},
                        progress?: (loaded: number, total: number) => void,{3}
                    ): Promise<{4}> {{
                        return new Promise((resolve, reject) => {{
                            if (!files || files.length === 0) {{
                                reject(new Error("No files to upload"));
                                return;
                            }}
                            var xhr = new XMLHttpRequest();{11}
                            if (progress) {{
                                xhr.upload.addEventListener(
                                    "progress",
                                    (event) => {{
                                        if (event.lengthComputable && progress) {{
                                            progress(event.loaded, event.total);
                                        }}
                                    }},
                                    false
                                );
                            }}{5}
                            xhr.onerror = function () {{
                                reject({{
                                    xhr: this, 
                                    status: this.status,
                                    statusText: this.statusText || 'Network error occurred',
                                    response: this.response
                                }});
                            }};
                            xhr.open("POST", {6});{10}{7}
                            const formData = new FormData();
                            for(let i = 0; i < files.length; i++) {{
                                const file = files[i];
                                formData.append("file", file, file.name);
                            }}{8}{9}
                        }});
                    }}
                    """,
                        GetComment(routine, resultType, paramComments),//0
                        camel,//1
                        parameters,//2
                        options.XsrfTokenHeaderName is not null ?
                        string.Format("""
                        {0}    xsrfToken?: string
                        """, Environment.NewLine) : "", //3
                        resultType,//4
                        NewLine(onloadExp, 0),//5
                        url,//6
                        options.XsrfTokenHeaderName is not null ?
                        NewLine(string.Format("""
                                if (xsrfToken) {{
                                    xhr.setRequestHeader("{0}", xsrfToken);
                                }}
                        """, options.XsrfTokenHeaderName), 0) : "", //7
                        includeParseRequestParam ? NewLine(
                        """
                                if (parseRequest) {
                                    const modifiedXhr = parseRequest(xhr);
                                    if (modifiedXhr instanceof XMLHttpRequest) {
                                        xhr = modifiedXhr;
                                    } else {
                                        console.warn('parseRequest did not return an XMLHttpRequest object');
                                    }
                                }
                        """, 0) : "", //8
                        NewLine(sendExp, 0), //9
                        headers, //10
                        eventsStreamingEnabled ? NewLine("const executionId = id ? id : window.crypto.randomUUID();", 2) : "" //11
                     ));
                }
                else
                {
                    resultType = $"{{status: number, response: object[], error: {options.ErrorType}}}";
                    //returnExp = "{status: this.status, response: JSON.parse(this.responseText)}";
                    parameters = (parameters ?? "").Trim('\n', '\r');
                    content.AppendLine(string.Format(
                    """
                    /**
                    {0}
                    */
                    export async function {1}(
                        files,
                    {2},
                        progress,{3}
                    ) {{
                        return new Promise((resolve, reject) => {{
                            if (!files || files.length === 0) {{
                                reject(new Error("No files to upload"));
                                return;
                            }}
                            var xhr = new XMLHttpRequest();{10}
                            if (progress) {{
                                xhr.upload.addEventListener(
                                    "progress",
                                    (event) => {{
                                        if (event.lengthComputable && progress) {{
                                            progress(event.loaded, event.total);
                                        }}
                                    }},
                                    false
                                );
                            }}{4}
                            xhr.onerror = function () {{
                                reject({{
                                    xhr: this, 
                                    status: this.status,
                                    statusText: this.statusText || 'Network error occurred',
                                    response: this.response
                                }});
                            }};
                            xhr.open("POST", {5});{9}{6}
                            const formData = new FormData();
                            for(let i = 0; i < files.length; i++) {{
                                const file = files[i];
                                formData.append("file", file, file.name);
                            }}{7}{8}
                        }});
                    }}
                    """,
                        GetComment(routine, resultType, paramComments),//0
                        camel,//1
                        parameters,//2
                        options.XsrfTokenHeaderName is not null ?
                        string.Format("""
                        {0}    xsrfToken
                        """, Environment.NewLine) : "", //3
                        NewLine(onloadExp, 0),//4
                        url,//5
                        options.XsrfTokenHeaderName is not null ?
                        NewLine(string.Format("""
                                if (xsrfToken) {{
                                    xhr.setRequestHeader("{0}", xsrfToken);
                                }}
                        """, options.XsrfTokenHeaderName), 0) : "", //6
                        includeParseRequestParam ? NewLine(
                        """
                                if (parseRequest) {
                                    const modifiedXhr = parseRequest(xhr);
                                    if (modifiedXhr instanceof XMLHttpRequest) {
                                        xhr = modifiedXhr;
                                    } else {
                                        console.warn('parseRequest did not return an XMLHttpRequest object');
                                    }
                                }
                        """, 0) : "", //7
                        NewLine(sendExp, 0),//8
                        headers, //9
                        eventsStreamingEnabled ? NewLine("const executionId = id ? id : window.crypto.randomUUID();", 2) : "" //10
                     ));

                }
            }
            else
            {
                string funcBody;
                string? headers = null;
                if (eventsStreamingEnabled)
                {
                    if (headersDict.Count > 0)
                    {
                        headers = "headers: {" + string.Join(", ", headersDict.Select(h => NewLine($"{Quote(h.Key)}: {h.Value}", 4))) + NewLine("},", 3);
                    }
                    funcBody = string.Format(
                    """
                    const executionId = id ? id : window.crypto.randomUUID();
                    let eventSource: EventSource;
                    if (onMessage) {{
                        eventSource = {0}(executionId);
                        eventSource.onmessage = {1} => {{
                            onMessage(event.data);
                        }};
                        if (awaitConnectionMs !== undefined) {{
                            await new Promise(resolve => setTimeout(resolve, awaitConnectionMs));
                        }}
                    }}
                    try {{
                        {2}await fetch({3}, {4}{{
                            method: "{5}",{6}{7}
                        }}{8});{9}
                    }}
                    finally {{
                        if (onMessage) {{
                            setTimeout(() => eventSource.close(), closeAfterMs);
                        }}
                    }}
                """,
                    createEventSourceFunc, // 0
                    options.SkipTypes ? "event" : "(event: MessageEvent)", // 1
                    isVoid && !includeStatusCode ? "" : "const response = ",//2
                    url,//3
                    includeParseRequestParam ? "parseRequest(" : "",//4
                    endpoint.Method,//5
                    NewLine(headers, 3),//6
                    NewLine(body, 3),//7
                    includeParseRequestParam ? ")" : "",//8
                    NewLine(
                        returnExp?.Replace(
                                string.Concat(Environment.NewLine, "    "), 
                                string.Concat(Environment.NewLine, string.Concat(Enumerable.Repeat("    ", 2)))
                                ), 2));//9
                }
                else
                {
                    if (headersDict.Count > 0)
                    {
                        headers = "headers: {" + string.Join(", ", headersDict.Select(h => NewLine($"{Quote(h.Key)}: {h.Value}", 3))) + NewLine("},", 2);
                    }
                    funcBody = string.Format(
                    """
                    {0}await fetch({1}, {2}{{
                        method: "{3}",{4}{5}
                    }}{6});{7}
                """,
                    isVoid && !includeStatusCode ? "" : "const response = ",//0
                    url,//1
                    includeParseRequestParam ? "parseRequest(" : "",//2
                    endpoint.Method,//3
                    NewLine(headers, 2),//4
                    NewLine(body, 2),//5
                    includeParseRequestParam ? ")" : "",//6
                    NewLine(returnExp, 1));//7
                }

                if (!options.SkipTypes)
                {
                    content.AppendLine(string.Format(
                        """
                /**
                {0}
                */
                export async function {1}({2}) : Promise<{3}> {{
                {4}
                """,
                        GetComment(routine, resultType, paramComments), // 0
                        camel, // 1
                        parameters,  // 2
                        resultType,  // 3
                        funcBody));  // 4
                    content.AppendLine("}");
                }
                else
                {
                    content.AppendLine(string.Format(
                        """
                /**
                {0}
                */
                export async function {1}({2}) {{
                {3}
                """,
                        GetComment(routine, resultType, paramComments),
                        camel,
                        parameters,
                        funcBody));
                    content.AppendLine("}");
                }
            }
            return true;
        } // void Handle
    }

    //paramComments.Add(("parseRequest", "(request: RequestInit) => RequestInit", "Optional function to parse constructed request before making the request."));
    private string GetComment(Routine routine, string resultType, List<(string name, string type, string desc)>? paramComments)
    {
        StringBuilder sb = new();
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
                sb.AppendLine(string.Concat("* ", line.TrimEnd('\r')));
            }

            //sb.AppendLine("* ");
            //sb.AppendLine("* @remarks");
            //sb.AppendLine(string.Format("* {0} {1}", endpoint.Method, endpoint.Url));
            if (options.CommentHeaderIncludeComments && !string.IsNullOrEmpty(routine.Comment?.Trim()))
            {
                sb.AppendLine("* ");
                sb.AppendLine("* @remarks");
                var lines = routine
                    .Comment
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var (line, index) in lines.Select((l, i) => (l, i)))
                {
                    if (line == "\r" && index > 0)
                    {
                        continue;
                    }
                    var commentLine = line.Replace("'", "''").TrimEnd('\r');
                    if (index == 0)
                    {
                        commentLine = string.Concat($"comment on function {routine.Schema}.{routine.Name} is '", commentLine);
                    }
                    else if (index == lines.Length - 1)
                    {
                        commentLine = string.Concat(commentLine, "';");
                    }
                    sb.AppendLine(string.Concat("* ", commentLine));
                }
            }
        }
        else
        {
            //sb.AppendLine(string.Format("* {0} {1}", endpoint.Method, endpoint.Url));
        }
        sb.AppendLine("* ");

        foreach (var pc in paramComments ?? [])
        {
            if (options.SkipTypes)
            {
                sb.AppendLine(string.Concat("* @param {", pc.type, "} ", pc.name, " - ", pc.desc));
            }
            else
            {
                sb.AppendLine(string.Concat("* @param ", pc.name, " - ", pc.desc));
            }
        }
        // if (string.IsNullOrEmpty(parameters) is false)
        // {
        //     foreach(var p in parameters.Trim().Split(','))
        //     {
        //         var param = p.Trim();
        //         if (string.IsNullOrEmpty(param))
        //         {
        //             continue;
        //         }
        //         if (param.Contains(':'))
        //         {
        //             var parts = param.Split(':');
        //             var name = parts[0].Trim();
        //             var type = string.Join(':', parts[1..]).Trim();
        //             if (type.Contains(" = "))
        //             {
        //                 type = type.Split(" = ")[0].Trim();
        //             }
        //             sb.AppendLine(string.Concat("* @param {", type, "} ", name.Trim('?')));
        //         }
        //         else
        //         {
        //             if (param.Contains('='))
        //             {
        //                 param = param.Split('=')[0].Trim();
        //             }
        //             sb.AppendLine(string.Concat("* @param ", param.Trim('?')));
        //         }
        //     }
        // }
        
        if (!string.IsNullOrEmpty(resultType))
        {
            if (resultType.StartsWith('{') && resultType.EndsWith('}'))
            {
                sb.AppendLine(string.Concat("* @returns ", resultType));
            }
            else
            {
                sb.AppendLine(string.Concat("* @returns {", resultType, "}"));
            }
        }
        sb.AppendLine("* ");
        sb.Append(string.Format("* @see {0} {1}.{2}", routine.Type.ToString().ToUpperInvariant(), routine.Schema, routine.Name));
        return sb.ToString();
    }

    private string GetTsType(TypeDescriptor descriptor, bool useDateType)
    {
        var type = "string";
        
        if (useDateType && (descriptor.IsDate || descriptor.IsDateTime))
        {
            type = "Date";
        }
        else if (descriptor.IsNumeric)
        {
            type = "number";
        }
        else if (descriptor.IsBoolean)
        {
            type = "boolean";
        }
        else if (descriptor.IsJson)
        {
            type = options.DefaultJsonType;
        }

        if (descriptor.IsArray)
        {
            type = string.Concat(type, "[]");
        }
        return type;
    }

    private static readonly char[] separator = ['_', '-', '/', '\\'];

    public string SanitizeJavaScriptVariableName(string name)
    {
        // Replace invalid starting characters with underscore
        name = InvalidChars1().Replace(name, "_");

        // Replace any other invalid characters with underscore
        name = InvalidChars2().Replace(name, "_");

        return name;
    }

    public string QuoteJavaScriptVariableName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var invalidChars1 = InvalidChars1();
        var invalidChars2 = InvalidChars2();

        if (name.EndsWith('?'))
        {
            var part = name[..^1];
            if (invalidChars1.IsMatch(part) || invalidChars2.IsMatch(part))
            {
                return $"\"{part}\"?";
            }
            return name;
        }

        if (invalidChars1.IsMatch(name) || invalidChars2.IsMatch(name))
        {
            return $"\"{name}\"";
        }

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

    public string Quote(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }
        if (!( 
            (name.StartsWith('"') && name.StartsWith('"')) || 
            (name.StartsWith('\'') && name.StartsWith('\''))
            ))
        {
            return $"\"{name}\"";
        }
        return name;
    }

    [GeneratedRegex("^[^a-zA-Z_$]")]
    private static partial Regex InvalidChars1();
    [GeneratedRegex("[^a-zA-Z0-9_$]")]
    private static partial Regex InvalidChars2();
    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PathParamRegex();

    /// <summary>
    /// Converts a path with {param} placeholders to a JavaScript template literal.
    /// Example: "/products/{p_id}" => "/products/${request.p_id}"
    /// </summary>
    private static string ConvertPathToTemplateLiteral(string path)
    {
        return PathParamRegex().Replace(path, @"${request.$1}");
    }

    /// <summary>
    /// Creates an exclusion expression for path parameters in parseQuery.
    /// Example: ["p_id", "review_id"] => "(({ [\"p_id\"]: _1, [\"review_id\"]: _2, ...rest }) => rest)(request)"
    /// </summary>
    private static string CreatePathParamExclusionExpression(string[] pathParams, string? bodyParameterName)
    {
        var excludeParams = new List<string>(pathParams);
        if (bodyParameterName is not null)
        {
            excludeParams.Add(bodyParameterName);
        }

        var destructured = string.Join(", ", excludeParams.Select((p, i) => $"[\"{p}\"]: _{i + 1}"));
        return $"({{ {destructured}, ...rest }}) => rest)(request)";
    }

    /// <summary>
    /// Gets or creates a TypeScript interface for a composite type.
    /// Returns the interface name to use in type declarations.
    /// Handles nested composite types recursively.
    /// </summary>
    private string GetOrCreateCompositeInterface(
        string[] fieldNames,
        TypeDescriptor[] fieldDescriptors,
        string columnName,
        Dictionary<string, string> compositeTypeInterfaces,
        StringBuilder compositeInterfaces)
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

        if (compositeTypeInterfaces.TryGetValue(key, out var existingName))
        {
            return existingName;
        }

        // Generate interface name from column name
        var interfaceName = $"I{ConvertToPascalCase(columnName)}";

        // Ensure unique interface name
        var baseName = interfaceName;
        var counter = 1;
        while (compositeTypeInterfaces.ContainsValue(interfaceName))
        {
            interfaceName = $"{baseName}{counter++}";
        }

        // Register the interface name early to handle circular references
        compositeTypeInterfaces[key] = interfaceName;

        // Build the interface
        StringBuilder interfaceBuilder = new();
        interfaceBuilder.AppendLine($"interface {interfaceName} {{");

        for (var i = 0; i < fieldNames.Length; i++)
        {
            var fieldName = ConvertToCamelCase(fieldNames[i]);
            var descriptor = fieldDescriptors[i];
            string tsType;

            // Check if this field is a nested composite type
            if (descriptor.CompositeFieldNames != null && descriptor.CompositeFieldDescriptors != null)
            {
                // Recursively create interface for nested composite
                var nestedInterfaceName = GetOrCreateCompositeInterface(
                    descriptor.CompositeFieldNames,
                    descriptor.CompositeFieldDescriptors,
                    fieldNames[i],
                    compositeTypeInterfaces,
                    compositeInterfaces);
                tsType = nestedInterfaceName;
            }
            // Check if this field is an array of composite types
            else if (descriptor.ArrayCompositeFieldNames != null && descriptor.ArrayCompositeFieldDescriptors != null)
            {
                // Recursively create interface for the array element composite type
                var elementInterfaceName = GetOrCreateCompositeInterface(
                    descriptor.ArrayCompositeFieldNames,
                    descriptor.ArrayCompositeFieldDescriptors,
                    fieldNames[i],
                    compositeTypeInterfaces,
                    compositeInterfaces);
                tsType = $"{elementInterfaceName}[]";
            }
            else
            {
                tsType = GetTsType(descriptor, false);
            }

            interfaceBuilder.AppendLine($"    {fieldName}: {tsType} | null;");
        }

        interfaceBuilder.AppendLine("}");
        interfaceBuilder.AppendLine();

        compositeInterfaces.Append(interfaceBuilder);

        return interfaceName;
    }
}