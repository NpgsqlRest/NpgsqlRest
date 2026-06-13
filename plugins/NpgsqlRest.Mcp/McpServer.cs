using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlRest.Auth;
using NpgsqlRest.Common;
using static NpgsqlRest.NpgsqlRestOptions;

namespace NpgsqlRest.Mcp;

/// <summary>
/// MCP server transport: serves the JSON-RPC endpoint (Streamable HTTP, single endpoint) at
/// <see cref="McpOptions.UrlPath"/>. Stateless, single application/json response — no SSE.
/// Implements the MCP 2025-11-25 lifecycle subset: initialize, notifications/initialized, ping,
/// tools/list. (tools/call arrives in a later increment.)
/// </summary>
public partial class Mcp
{
    private const string ProtocolVersion = "2025-11-25";

    // Relaxed escaping: MCP responses are application/json consumed by MCP clients (never embedded in
    // HTML), so emit conventional JSON (e.g. \" instead of ", and no over-escaping of < > &).
    private static readonly JsonSerializerOptions JsonOutput = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private IApplicationBuilder _builder = default!;
    private string? _connectionString;
    private NpgsqlRestAuthenticationOptions _authOptions = new();

    public void Setup(IApplicationBuilder builder, NpgsqlRestOptions options)
    {
        _builder = builder;
        _connectionString = options.ConnectionString;
        _authOptions = options.AuthenticationOptions;
    }

    public void Cleanup()
    {
        if (!_options.Enabled)
        {
            return;
        }

        WarnIfAuthRequiredButNoAuthScheme();

        // One-line startup summary of the catalog (Information level), so operators can see which tools
        // are exposed without enabling Debug.
        if (_tools.Count > 0)
        {
            Logger?.LogInformation("MCP: {Count} tool(s) exposed at {Path} — {Tools}",
                _tools.Count, _options.UrlPath, string.Join(", ", _tools.Keys));
        }
        else
        {
            Logger?.LogInformation("MCP enabled at {Path}, but no routines are annotated `mcp` — no tools exposed.",
                _options.UrlPath);
        }

        var path = _options.UrlPath;
        // Protected Resource Metadata (RFC 9728) is served only when an Authorization Server is configured
        // — without one the document carries no useful discovery information.
        var servePrm = _options.Authorization.AuthorizationServers.Length > 0;
        var prmPath = ProtectedResourceMetadataPath();

        // Prefer mapped endpoints over middleware: an ASP.NET rate-limiter policy (RateLimiterPolicy) can
        // only be attached to a mapped endpoint via RequireRateLimiting — not to a middleware path. Map both
        // GET and POST so HandleAsync keeps emitting the in-handler 405 for GET (rather than a routing 405),
        // preserving the transport behavior. Fall back to middleware on hosts without endpoint routing.
        if (_builder is IEndpointRouteBuilder routes)
        {
            var mcp = routes.MapMethods(path, ["GET", "POST"], HandleAsync);
            if (!string.IsNullOrWhiteSpace(_options.RateLimiterPolicy))
            {
                mcp.RequireRateLimiting(_options.RateLimiterPolicy);
            }
            if (servePrm)
            {
                routes.MapMethods(prmPath, ["GET"], HandleProtectedResourceMetadataAsync);
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(_options.RateLimiterPolicy))
        {
            Logger?.LogWarning(
                "MCP RateLimiterPolicy '{Policy}' is configured but the host does not use endpoint routing, so it will not be applied to {Path}.",
                _options.RateLimiterPolicy, _options.UrlPath);
        }
        _builder.Use(async (context, next) =>
        {
            if (servePrm && string.Equals(context.Request.Path, prmPath, StringComparison.Ordinal))
            {
                await HandleProtectedResourceMetadataAsync(context);
                return;
            }
            if (string.Equals(context.Request.Path, path, StringComparison.Ordinal))
            {
                await HandleAsync(context);
                return;
            }
            await next(context);
        });
    }

    /// <summary>
    /// Startup guardrail: <c>RequireAuthorization</c> only works if the host has authentication wired —
    /// enabling MCP does not enable auth. With no registered scheme, every request would be 401, so warn.
    /// </summary>
    private void WarnIfAuthRequiredButNoAuthScheme()
    {
        if (!_options.Authorization.RequireAuthorization)
        {
            return;
        }
        var schemeProvider = _builder.ApplicationServices.GetService<IAuthenticationSchemeProvider>();
        var hasScheme = schemeProvider is not null && schemeProvider.GetAllSchemesAsync().GetAwaiter().GetResult().Any();
        if (!hasScheme)
        {
            Logger?.LogWarning(
                "MCP Authorization.RequireAuthorization is enabled but no authentication scheme is registered — every request to {Path} will return 401. Enabling MCP does not enable authentication; configure it separately (e.g. the client's Auth section / JWT bearer).",
                _options.UrlPath);
        }
    }

    /// <summary>The RFC 9728 well-known path for this resource (overridable via config).</summary>
    private string ProtectedResourceMetadataPath() =>
        _options.Authorization.ProtectedResourceMetadataPath
        ?? "/.well-known/oauth-protected-resource" + _options.UrlPath;

    private async Task HandleProtectedResourceMetadataAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        var auth = _options.Authorization;
        // The canonical resource URI tokens must target (RFC 8707). Explicit Audience wins; otherwise
        // derive it from the request so it matches the live origin.
        var resource = string.IsNullOrWhiteSpace(auth.Audience)
            ? $"{context.Request.Scheme}://{context.Request.Host}{_options.UrlPath}"
            : auth.Audience;

        var authServers = new JsonArray();
        foreach (var server in auth.AuthorizationServers)
        {
            authServers.Add((JsonNode?)server);
        }
        var doc = new JsonObject
        {
            ["resource"] = resource,
            ["authorization_servers"] = authServers,
            ["bearer_methods_supported"] = new JsonArray((JsonNode?)"header"),
        };
        if (auth.ScopesSupported.Length > 0)
        {
            var scopes = new JsonArray();
            foreach (var scope in auth.ScopesSupported)
            {
                scopes.Add((JsonNode?)scope);
            }
            doc["scopes_supported"] = scopes;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(doc.ToJsonString(JsonOutput), context.RequestAborted);
    }

    /// <summary>
    /// 401: authentication is required or the token is invalid (RFC 9728 §5.1 challenge).
    /// </summary>
    private Task WriteUnauthorized(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.Append("WWW-Authenticate", BearerChallenge(context, error: null));
        return WriteChallengeBodyAsync(context, "invalid_token", "This tool requires authentication. Provide a valid bearer token.");
    }

    /// <summary>
    /// 403: the principal is authenticated but lacks the permission the tool's <c>authorize</c> requires
    /// (RFC 6750 §3.1 <c>error="insufficient_scope"</c>).
    /// </summary>
    private Task WriteForbidden(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.Headers.Append("WWW-Authenticate", BearerChallenge(context, error: "insufficient_scope"));
        return WriteChallengeBodyAsync(context, "insufficient_scope", "This tool requires a role your token does not have.");
    }

    /// <summary>
    /// Writes a small RFC 6750-shaped JSON body (<c>error</c> + <c>error_description</c>) for a 401/403.
    /// The formal OAuth challenge is the status code plus the <c>WWW-Authenticate</c> header (set by the
    /// caller, unchanged); this body is supplementary so clients that surface the response body — and
    /// humans debugging with curl — get an actionable message instead of an empty response.
    /// </summary>
    private static Task WriteChallengeBodyAsync(HttpContext context, string error, string description)
    {
        context.Response.ContentType = "application/json";
        var body = new JsonObject { ["error"] = error, ["error_description"] = description };
        return context.Response.WriteAsync(body.ToJsonString(JsonOutput), context.RequestAborted);
    }

    /// <summary>
    /// Builds the <c>WWW-Authenticate: Bearer</c> challenge with <c>scope</c> (when scopes are configured,
    /// per the spec's SHOULD) and <c>resource_metadata</c> (when an Authorization Server is configured, so
    /// the client can discover it — RFC 9728 §5.1). Used for both 401 and 403 responses.
    /// </summary>
    private string BearerChallenge(HttpContext context, string? error)
    {
        var parts = new List<string>(3);
        if (error is not null)
        {
            parts.Add($"error=\"{error}\"");
        }
        if (_options.Authorization.ScopesSupported.Length > 0)
        {
            parts.Add($"scope=\"{string.Join(' ', _options.Authorization.ScopesSupported)}\"");
        }
        if (_options.Authorization.AuthorizationServers.Length > 0)
        {
            var prm = $"{context.Request.Scheme}://{context.Request.Host}{ProtectedResourceMetadataPath()}";
            parts.Add($"resource_metadata=\"{prm}\"");
        }
        return parts.Count == 0 ? "Bearer" : "Bearer " + string.Join(", ", parts);
    }

    /// <summary>
    /// DNS-rebinding protection. A request with no <c>Origin</c> header (server-to-server) is allowed; a
    /// present <c>Origin</c> must match a configured <see cref="McpOptions.AllowedOrigins"/> entry or the
    /// server's own origin, otherwise it is rejected.
    /// </summary>
    private bool IsOriginAllowed(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
        {
            return true;
        }
        foreach (var allowed in _options.AllowedOrigins)
        {
            if (string.Equals(origin, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        var self = $"{context.Request.Scheme}://{context.Request.Host}";
        return string.Equals(origin, self, StringComparison.OrdinalIgnoreCase);
    }

    private async Task HandleAsync(HttpContext context)
    {
        // DNS-rebinding protection (Streamable HTTP transport MUST): reject a present-but-untrusted Origin.
        if (!IsOriginAllowed(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // Protocol-version header (transport MUST): a present header that is not the version we speak is
        // a 400. An absent header is allowed (the initialize request carries none, and older clients omit it).
        var requestedVersion = context.Request.Headers["MCP-Protocol-Version"].ToString();
        if (!string.IsNullOrEmpty(requestedVersion) && !string.Equals(requestedVersion, ProtocolVersion, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        // Transport authorization gate (OAuth 2.1 Resource Server). When RequireAuthorization is on, the
        // host's bearer middleware must have authenticated the principal; otherwise reject with 401 and
        // point the client at the Protected Resource Metadata (RFC 9728) so it can discover the AS.
        if (_options.Authorization.RequireAuthorization && context.User?.Identity?.IsAuthenticated != true)
        {
            await WriteUnauthorized(context);
            return;
        }

        // Token audience binding (RFC 8707): when a canonical Audience is configured, an authenticated
        // token MUST carry it — reject tokens issued for a different resource. (Signature/expiry are
        // validated by the host's bearer middleware; this enforces the audience the PRM advertises.)
        if (!string.IsNullOrEmpty(_options.Authorization.Audience)
            && context.User?.Identity?.IsAuthenticated == true
            && !context.User.Claims.Any(c =>
                string.Equals(c.Type, "aud", StringComparison.Ordinal) &&
                string.Equals(c.Value, _options.Authorization.Audience, StringComparison.Ordinal)))
        {
            await WriteUnauthorized(context);
            return;
        }

        // POST carries JSON-RPC. (GET would open an SSE stream — not supported in the stateless model.)
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        string body;
        using (var reader = new StreamReader(context.Request.Body))
        {
            body = await reader.ReadToEndAsync(context.RequestAborted);
        }

        JsonNode? request;
        try
        {
            request = JsonNode.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        }
        catch (JsonException)
        {
            await WriteResponseAsync(context, ErrorEnvelope(null, -32700, "Parse error"));
            return;
        }

        // The body must be a single JSON-RPC object. Batches (JSON arrays) were removed in MCP 2025-11-25,
        // and any other shape is invalid — reject cleanly rather than letting member access throw.
        if (request is not JsonObject)
        {
            await WriteResponseAsync(context, ErrorEnvelope(null, -32600, "Invalid Request"));
            return;
        }

        var method = request["method"]?.GetValue<string>();
        var id = request["id"]?.DeepClone();

        // Notifications carry no id and expect no response body.
        if (method is not null && method.StartsWith("notifications/", StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            return;
        }

        if (method == "tools/call")
        {
            await HandleToolCallAsync(context, request, id);
            return;
        }

        JsonObject? result = method switch
        {
            "initialize" => BuildInitializeResult(),
            "ping" => new JsonObject(),
            "tools/list" => BuildToolsListResult(context.User),
            _ => null,
        };

        if (result is null)
        {
            await WriteResponseAsync(context, ErrorEnvelope(id, -32601, $"Method not found: {method}"));
            return;
        }

        await WriteResponseAsync(context, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result,
        });
    }

    private async Task HandleToolCallAsync(HttpContext context, JsonNode? request, JsonNode? id)
    {
        var prms = request?["params"];
        var name = prms?["name"]?.GetValue<string>();
        if (name is null || !_toolEndpoints.TryGetValue(name, out var endpoint))
        {
            // Structural failure → JSON-RPC error (not a tool result).
            await WriteResponseAsync(context, ErrorEnvelope(id, -32602, $"Unknown tool: {name}"));
            return;
        }

        var (httpMethod, path, body, contentType) = BuildInvocation(endpoint, prms?["arguments"] as JsonObject);

        // Forward the /mcp request's principal so the routine's authorize/claims binding applies.
        var invoke = await RoutineInvoker.InvokeAsync(
            httpMethod, path, headers: null, body: body, contentType: contentType,
            user: context.User, cancellationToken: context.RequestAborted);

        // Authorization outcome from the execution pipeline (core ran the tool's `authorize`/role check
        // against the forwarded principal) → spec-level HTTP challenges, not a tool result. 401 = the tool
        // needs authentication; 403 = authenticated but insufficient permission (RFC 9728/6750).
        if (invoke.StatusCode == StatusCodes.Status401Unauthorized)
        {
            await WriteUnauthorized(context);
            return;
        }
        if (invoke.StatusCode == StatusCodes.Status403Forbidden)
        {
            await WriteForbidden(context);
            return;
        }

        // Business/execution outcome → a normal result with isError. (Only structural problems above use
        // a JSON-RPC error.) On success we build structuredContent (always a JSON object, per spec) and,
        // for backward compatibility, put its serialized JSON in the text content block.
        var structured = invoke.IsSuccess && !endpoint.Raw
            ? BuildStructuredContent(endpoint, invoke.Body)
            : null;
        var text = structured?.ToJsonString(JsonOutput) ?? invoke.Body ?? string.Empty;

        var content = new JsonArray();
        // Cast to JsonNode? to bind the non-generic JsonArray.Add (the generic Add<T> is not AOT/trim-safe).
        content.Add((JsonNode?)new JsonObject { ["type"] = "text", ["text"] = text });

        var toolResult = new JsonObject
        {
            ["content"] = content,
            ["isError"] = !invoke.IsSuccess,
        };
        if (structured is not null)
        {
            toolResult["structuredContent"] = structured;
        }

        await WriteResponseAsync(context, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = toolResult,
        });
    }

    /// <summary>
    /// Builds the MCP <c>structuredContent</c> object from the routine's response body. Per spec it is
    /// always a JSON object: a single scalar value → <c>{ "value": … }</c>; a single record/composite →
    /// the object itself; a set of rows/values → <c>{ "items": [ … ] }</c>. Returns null when there is
    /// nothing to structure (void routine, empty body, or an unparseable JSON payload).
    /// </summary>
    private static JsonObject? BuildStructuredContent(RoutineEndpoint endpoint, string? body)
    {
        var routine = endpoint.Routine;
        if (routine.IsVoid || string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        // A set returns a JSON array → wrap as { "items": [...] }. (`single` collapses a set to one row.)
        if (routine.ReturnsSet && !endpoint.ReturnSingleRecord)
        {
            return TryParseJson(body) switch
            {
                JsonArray array => new JsonObject { ["items"] = array },
                // A multi-command SQL file returns an object keyed per result set ({ "result1": [...], … }) —
                // already a valid structuredContent object.
                JsonObject obj => obj,
                _ => null,
            };
        }

        // A single scalar value → { "value": ... }; a single record/composite → the object itself.
        if (routine.ColumnCount == 1 && !routine.ReturnsRecordType)
        {
            var type = routine.ColumnsTypeDescriptor.Length > 0 ? routine.ColumnsTypeDescriptor[0] : null;
            return new JsonObject { ["value"] = ScalarValue(body, type) };
        }

        return TryParseJson(body) as JsonObject;
    }

    /// <summary>
    /// Builds the tool's <c>outputSchema</c> (declared in tools/list) describing the structuredContent it
    /// returns. Mirrors <see cref="BuildStructuredContent"/>'s shape decisions exactly so results always
    /// conform (MCP 2025-11-25: when an outputSchema is provided, results MUST conform). Returns null for
    /// void/raw routines (which emit no structuredContent). Leaf value schemas allow null (PG columns are
    /// nullable); array/json/composite columns use a permissive schema so conformance is guaranteed.
    /// </summary>
    private static JsonObject? BuildOutputSchema(RoutineEndpoint endpoint)
    {
        var routine = endpoint.Routine;
        // No schema for void/raw, or for a multi-command SQL file (its per-result-set object shape, e.g.
        // { "result1": […], "result2": […] }, can't be derived from the single-set column metadata).
        if (routine.IsVoid || endpoint.Raw || routine.IsMultiCommand)
        {
            return null;
        }

        if (routine.ReturnsSet && !endpoint.ReturnSingleRecord)
        {
            var element = routine.ColumnCount == 1 && !routine.ReturnsRecordType
                ? NullableValueSchema(ColumnType(routine, 0))   // set of scalar values
                : RecordSchema(routine);                          // set of rows
            return ObjectSchema("items", new JsonObject { ["type"] = "array", ["items"] = element });
        }

        if (routine.ColumnCount == 1 && !routine.ReturnsRecordType)
        {
            return ObjectSchema("value", NullableValueSchema(ColumnType(routine, 0)));
        }

        return RecordSchema(routine);
    }

    private static JsonObject ObjectSchema(string property, JsonNode propertySchema) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { [property] = propertySchema },
    };

    private static JsonObject RecordSchema(Routine routine)
    {
        var properties = new JsonObject();
        for (var i = 0; i < routine.ColumnCount && i < routine.ColumnNames.Length; i++)
        {
            properties[routine.ColumnNames[i]] = NullableValueSchema(ColumnType(routine, i));
        }
        return new JsonObject { ["type"] = "object", ["properties"] = properties };
    }

    private static TypeDescriptor? ColumnType(Routine routine, int index) =>
        index < routine.ColumnsTypeDescriptor.Length ? routine.ColumnsTypeDescriptor[index] : null;

    /// <summary>
    /// A JSON Schema for a single column value, with <c>null</c> allowed. Arrays/json/composite columns
    /// (whose JSON form varies) get a permissive empty schema so a NULL or any payload still conforms.
    /// </summary>
    private static JsonNode NullableValueSchema(TypeDescriptor? type)
    {
        if (type is null || type.IsArray || type.IsJson || type.IsCompositeType)
        {
            return new JsonObject();
        }
        var schema = SchemaMapper.GetSchemaForType(type);
        if (schema["type"] is JsonValue typeValue && typeValue.TryGetValue<string>(out var typeName))
        {
            schema["type"] = new JsonArray { (JsonNode?)typeName, (JsonNode?)"null" };
        }
        return schema;
    }

    private static JsonNode? TryParseJson(string body)
    {
        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A scalar column value for <c>structuredContent.value</c>: numeric/boolean/json/array values are
    /// embedded as their JSON form; everything else (text, dates, etc.) as a JSON string.
    /// </summary>
    private static JsonNode? ScalarValue(string body, TypeDescriptor? type)
    {
        if (type is not null)
        {
            // PostgreSQL renders a scalar boolean as t/f, which is not valid JSON — map it explicitly.
            if (type.IsBoolean)
            {
                return JsonValue.Create(body is "t" or "true");
            }
            if ((type.IsNumeric || type.IsJson || type.IsArray) && TryParseJson(body) is { } parsed)
            {
                return parsed;
            }
        }
        return JsonValue.Create(body);
    }

    /// <summary>
    /// Maps a flat MCP arguments object onto the endpoint's HTTP shape: path-parameter substitution,
    /// then query string (QueryString endpoints) or a JSON body (BodyJson endpoints).
    /// </summary>
    private static (string Method, string Path, string? Body, string? ContentType) BuildInvocation(
        RoutineEndpoint endpoint, JsonObject? arguments)
    {
        var args = arguments is null ? new JsonObject() : (JsonObject)arguments.DeepClone();
        var path = endpoint.Path;

        if (endpoint.PathParameters is { Length: > 0 } pathParams)
        {
            foreach (var pp in pathParams)
            {
                if (args.TryGetPropertyValue(pp, out var v) && v is not null)
                {
                    path = path.Replace("{" + pp + "}", Uri.EscapeDataString(v.ToString()));
                    args.Remove(pp);
                }
            }
        }

        var method = endpoint.Method.ToString();

        if (endpoint.RequestParamType == RequestParamType.BodyJson)
        {
            return (method, path, args.ToJsonString(), "application/json");
        }

        // QueryString endpoints (GET/DELETE by default).
        if (args.Count > 0)
        {
            var query = string.Join("&", args.Select(kv =>
                string.Concat(Uri.EscapeDataString(kv.Key), "=", Uri.EscapeDataString(kv.Value?.ToString() ?? string.Empty))));
            path = string.Concat(path, path.Contains('?') ? "&" : "?", query);
        }
        return (method, path, null, null);
    }

    private JsonObject BuildInitializeResult()
    {
        var result = new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = GetServerName(),
                ["version"] = string.IsNullOrWhiteSpace(_options.ServerVersion) ? "1.0.0" : _options.ServerVersion,
            },
        };
        if (!string.IsNullOrWhiteSpace(_options.Instructions))
        {
            result["instructions"] = _options.Instructions;
        }
        return result;
    }

    /// <summary>
    /// serverInfo.name for the initialize handshake. Explicit <see cref="McpOptions.ServerName"/> wins;
    /// otherwise the database name from the connection string is used (mirrors the OpenAPI document title),
    /// falling back to "NpgsqlRest" when it cannot be resolved.
    /// </summary>
    private string GetServerName()
    {
        if (!string.IsNullOrWhiteSpace(_options.ServerName))
        {
            return _options.ServerName;
        }
        if (!string.IsNullOrWhiteSpace(_connectionString))
        {
            var database = new NpgsqlConnectionStringBuilder(_connectionString).Database;
            if (!string.IsNullOrWhiteSpace(database))
            {
                return database;
            }
        }
        return "NpgsqlRest";
    }

    private JsonObject BuildToolsListResult(ClaimsPrincipal? user)
    {
        // Optional role filter: hide tools the caller couldn't run (their routine's authorize/role check
        // would deny). Off by default — the list stays fully discoverable and tools/call enforces anyway.
        var filter = _options.Authorization.FilterToolsByRole;
        var tools = new JsonArray();
        foreach (var (name, tool) in _tools)
        {
            if (filter && _toolEndpoints.TryGetValue(name, out var endpoint) && !endpoint.IsCallableBy(user, _authOptions))
            {
                continue;
            }
            tools.Add(tool.DeepClone()); // catalog entries are owned by _tools; clone before reparenting
        }
        return new JsonObject { ["tools"] = tools };
    }

    private static JsonObject ErrorEnvelope(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };

    private static async Task WriteResponseAsync(HttpContext context, JsonObject payload)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";
        context.Response.Headers["MCP-Protocol-Version"] = ProtocolVersion;
        await context.Response.WriteAsync(payload.ToJsonString(JsonOutput), context.RequestAborted);
    }
}
