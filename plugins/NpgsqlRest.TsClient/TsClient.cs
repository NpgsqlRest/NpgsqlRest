﻿using System.Text;
using System.Text.RegularExpressions;

namespace NpgsqlRest.TsClient;

public partial class TsClient(TsClientOptions options) : IEndpointCreateHandler
{
    private IApplicationBuilder _builder = default!;
    private ILogger? _logger;
    private NpgsqlRestOptions? _npgsqlRestoptions;

    private const string Module = "tsclient";
    private const string InfoEvents = "tsclient_events";
    private const string IncludeParseUrl = "tsclient_parse_url";
    private const string IncludeParseRequest = "tsclient_parse_request";
    private const string IncludeStatusCode = "tsclient_status_code";

    public TsClient(string filePath) : this(new TsClientOptions(filePath)) { }

    public TsClient(string filePath, 
        bool fileOverwrite = false, 
        bool includeHost = false, 
        string? customHost = null, 
        CommentHeader commentHeader = CommentHeader.Simple,
        bool includeStatusCode = false) : this(new TsClientOptions(filePath, fileOverwrite, includeHost, customHost, commentHeader, includeStatusCode)) { }

    public void Setup(IApplicationBuilder builder, ILogger? logger, NpgsqlRestOptions options)
    {
        if (builder is WebApplication app)
        {
            var factory = app.Services.GetRequiredService<ILoggerFactory>();
            if (factory is not null)
            {
                _logger = factory.CreateLogger(options.LoggerName ?? typeof(TsClient).Namespace ?? "NpgsqlRest.TsClient");
            }
            else
            {
                _logger = app.Logger;
            }
        }
        else
        {
            _logger = logger;
        }
        _builder = builder;
        _npgsqlRestoptions = options;
    }

    public void Cleanup(RoutineEndpoint[] endpoints)
    {
        if (options.FilePath is null)
        {
            return;
        }

        if (options.BySchema is false)
        {
            Run(endpoints, options.FilePath);
        }
        else
        {
            if (options.FilePath.Contains("{0}") is false)
            {
                _logger?.LogError("TsClient Option FilePath doesn't contain {{0}} formatter and BySchema options is true. Some files may be overwritten! Existing...");
                return;
            }

            foreach (var group in endpoints.GroupBy(e => e.Routine.Schema))
            {
                var filename = string.Format(options.FilePath, ConvertToCamelCase(group.Key));
                if (options.SkipTypes && filename.EndsWith(".ts"))
                {
                    filename = filename[..^3] + ".js";
                }
                Run([.. group], filename);
            }
        }
    }

    private void Run(RoutineEndpoint[] endpoints, string fileName)
    {
        if (fileName is null)
        {
            return;
        }

        RoutineEndpoint[] filtered = [.. endpoints.Where(e => e.CustomParameters.ParameterEnabled(Module) is not false)];

        Dictionary<string, string> modelsDict = [];
        Dictionary<string, int> names = [];
        StringBuilder contentHeader = new();
        StringBuilder content = new();
        StringBuilder interfaces = new();

        foreach (var import in options.CustomImports ?? [])
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
                .Where(e => e.RequestParamType == RequestParamType.QueryString && e.Routine.ParamCount > 0)
                .Any();

            if (haveParseQuery is true)
            {
                if (options.SkipTypes is false)
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
        if (options.ExportUrls is true)
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
            if (Handle(endpoint) && handled is false)
            {
                handled = true;
            }
        }

        foreach (var endpoint in filtered
            .Where(e => (e.Routine.Type == RoutineType.Table || e.Routine.Type == RoutineType.View) is false)
            .OrderBy(e => e.Routine.Schema)
            .ThenBy(e => e.Routine.Name))
        {
            if (Handle(endpoint) && handled is false)
            {
                handled = true;
            }
        }

        if (handled is false)
        {
            if (filtered.Length == 0 && options.FileOverwrite is true)
            {
                if (File.Exists(fileName))
                {
                    try
                    {
                        File.Delete(fileName);
                        _logger?.LogDebug("Deleted file: {fileName}", fileName);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Failed to delete file: {fileName}", fileName);

                        try
                        {
                            File.WriteAllText(fileName, "// No endpoints found.");
                        }
                        catch (Exception ex2)
                        {
                            _logger?.LogError(ex2, "Failed to empty file: {fileName}", fileName);
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
        if (dir is not null && Directory.Exists(dir) is false)
        {
            Directory.CreateDirectory(dir);
        }
        if (options.CreateSeparateTypeFile is false)
        {
            if (options.SkipTypes is false)
            {
                interfaces.AppendLine(content.ToString());
                if (contentHeader.Length > 0)
                {
                    contentHeader.AppendLine();
                    interfaces.Insert(0, contentHeader.ToString());
                }
                AddHeader(interfaces);
                File.WriteAllText(fileName, interfaces.ToString());
                _logger?.LogDebug("Created Typescript file: {fileName}", fileName);
            }
        }
        else
        {
            if (options.SkipTypes is false)
            {
                var typeFile = fileName.Replace(".ts", "Types.d.ts");
                AddHeader(interfaces);
                File.WriteAllText(typeFile, interfaces.ToString());
                _logger?.LogDebug("Created Typescript type file: {typeFile}", typeFile);
            }

            if (contentHeader.Length > 0)
            {
                content.Insert(0, contentHeader.ToString());
            }
            AddHeader(content);
            File.WriteAllText(fileName, content.ToString());
            if (options.SkipTypes is false)
            {
                _logger?.LogDebug("Created Typescript file: {fileName}", fileName);
            }
            else
            {
                _logger?.LogDebug("Created Javascript file: {fileName}", fileName);
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
            sb.Insert(0, string.Join(Environment.NewLine, options.HeaderLines.Select(l => string.Format(l, now))));
        }

        bool Handle(RoutineEndpoint endpoint)
        {
            Routine routine = endpoint.Routine;
            var eventsStreamingEnabled = endpoint.InfoEventsStreamingPath is not null;
            if (endpoint.CustomParameters.ParameterEnabled(InfoEvents) is false)
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
            if (options.SkipPaths.Contains(endpoint.Url))
            {
                return false;
            }

            string? name;
            try
            {
                if (options.UseRoutineNameInsteadOfEndpoint)
                {
                    name = string.Concat(routine.Schema, "/", routine.Name);
                }
                else
                {
                    if (string.IsNullOrEmpty(_npgsqlRestoptions?.UrlPathPrefix) || _npgsqlRestoptions.UrlPathPrefix.Length > endpoint.Url.Length)
                    {
                        name = endpoint.Url;
                    }
                    else
                    {
                        name = endpoint.Url[_npgsqlRestoptions.UrlPathPrefix.Length..];
                    }
                }
            }
            catch
            {
                name = string.Concat(routine.Schema, "/", routine.Name);
            }
            if (name.Length < 3)
            {
                name = string.Concat(routine.Schema, "/", routine.Name);
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

            if (endpoint.Login is true)
            {
                isVoid = false;
                returnsSet = false;
                columnCount = 1;
                returnsRecordType = false;
                columnsTypeDescriptor = [new TypeDescriptor("text")];
            }

            if (endpoint.Logout is true)
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
                var nameSuffix = descriptor.HasDefault ? "?" : "";
                paramNames[i] = QuoteJavaScriptVariableName($"{parameter.ConvertedName}{nameSuffix}");
                if (string.Equals(endpoint.BodyParameterName, parameter.ConvertedName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(endpoint.BodyParameterName, parameter.ActualName, StringComparison.OrdinalIgnoreCase))
                {
                    bodyParameterName = paramNames[i];
                }
            }
            if (paramCount > 0)
            {
                StringBuilder req = new();
                requestName = $"I{pascal}Request";

                for (var i = 0; i < paramCount; i++)
                {
                    var descriptor = routine.Parameters[i].TypeDescriptor;
                    var type = GetTsType(descriptor, false);
                    req.AppendLine($"    {paramNames[i]}: {type} | null;");
                }

                if (modelsDict.TryGetValue(req.ToString(), out var newName))
                {
                    requestName = newName;
                }
                else
                {
                    if (options.SkipTypes is false)
                    {
                        if (options.UniqueModels is true)
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
                    //return string.Concat("return {status: response.status, response: ", responseExp, "};");
                    return string.Concat(
                        "return {", 
                        Environment.NewLine,
                        "        status: response.status,",
                        Environment.NewLine,
                        "        response: ", 
                        (responseExp == "await response.text()" ? responseExp : string.Concat("response.status == 200 ? ", responseExp, " : await response.text()")),
                        Environment.NewLine,
                        "    };");
                }
                return string.Concat("return ", responseExp, ";");
            }

            if (isVoid is false)
            {
                if (endpoint.Upload is true)
                {
                    responseName = $"I{pascal}Response";
                    StringBuilder resp = new();

                    resp.AppendLine($"interface {responseName} {{");
                    resp.AppendLine("      type: string;");
                    resp.AppendLine("      fileName: string;");
                    resp.AppendLine("      contentType: string;");
                    resp.AppendLine("      size: number;");
                    resp.AppendLine("      success: boolean;");
                    resp.AppendLine("      status: string;");
                    resp.AppendLine("      [key: string]: string | number | boolean;");
                    resp.AppendLine("}");
                    resp.AppendLine();
                    interfaces.Append(resp);
                    responseName = string.Concat(responseName, "[]");
                }
                else if (returnsSet == false && columnCount == 1 && returnsRecordType is false)
                {
                    var descriptor = columnsTypeDescriptor[0];
                    responseName = GetTsType(descriptor, true);
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

                        for (var i = 0; i < columnCount; i++)
                        {
                            var descriptor = columnsTypeDescriptor[i];
                            var type = GetTsType(descriptor, false);

                            resp.AppendLine($"    {routine.ColumnNames[i]}: {type} | null;");
                        }

                        if (modelsDict.TryGetValue(resp.ToString(), out var newName))
                        {
                            responseName = newName;
                        }
                        else
                        {
                            if (options.SkipTypes is false)
                            {
                                if (options.UniqueModels is true)
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
                    if (options.SkipTypes is false)
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
                    returnExp = "return response.status;";
                }
            }

            string NewLine(string? input, int ident) =>
                input is null ? "" : string.Concat(Environment.NewLine, string.Concat(Enumerable.Repeat("    ", ident)), input);

            Dictionary<string, string> headersDict = [];
            if (json)
            {
                headersDict.Add("Content-Type", "\"application/json\"");
            }
            if (eventsStreamingEnabled is true && _npgsqlRestoptions?.ExecutionIdHeaderName is not null)
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

            var qs = endpoint.RequestParamType == RequestParamType.QueryString && requestName is not null ?
                (bodyParameterName is null ? " + parseQuery(request)" : 
                    $" + parseQuery((({{ [\"{bodyParameterName}\"]: _, ...rest }}) => rest)(request))" ) : 
                "";

            string? parameters = null;

            if (requestName is not null) 
            {
                if (string.IsNullOrEmpty(parameters) is false)
                {
                    parameters = string.Concat(parameters, ",", Environment.NewLine);
                }
                else
                {
                    parameters = string.Concat(parameters, Environment.NewLine);
                }
                if (options.SkipTypes is false)
                {
                    parameters = string.Concat(parameters, "    request: ", requestName);
                }
                else
                {
                    parameters = string.Concat(parameters, "    request");
                }
            }
            if (eventsStreamingEnabled)
            {
                if (string.IsNullOrEmpty(parameters) is false)
                {
                    parameters = string.Concat(parameters, ",", Environment.NewLine);
                }
                else
                {
                    parameters = string.Concat(parameters, Environment.NewLine);
                }
                if (options.SkipTypes is false)
                {
                    parameters = string.Concat(parameters, "    info: (message: string) => void = msg => msg");
                }
                else
                {
                    parameters = string.Concat(parameters, "    info = msg => msg");
                }
            }
            if (includeParseUrlParam is true)
            {
                if (string.IsNullOrEmpty(parameters) is false)
                {
                    parameters = string.Concat(parameters, ",", Environment.NewLine);
                }
                else
                {
                    parameters = string.Concat(parameters, Environment.NewLine);
                }
                if (options.SkipTypes is false)
                {
                    parameters = string.Concat(parameters, "    parseUrl: (url: string) => string = url => url");
                }
                else
                {
                    parameters = string.Concat(parameters, "    parseUrl = url => url");
                }
            }
            if (includeParseRequestParam is true)
            {
                if (string.IsNullOrEmpty(parameters) is false)
                {
                    parameters = string.Concat(parameters, ",", Environment.NewLine);
                }
                else
                {
                    parameters = string.Concat(parameters, Environment.NewLine);
                }
                if (options.SkipTypes is false)
                {
                    parameters = string.Concat(parameters, "    parseRequest: (request: RequestInit) => RequestInit = request => request");
                }
                else
                {
                    parameters = string.Concat(parameters, "    parseRequest = request => request");
                }
            }
            if (string.IsNullOrEmpty(parameters) is false)
            {
                parameters = string.Concat(parameters, Environment.NewLine);
            }

            string url;
            if (options.ExportUrls is false)
            {
                url = includeParseUrlParam is true ?
                    string.Format("parseUrl(baseUrl + \"{0}\"{1})", endpoint.Url, qs) :
                    string.Format("baseUrl + \"{0}\"{1}", endpoint.Url, qs);
            }
            else
            {
                url = includeParseUrlParam is true ?
                    (requestName is not null && body is null ? string.Format("parseUrl({0}Url(request))", camel) : string.Format("parseUrl({0}Url())", camel)) :
                    (requestName is not null && body is null ? string.Format("{0}Url(request)", camel) : string.Format("{0}Url()", camel));

                if (options.SkipTypes is false)
                {
                    contentHeader.AppendLine(string.Format(
                        "export const {0}Url = {1} => baseUrl + \"{2}\"{3};",
                        camel,
                        requestName is not null && body is null ? string.Format("(request: {0})", requestName) : "()",
                        endpoint.Url,
                        qs));
                }
                else
                {
                    contentHeader.AppendLine(string.Format(
                        "export const {0}Url = {1} => baseUrl + \"{2}\"{3};",
                        camel,
                        requestName is not null && body is null ? "request" : "()",
                        endpoint.Url,
                        qs));
                }
            }
            string? createEventSourceFunc = null;
            if (eventsStreamingEnabled)
            {
                createEventSourceFunc = string.Concat("create", pascal, "EventSource");
                contentHeader.AppendLine(string.Format(
                    "{0}const {1} = {2} => new EventSource(baseUrl + \"{3}?\" + id);",
                    options.ExportEventSources is true ? "export " : "", //0
                    createEventSourceFunc, //1
                    options.SkipTypes ? "(id = \"\")" : "(id: string = \"\")", //2
                    endpoint.InfoEventsStreamingPath)); //3
            }

            if (body is null && bodyParameterName is not null)
            {
                body = $"body: request.{bodyParameterName}";
            }

            string resultType;
            if (string.Equals(responseName, "void", StringComparison.OrdinalIgnoreCase))
            {
                resultType = includeStatusCode ?
                    "number" :
                    responseName;
            }
            else
            {
                resultType = includeStatusCode ?
                    string.Concat("{status: number, response: ", responseName, (responseName == "string" ? "" : " | string"), "}") :
                    responseName;
            }

            if (endpoint.Upload is true)
            {
                string onloadExp;
                if (isVoid is false)
                {
                    onloadExp =string.Format(
                        """
                            xhr.onload = function () {{
                                if (this.status >= 200 && this.status < 300) {{
                                    resolve({0});
                                }} else {{
                                    resolve({{status: this.status, response: this.response}});
                                }}
                            }};
                    """, returnExp);
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
                            const eventSource = {0}(executionId);
                            eventSource.onmessage = {1} => {{
                                info(event.data);
                            }};
                            try {{
                                xhr.send(formData);
                            }}
                            finally {{
                                setTimeout(() => eventSource.close(), 1000);
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
                if (options.SkipTypes is false)
                {
                    returnExp = $"{{status: this.status, response: JSON.parse(this.responseText) as {responseName}}}";
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
                        GetComment(routine, parameters, resultType),//0
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
                        includeParseRequestParam is true ? NewLine(
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
                        eventsStreamingEnabled ? NewLine("const executionId = window.crypto.randomUUID();", 2) : "" //11
                     ));
                }
                else
                {
                    resultType = "{status: number, response: object[] | string}";
                    returnExp = "{status: this.status, response: JSON.parse(this.responseText)}";
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
                        GetComment(routine, parameters, resultType),//0
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
                        includeParseRequestParam is true ? NewLine(
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
                        eventsStreamingEnabled ? NewLine("const executionId = window.crypto.randomUUID();", 2) : "" //10
                     ));

                }
            }
            else
            {
                string funcBody;
                string? headers = null;
                if (eventsStreamingEnabled is true)
                {
                    if (headersDict.Count > 0)
                    {
                        headers = "headers: {" + string.Join(", ", headersDict.Select(h => NewLine($"{Quote(h.Key)}: {h.Value}", 4))) + NewLine("},", 3);
                    }
                    funcBody = string.Format(
                    """
                    const executionId = window.crypto.randomUUID();
                    const eventSource = {0}(executionId);
                    eventSource.onmessage = {1} => {{
                        info(event.data);
                    }};
                    try {{
                        {2}await fetch({3}, {4}{{
                            method: "{5}",{6}{7}
                        }}{8});{9}
                    }}
                    finally {{
                        setTimeout(() => eventSource.close(), 1000);
                    }}
                """,
                    createEventSourceFunc, // 0
                    options.SkipTypes ? "event" : "(event: MessageEvent)", // 1
                    isVoid && includeStatusCode is false ? "" : "const response = ",//2
                    url,//3
                    includeParseRequestParam ? "parseRequest(" : "",//4
                    endpoint.Method,//5
                    NewLine(headers, 3),//6
                    NewLine(body, 3),//7
                    includeParseRequestParam ? ")" : "",//8
                    NewLine(returnExp, 2));//9
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
                    isVoid && includeStatusCode is false ? "" : "const response = ",//0
                    url,//1
                    includeParseRequestParam ? "parseRequest(" : "",//2
                    endpoint.Method,//3
                    NewLine(headers, 2),//4
                    NewLine(body, 2),//5
                    includeParseRequestParam ? ")" : "",//6
                    NewLine(returnExp, 1));//7
                }

                if (options.SkipTypes is false)
                {
                    content.AppendLine(string.Format(
                        """
                /**
                {0}
                */
                export async function {1}({2}) : Promise<{3}> {{
                {4}
                """,
                        GetComment(routine, parameters ?? "", resultType), // 0
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
                        GetComment(routine, parameters ?? "", resultType),
                        camel,
                        parameters,
                        funcBody));
                    content.AppendLine("}");
                }
            }
            return true;
        } // void Handle
    }

    private string GetComment(Routine routine, string parameters, string resultType)
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
            if (options.CommentHeaderIncludeComments is true && string.IsNullOrEmpty(routine.Comment?.Trim()) is false)
            {
                sb.AppendLine("* ");
                sb.AppendLine("* @remarks");
                var lines = routine
                    .Comment
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .ToArray();
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
        if (string.IsNullOrEmpty(parameters) is false)
        {
            foreach(var p in parameters.Trim().Split(','))
            {
                var param = p.Trim();
                if (string.IsNullOrEmpty(param))
                {
                    continue;
                }
                if (param.Contains(':'))
                {
                    var parts = param.Split(':');
                    var name = parts[0].Trim();
                    var type = string.Join(':', parts[1..]).Trim();
                    if (type.Contains(" = "))
                    {
                        type = type.Split(" = ")[0].Trim();
                    }
                    sb.AppendLine(string.Concat("* @param {", type, "} ", name));
                }
                else
                {
                    sb.AppendLine(string.Concat("* @param ", param));
                }
            }
        }
        if (string.IsNullOrEmpty(resultType) is false)
        {
            sb.AppendLine(string.Concat("* @returns {", resultType, "}"));
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
            .Select((s, i) =>
                string.Concat(i == 0 ? char.ToUpperInvariant(s[0]) : char.ToUpperInvariant(s[0]), s[1..]))
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
        if (options.IncludeHost is false)
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
                var section = app.Configuration?.GetSection("ASPNETCORE_URLS");
                if (section?.Value is not null)
                {
                    host = section.Value.Split(";")?.LastOrDefault();
                }
                if (host is null && app.Configuration?["urls"] is not null)
                {
                    host = app.Configuration?["urls"];
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
}