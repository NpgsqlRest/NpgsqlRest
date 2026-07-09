using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlRest.Common;
using static NpgsqlRest.NpgsqlRestOptions;

namespace NpgsqlRest.OpenAPI;

[JsonSerializable(typeof(JsonObject))]
internal partial class OpenApiSerializerContext : JsonSerializerContext;

/// <summary>
/// Exact "openapi" field values emitted for each supported OpenApiOptions.SpecVersion setting.
/// Public so consumers and tests can reference the emitted versions instead of string literals.
/// </summary>
public static class OpenApiSpecVersions
{
    public const string V30 = "3.0.3";
    public const string V31 = "3.1.1";
}

public class OpenApi(OpenApiOptions openApiOptions) : IEndpointCreateHandler
{
    public OpenApi() : this(new OpenApiOptions()) { }

    // Resolved in the constructor so an invalid SpecVersion fails fast at startup,
    // when the handler is built - not silently at generation time.
    private readonly string _specVersion = ResolveSpecVersion(openApiOptions.SpecVersion);

    internal static string ResolveSpecVersion(string? specVersion)
    {
        var value = specVersion?.Trim();
        if (string.Equals(value, "3.0", StringComparison.OrdinalIgnoreCase))
        {
            return OpenApiSpecVersions.V30;
        }
        if (string.Equals(value, "3.1", StringComparison.OrdinalIgnoreCase))
        {
            return OpenApiSpecVersions.V31;
        }
        throw new ArgumentException(
            $"Invalid OpenApiOptions.SpecVersion value '{specVersion}'. " +
            $"Valid values are \"3.0\" (emits openapi: {OpenApiSpecVersions.V30}) and \"3.1\" (emits openapi: {OpenApiSpecVersions.V31}).");
    }

    private IApplicationBuilder _builder = default!;
    private JsonObject _document = default!;
    private JsonObject _paths = default!;
    private JsonObject _schemas = default!;
    private JsonObject? _securitySchemes = null;
    private HashSet<string>? _includeSchemas = null;
    private HashSet<string>? _excludeSchemas = null;
    private Regex? _nameSimilarRegex = null;
    private Regex? _nameNotSimilarRegex = null;

    public void Setup(IApplicationBuilder builder, NpgsqlRestOptions options)
    {
        _builder = builder;

        // Initialize OpenAPI document structure
        var info = new JsonObject
        {
            ["title"] = GetDocumentTitle(),
            ["version"] = openApiOptions.DocumentVersion
        };

        if (!string.IsNullOrEmpty(openApiOptions.DocumentDescription))
        {
            info["description"] = openApiOptions.DocumentDescription;
        }

        _document = new JsonObject
        {
            // When nullability emission is added, this option must also gate
            // nullable: true (3.0) vs "type": [T, "null"] (3.1).
            ["openapi"] = _specVersion,
            ["info"] = info
        };

        // Add servers section if configured
        var servers = BuildServersArray();
        if (servers != null && servers.Count > 0)
        {
            _document["servers"] = servers;
        }

        _paths = new JsonObject();
        _document["paths"] = _paths;

        _schemas = new JsonObject();
        _document["components"] = new JsonObject
        {
            ["schemas"] = _schemas
        };

        // Initialize security schemes if configured
        if (openApiOptions.SecuritySchemes != null && openApiOptions.SecuritySchemes.Length > 0)
        {
            _securitySchemes = BuildSecuritySchemes(openApiOptions.SecuritySchemes);
            if (_securitySchemes != null && _securitySchemes.Count > 0)
            {
                _document["components"]!["securitySchemes"] = _securitySchemes;
            }
        }

        // Pre-compile filter sets and regexes once. Hot path (Handle) just consults these.
        if (openApiOptions.IncludeSchemas is { Length: > 0 } incl)
        {
            _includeSchemas = new HashSet<string>(incl, StringComparer.Ordinal);
        }
        if (openApiOptions.ExcludeSchemas is { Length: > 0 } excl)
        {
            _excludeSchemas = new HashSet<string>(excl, StringComparer.Ordinal);
        }
        if (!string.IsNullOrEmpty(openApiOptions.NameSimilarTo))
        {
            _nameSimilarRegex = SimilarToRegex(openApiOptions.NameSimilarTo);
        }
        if (!string.IsNullOrEmpty(openApiOptions.NameNotSimilarTo))
        {
            _nameNotSimilarRegex = SimilarToRegex(openApiOptions.NameNotSimilarTo);
        }
    }

    /// <summary>
    /// Translates a PostgreSQL <c>SIMILAR TO</c> pattern into an anchored .NET regex. <c>_</c> →
    /// <c>.</c>, <c>%</c> → <c>.*</c>. The remaining meta-characters PostgreSQL accepts (<c>|</c>,
    /// <c>*</c>, <c>+</c>, <c>?</c>, <c>(...)</c>, <c>[...]</c>, <c>{m,n}</c>) overlap with .NET regex
    /// syntax and pass through unchanged. The result is anchored with <c>\A...\z</c> so the pattern
    /// must cover the whole identifier, matching PostgreSQL's <c>SIMILAR TO</c> semantics.
    /// Compiled and cached at <see cref="Setup"/>; no per-request allocation.
    /// </summary>
    private static Regex SimilarToRegex(string pattern)
    {
        var sb = new System.Text.StringBuilder(pattern.Length + 8);
        sb.Append(@"\A");
        foreach (var c in pattern)
        {
            if (c == '_') sb.Append('.');
            else if (c == '%') sb.Append(".*");
            else sb.Append(c);
        }
        sb.Append(@"\z");
        return new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    public void Handle(RoutineEndpoint endpoint)
    {
        // openapi hide/tags were parsed during core's single comment pass (HandleCommentLine) and
        // stashed in Items. Core is OpenAPI-agnostic.
        var openApiHide = endpoint.TryGetItem(ItemHide, out var hideVal) && hideVal is true;
        var openApiTags = endpoint.TryGetItem(ItemTags, out var tagsVal) ? tagsVal as string[] : null;

        // Filter gate — applied in order of decreasing specificity. First match short-circuits.
        // The endpoint itself remains registered with NpgsqlRest; only its documentation is skipped.

        // Internal-only endpoints have no public HTTP route (proxy/HTTP-type-callable, or a bare-@mcp
        // MCP-only routine), so documenting them would advertise a path that 404s.
        if (endpoint.InternalOnly)
        {
            return;
        }
        // Per-endpoint opt-out from comment annotation (`openapi hide`).
        if (openApiHide)
        {
            return;
        }
        // Document only authenticated endpoints when configured — anonymous routes (health, login,
        // probes) typically shouldn't appear in a partner-facing document.
        if (openApiOptions.RequiresAuthorizationOnly && !endpoint.RequiresAuthorization)
        {
            return;
        }
        // Schema allow-list / deny-list. Both apply: include must pass AND exclude must not match.
        var schema = endpoint.Routine.Schema;
        if (_includeSchemas is not null && !_includeSchemas.Contains(schema))
        {
            return;
        }
        if (_excludeSchemas is not null && _excludeSchemas.Contains(schema))
        {
            return;
        }
        // Name-pattern filters. PostgreSQL-style SIMILAR TO, compiled once in Setup.
        var name = endpoint.Routine.Name;
        if (_nameSimilarRegex is not null && !_nameSimilarRegex.IsMatch(name))
        {
            return;
        }
        if (_nameNotSimilarRegex is not null && _nameNotSimilarRegex.IsMatch(name))
        {
            return;
        }

        var path = endpoint.Path;
        // OpenAPI 3.0/3.1 path items only allow the fixed operation keys (get/put/post/delete/...);
        // the QUERY method has no representation until OpenAPI 3.2. Skip those endpoints instead of
        // emitting an invalid `query` key.
        if (endpoint.Method == Method.QUERY)
        {
            Logger?.LogWarning("OpenAPI: skipping {Method} {Path} - the QUERY method cannot be represented in OpenAPI {Version} (supported from OpenAPI 3.2).",
                endpoint.Method, endpoint.Path, _specVersion);
            return;
        }
        var method = endpoint.Method.ToString().ToLowerInvariant();

        // Initialize path object if it doesn't exist
        if (!_paths.ContainsKey(path))
        {
            _paths[path] = new JsonObject();
        }

        var pathItem = _paths[path] as JsonObject;
        var operation = new JsonObject();

        // Add operation summary and description
        if (!string.IsNullOrEmpty(endpoint.Routine.Comment))
        {
            var lines = endpoint.Routine.Comment.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            operation["summary"] = lines[0].Trim();
            if (lines.Length > 1)
            {
                operation["description"] = string.Join("\n", lines.Skip(1).Select(l => l.Trim()));
            }
        }
        else
        {
            operation["summary"] = $"{endpoint.Routine.Type} {endpoint.Routine.Schema}.{endpoint.Routine.Name}";
        }

        // Tag selection: explicit `openapi tag/tags` annotation values win over the default schema
        // grouping. Drives the section headings in Swagger UI / ReDoc.
        if (openApiTags is { Length: > 0 } customTags)
        {
            var tagsArray = new JsonArray();
            foreach (var tag in customTags)
            {
                tagsArray.Add((JsonNode)tag);
            }
            operation["tags"] = tagsArray;
        }
        else
        {
            operation["tags"] = new JsonArray(endpoint.Routine.Schema);
        }

        // Add operation ID
        operation["operationId"] = $"{endpoint.Routine.Schema}_{endpoint.Routine.Name}_{method}";

        // Add parameters
        var parameters = new JsonArray();

        // Add path parameters first (if any)
        if (endpoint.HasPathParameters)
        {
            foreach (var pathParamName in endpoint.PathParameters!)
            {
                // Find the matching routine parameter to get its type
                var routineParam = endpoint.Routine.Parameters
                    .FirstOrDefault(p =>
                        string.Equals(p.ConvertedName, pathParamName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.ActualName, pathParamName, StringComparison.OrdinalIgnoreCase));

                var pathParameter = new JsonObject
                {
                    ["name"] = routineParam?.ConvertedName ?? pathParamName,
                    ["in"] = "path",
                    ["required"] = true // Path parameters are always required in OpenAPI
                };

                if (routineParam != null)
                {
                    pathParameter["schema"] = SchemaMapper.GetSchemaForType(routineParam.TypeDescriptor);
                }
                else
                {
                    // Default to string if parameter not found
                    pathParameter["schema"] = new JsonObject { ["type"] = "string" };
                }

                parameters.Add((JsonNode)pathParameter);
            }
        }

        if (endpoint.Routine.Parameters.Length > 0)
        {
            if (endpoint.RequestParamType == RequestParamType.QueryString)
            {
                foreach (var param in endpoint.Routine.Parameters)
                {
                    // Skip server-filled parameters the client cannot set (when enabled)
                    if (openApiOptions.OmitAutomaticParameters && endpoint.OmitParameterFromGeneratedRequest(param))
                    {
                        continue;
                    }

                    // Skip body parameter if it exists
                    if (endpoint.IsBodyParameter(param))
                    {
                        continue;
                    }

                    // Skip path parameters - they are already added above
                    if (endpoint.HasPathParameters)
                    {
                        var isPathParam = false;
                        foreach (var pathParam in endpoint.PathParameters!)
                        {
                            if (string.Equals(param.ConvertedName, pathParam, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(param.ActualName, pathParam, StringComparison.OrdinalIgnoreCase))
                            {
                                isPathParam = true;
                                break;
                            }
                        }
                        if (isPathParam)
                        {
                            continue;
                        }
                    }

                    var parameter = new JsonObject
                    {
                        ["name"] = param.ConvertedName,
                        ["in"] = "query",
                        ["required"] = !param.TypeDescriptor.HasDefault,
                        ["schema"] = SchemaMapper.GetSchemaForType(param.TypeDescriptor)
                    };

                    parameters.Add((JsonNode)parameter);
                }
            }
            else if (endpoint.RequestParamType == RequestParamType.BodyJson)
            {
                // Add request body for JSON, excluding path parameters
                var requestSchema = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject()
                };

                var properties = requestSchema["properties"] as JsonObject;
                var required = new JsonArray();

                foreach (var param in endpoint.Routine.Parameters)
                {
                    // Skip server-filled parameters the client cannot set (when enabled)
                    if (openApiOptions.OmitAutomaticParameters && endpoint.OmitParameterFromGeneratedRequest(param))
                    {
                        continue;
                    }

                    // Skip path parameters - they should not be in the request body
                    if (endpoint.HasPathParameters)
                    {
                        var isPathParam = false;
                        foreach (var pathParam in endpoint.PathParameters!)
                        {
                            if (string.Equals(param.ConvertedName, pathParam, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(param.ActualName, pathParam, StringComparison.OrdinalIgnoreCase))
                            {
                                isPathParam = true;
                                break;
                            }
                        }
                        if (isPathParam)
                        {
                            continue;
                        }
                    }

                    properties![param.ConvertedName] = SchemaMapper.GetSchemaForType(param.TypeDescriptor);
                    if (!param.TypeDescriptor.HasDefault)
                    {
                        required.Add((JsonNode?)JsonValue.Create(param.ConvertedName));
                    }
                }

                if (required.Count > 0)
                {
                    requestSchema["required"] = required;
                }

                // Only add requestBody if there are non-path parameters
                if (properties!.Count > 0)
                {
                    operation["requestBody"] = new JsonObject
                    {
                        ["required"] = true,
                        ["content"] = new JsonObject
                        {
                            ["application/json"] = new JsonObject
                            {
                                ["schema"] = requestSchema
                            }
                        }
                    };
                }
            }
        }

        if (parameters.Count > 0)
        {
            operation["parameters"] = parameters;
        }

        // Handle body parameter in query string mode
        if (endpoint.BodyParameterName is not null && endpoint.RequestParamType == RequestParamType.QueryString)
        {
            var bodyParam = endpoint.Routine.Parameters
                .FirstOrDefault(endpoint.IsBodyParameter);

            // A server-filled body parameter (e.g. an HTTP Custom Type field) is not part of the client contract.
            if (bodyParam is not null && openApiOptions.OmitAutomaticParameters && endpoint.OmitParameterFromGeneratedRequest(bodyParam))
            {
                bodyParam = null;
            }

            if (bodyParam is not null)
            {
                operation["requestBody"] = new JsonObject
                {
                    ["required"] = !bodyParam.TypeDescriptor.HasDefault,
                    ["content"] = new JsonObject
                    {
                        ["text/plain"] = new JsonObject
                        {
                            ["schema"] = SchemaMapper.GetSchemaForType(bodyParam.TypeDescriptor)
                        }
                    }
                };
            }
        }

        // Add responses
        var responses = new JsonObject
        {
            ["200"] = new JsonObject
            {
                ["description"] = "Successful response"
            }
        };

        // Add response content if not void
        if (!endpoint.Routine.IsVoid)
        {
            var responseContent = new JsonObject();
            var contentType = endpoint.ResponseContentType ?? "application/json";

            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                var responseSchema = GetResponseSchema(endpoint.Routine);
                responseContent[contentType] = new JsonObject
                {
                    ["schema"] = responseSchema
                };
            }
            else
            {
                // For non-JSON responses
                responseContent[contentType] = new JsonObject
                {
                    ["schema"] = new JsonObject
                    {
                        ["type"] = "string"
                    }
                };
            }

            (responses["200"] as JsonObject)!["content"] = responseContent;
        }

        operation["responses"] = responses;

        // Add security if required
        if (endpoint.RequiresAuthorization)
        {
            // Add security requirements for this operation
            if (_securitySchemes != null && _securitySchemes.Count > 0)
            {
                // Add all configured security schemes as alternatives (OR relationship)
                var securityArray = new JsonArray();
                foreach (var schemeName in _securitySchemes.AsObject().Select(kv => kv.Key))
                {
                    securityArray.Add((JsonNode)new JsonObject
                    {
                        [schemeName] = new JsonArray()
                    });
                }
                operation["security"] = securityArray;
            }
            else
            {
                // Add default bearer auth if no schemes configured
                operation["security"] = new JsonArray(
                    new JsonObject
                    {
                        ["bearerAuth"] = new JsonArray()
                    }
                );

                // Add default bearer scheme to components if not already there
                if (!_document["components"]!.AsObject().ContainsKey("securitySchemes"))
                {
                    _document["components"]!["securitySchemes"] = new JsonObject
                    {
                        ["bearerAuth"] = new JsonObject
                        {
                            ["type"] = "http",
                            ["scheme"] = "bearer",
                            ["bearerFormat"] = "JWT"
                        }
                    };
                }
            }
        }

        pathItem![method] = operation;
    }

    private const string ItemHide = "openapi:hide";
    private const string ItemTags = "openapi:tags";

    /// <summary>
    /// Claims the `openapi` comment annotations during core's single parse pass and stashes the
    /// result in RoutineEndpoint.Items (read later in Handle):
    ///   openapi                        -> hide (bare form)
    ///   openapi hide | hidden | ignore -> hide
    ///   openapi tag | tags  a, b, ...   -> tag overrides (original casing preserved)
    /// Returns a result when claimed (RequestsEndpoint stays false — these are document-only
    /// modifiers and must NOT by themselves create an endpoint), null otherwise.
    /// </summary>
    public CommentLineResult? HandleCommentLine(RoutineEndpoint endpoint, string line, string[] words, string[] wordsLower)
    {
        if (wordsLower.Length == 0 || !CommentPrimitives.StrEquals(wordsLower[0], "openapi"))
        {
            return null;
        }

        if (wordsLower.Length < 2)
        {
            endpoint.Items[ItemHide] = true; // bare `openapi`
            return new CommentLineResult("openapi hide");
        }

        var sub = wordsLower[1];
        if (CommentPrimitives.StrEqualsToArray(sub, "hide", "hidden", "ignore"))
        {
            endpoint.Items[ItemHide] = true;
            return new CommentLineResult("openapi hide");
        }

        if (CommentPrimitives.StrEqualsToArray(sub, "tag", "tags"))
        {
            if (wordsLower.Length < 3)
            {
                return new CommentLineResult("openapi tag (ignored: no tag value)");
            }
            var tags = words[2..]; // original casing for tag values
            endpoint.Items[ItemTags] = tags;
            return new CommentLineResult("openapi tags: " + string.Join(", ", tags));
        }

        // Recognized `openapi` keyword but unknown sub-command — claim it so it isn't treated as prose.
        return new CommentLineResult($"openapi (ignored: unknown sub-command '{sub}')");
    }

    public void Cleanup()
    {
        if (openApiOptions.FileName is null && openApiOptions.UrlPath is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(_document, OpenApiSerializerContext.Default.JsonObject);

        // Write to file if FileName is specified
        if (openApiOptions.FileName is not null)
        {
            var fullFileName = System.IO.Path.Combine(Environment.CurrentDirectory, openApiOptions.FileName);

            if (!openApiOptions.FileOverwrite && File.Exists(fullFileName))
            {
                Logger?.LogDebug("OpenAPI file already exists and FileOverwrite is false: {fileName}", fullFileName);
            }
            else
            {
                var dir = System.IO.Path.GetDirectoryName(fullFileName);
                if (dir is not null && Directory.Exists(dir) is false)
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(fullFileName, json);
                Logger?.LogDebug("Created OpenAPI file: {fileName}", fullFileName);
            }
        }

        // Serve as endpoint if UrlPath is specified
        if (openApiOptions.UrlPath is not null)
        {
            var path = openApiOptions.UrlPath;
            _builder.Use(async (context, next) =>
            {
                if (string.Equals(context.Request.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.Path, path, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(json);
                    return;
                }

                await next(context);
            });

            var host = GetHost();
            Logger?.LogDebug("Exposed OpenAPI document on URL: {host}{path}", host, path);
        }
    }

    private JsonObject GetResponseSchema(Routine routine)
    {
        if (routine.ReturnsSet)
        {
            // Returns array of objects
            var itemSchema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject()
            };

            var properties = itemSchema["properties"] as JsonObject;
            for (int i = 0; i < routine.ColumnCount; i++)
            {
                properties![routine.ColumnNames[i]] = SchemaMapper.GetSchemaForType(routine.ColumnsTypeDescriptor[i]);
            }

            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = itemSchema
            };
        }
        else if (routine.ColumnCount > 1 || routine.ReturnsRecordType)
        {
            // Returns single object
            var schema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject()
            };

            var properties = schema["properties"] as JsonObject;
            for (int i = 0; i < routine.ColumnCount; i++)
            {
                properties![routine.ColumnNames[i]] = SchemaMapper.GetSchemaForType(routine.ColumnsTypeDescriptor[i]);
            }

            return schema;
        }
        else if (routine.ColumnCount == 1)
        {
            // Returns single value
            return SchemaMapper.GetSchemaForType(routine.ColumnsTypeDescriptor[0]);
        }

        // Default
        return new JsonObject
        {
            ["type"] = "object"
        };
    }

    private string GetHost()
    {
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
            }
            if (host is null && app.Configuration?["urls"] is not null)
            {
                host = app.Configuration?["urls"];
            }
        }
        // default, assumed host
        host ??= "http://localhost:8080";
        return host.TrimEnd('/');
    }
    
    private string GetDocumentTitle()
    {
        if (openApiOptions.DocumentTitle is not null)
        {
            return openApiOptions.DocumentTitle;
        }
        if (openApiOptions.ConnectionString is not null)
        {
            return new NpgsqlConnectionStringBuilder(openApiOptions.ConnectionString).Database ??
                   (openApiOptions.ConnectionString?.Split(";") ?? []).FirstOrDefault(s => s.StartsWith("Database="))
                   ?.Split("=")?.Last() ?? "NpgsqlRest API";
        }
        return "NpgsqlRest API";
    }

    private JsonArray? BuildServersArray()
    {
        var serversArray = new JsonArray();

        // Add configured servers first
        if (openApiOptions.Servers != null)
        {
            foreach (var server in openApiOptions.Servers)
            {
                var serverObj = new JsonObject
                {
                    ["url"] = server.Url
                };

                if (!string.IsNullOrEmpty(server.Description))
                {
                    serverObj["description"] = server.Description;
                }

                serversArray.Add((JsonNode)serverObj);
            }
        }

        // Add current server if enabled and not already added
        if (openApiOptions.AddCurrentServer)
        {
            var currentHost = GetHost();

            // Check if this URL is already in the servers array
            var alreadyExists = openApiOptions.Servers?.Any(s =>
                string.Equals(s.Url.TrimEnd('/'), currentHost, StringComparison.OrdinalIgnoreCase)) ?? false;

            if (!alreadyExists)
            {
                var currentServerObj = new JsonObject
                {
                    ["url"] = currentHost
                };

                // Add description for current server
                if (currentHost.Contains("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    currentServerObj["description"] = "Development server";
                }

                serversArray.Add((JsonNode)currentServerObj);
            }
        }

        return serversArray.Count > 0 ? serversArray : null;
    }

    private JsonObject? BuildSecuritySchemes(OpenApiSecurityScheme[] schemes)
    {
        if (schemes == null || schemes.Length == 0)
        {
            return null;
        }

        var securitySchemes = new JsonObject();

        foreach (var scheme in schemes)
        {
            var schemeObj = new JsonObject();

            switch (scheme.Type)
            {
                case OpenApiSecuritySchemeType.Http:
                    schemeObj["type"] = "http";
                    if (scheme.Scheme.HasValue)
                    {
                        schemeObj["scheme"] = scheme.Scheme.Value.ToString().ToLowerInvariant();
                    }
                    if (!string.IsNullOrEmpty(scheme.BearerFormat))
                    {
                        schemeObj["bearerFormat"] = scheme.BearerFormat;
                    }
                    break;

                case OpenApiSecuritySchemeType.ApiKey:
                    schemeObj["type"] = "apiKey";
                    if (!string.IsNullOrEmpty(scheme.In))
                    {
                        schemeObj["name"] = scheme.In;
                    }
                    if (scheme.ApiKeyLocation.HasValue)
                    {
                        schemeObj["in"] = scheme.ApiKeyLocation.Value.ToString().ToLowerInvariant();
                    }
                    break;
            }

            if (!string.IsNullOrEmpty(scheme.Description))
            {
                schemeObj["description"] = scheme.Description;
            }

            securitySchemes[scheme.Name] = schemeObj;
        }

        return securitySchemes.Count > 0 ? securitySchemes : null;
    }
}