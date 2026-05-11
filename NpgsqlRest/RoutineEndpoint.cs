using Microsoft.Extensions.Primitives;

namespace NpgsqlRest;

public class RoutineEndpoint(
    Routine routine,
    string path,
    Method method,
    RequestParamType requestParamType,
    bool requiresAuthorization,
    string? responseContentType,
    Dictionary<string, StringValues> responseHeaders,
    RequestHeadersMode requestHeadersMode,
    string requestHeadersParameterName,
    string? bodyParameterName,
    TextResponseNullHandling textResponseNullHandling,
    QueryStringNullHandling queryStringNullHandling,
    HashSet<string>? authorizeRoles = null,
    bool login = false,
    bool logout = false,
    bool securitySensitive = false,
    ulong? bufferRows = null,
    bool raw = false,
    string? rawValueSeparator = null,
    string? rawNewLineSeparator = null,
    bool rawColumnNames = false,
    bool cached = false,
    string[]? cachedParams = null,
    TimeSpan? cacheExpiresIn = null,
    string? connectionName = null,
    bool upload = false,
    string[]? uploadHandlers = null,
    Dictionary<string, string>? customParameters = null,
    bool userContext = false,
    bool userParameters = false,
    string? sseEventsPath = null,
    SseEventsScope sseEventsScope = SseEventsScope.All,
    HashSet<string>? sseEventsRoles = null,
    bool encryptAllParameters = false,
    HashSet<string>? encryptParameters = null,
    bool decryptAllColumns = false,
    HashSet<string>? decryptColumns = null)
{
    private string? _bodyParameterName = bodyParameterName;

    internal bool HasBodyParameter = !string.IsNullOrWhiteSpace(bodyParameterName);
    internal Action<ILogger, string, string, Exception?>? LogCallback { get; set; }
    internal bool HeadersNeedParsing { get; set; } = false;
    internal bool CustomParamsNeedParsing { get; set; } = false;

    public Routine Routine { get; } = routine;
    public string Path { get; set; } = path;
    public Method Method { get; set; } = method;
    public RequestParamType RequestParamType { get; set; } = requestParamType;
    public bool RequiresAuthorization { get; set; } = requiresAuthorization;
    public string? ResponseContentType { get; set; } = responseContentType;
    public Dictionary<string, StringValues> ResponseHeaders { get; set; } = responseHeaders;
    public RequestHeadersMode RequestHeadersMode { get; set; } = requestHeadersMode;
    public string RequestHeadersParameterName { get; set; } = requestHeadersParameterName;
    public string? BodyParameterName
    {
        get => _bodyParameterName;
        set
        {
            HasBodyParameter = !string.IsNullOrWhiteSpace(value);
            _bodyParameterName = value;
        }
    }
    public TextResponseNullHandling TextResponseNullHandling { get; set; } = textResponseNullHandling;
    public QueryStringNullHandling QueryStringNullHandling { get; set; } = queryStringNullHandling;
    public HashSet<string>? AuthorizeRoles { get; set; } = authorizeRoles;
    public bool Login { get; set; } = login;
    public bool Logout { get; set; } = logout;
    public bool SecuritySensitive { get; set; } = securitySensitive;
    public bool IsAuth => Login || Logout || SecuritySensitive;
    public ulong? BufferRows { get; set; } = bufferRows;
    public bool Raw { get; set; } = raw;
    public string? RawValueSeparator { get; set; } = rawValueSeparator;
    public string? RawNewLineSeparator { get; set; } = rawNewLineSeparator;
    public bool RawColumnNames { get; set; } = rawColumnNames;
    public string[][]? CommentWordLines { get; internal set; }
    public bool Cached { get; set; } = cached;
    public HashSet<string>? CachedParams { get; set; } = cachedParams?.ToHashSet();
    public TimeSpan? CacheExpiresIn { get; set; } = cacheExpiresIn;

    /// <summary>
    /// Name of the cache profile selected for this endpoint via the <c>@cache_profile &lt;name&gt;</c> annotation.
    /// Resolved at startup against <see cref="CacheOptions.Profiles"/>; if a name is not found startup fails with
    /// a single error listing every unresolved name and its offending endpoint.
    /// </summary>
    public string? CacheProfile { get; set; }

    /// <summary>
    /// Resolved cache backend for this endpoint (set during startup if <see cref="CacheProfile"/> is not null).
    /// At runtime the endpoint uses this instance for read/write/invalidate; falls back to
    /// <see cref="CacheOptions.DefaultRoutineCache"/> when null.
    /// </summary>
    internal IRoutineCache? ResolvedCache { get; set; }

    /// <summary>
    /// Cache key prefix (set to the resolved profile name) so two profiles sharing the same backend cannot collide.
    /// Null for endpoints without a profile (root cache; existing key shape unchanged).
    /// </summary>
    internal string? CacheKeyPrefix { get; set; }

    /// <summary>
    /// Conditional rules inherited from <see cref="CacheProfile.When"/>. Evaluated in order at request time
    /// against resolved parameter values; the first matching rule's action (bypass or TTL override) is applied.
    /// </summary>
    internal CacheWhenRule[]? CacheWhen { get; set; }
    public string? ConnectionName { get; set; } = connectionName;
    public bool Upload { get; set; } = upload;
    public string[]? UploadHandlers { get; set; } = uploadHandlers;
    public Dictionary<string, string>? CustomParameters { get; set; } = customParameters;
    public bool UserContext { get; set; } = userContext;
    public bool UseUserParameters { get; set; } = userParameters;
    public PostgresNoticeLevels? SseEventNoticeLevel { get; set; } = null;
    public string? SseEventsPath { get; set; } = sseEventsPath;
    public SseEventsScope SseEventsScope { get; set; } = sseEventsScope;
    public HashSet<string>? SseEventsRoles { get; set; } = sseEventsRoles;

    /// <summary>
    /// When true, <c>RAISE</c> statements in this routine's body whose severity matches the configured
    /// SSE level are forwarded to the SSE broadcaster. Set by <c>@sse_publish</c> (publish-only, no URL
    /// exposed) or as a side-effect of the <c>@sse</c> shorthand (publish + subscribe on the same path).
    /// Independent of <see cref="SseEventsPath"/>: a routine can publish without exposing a subscribe URL,
    /// and an <c>@sse_subscribe</c>-only routine exposes a URL without publishing from its own body.
    /// </summary>
    public bool SsePublishEnabled { get; set; } = false;
    public Auth.EndpointBasicAuthOptions? BasicAuth { get; set; } = null;
    public RetryStrategy? RetryStrategy { get; set; } = null;
    public string? RateLimiterPolicy { get; set; } = null;
    public string? ErrorCodePolicy { get; set; } = null;
    public TimeSpan? CommandTimeout { get; set; } = null;
    /// <summary>
    /// When true, this endpoint is only accessible via internal self-referencing calls
    /// (InternalRequestHandler). It is NOT registered as an HTTP route.
    /// </summary>
    public bool InternalOnly { get; set; } = false;
    /// <summary>
    /// When true, encrypt ALL text parameters using the default data protector.
    /// </summary>
    public bool EncryptAllParameters { get; set; } = encryptAllParameters;
    /// <summary>
    /// Set of parameter names (actual or converted) to encrypt using the default data protector.
    /// </summary>
    public HashSet<string>? EncryptParameters { get; set; } = encryptParameters;
    /// <summary>
    /// When true, decrypt ALL text result columns using the default data protector.
    /// </summary>
    public bool DecryptAllColumns { get; set; } = decryptAllColumns;
    /// <summary>
    /// Set of column names to decrypt using the default data protector.
    /// </summary>
    public HashSet<string>? DecryptColumns { get; set; } = decryptColumns;
    /// <summary>
    /// When true, this endpoint is a cache invalidation endpoint.
    /// Instead of executing the routine, it removes the cached entry for the given parameters.
    /// </summary>
    public bool InvalidateCache { get; set; } = false;
    /// <summary>
    /// When true, this endpoint is excluded from the OpenAPI document produced by the
    /// <c>NpgsqlRest.OpenApi</c> plugin. The HTTP endpoint itself is unaffected — only its appearance
    /// in the generated spec. Set via the <c>openapi hide</c> / <c>openapi hidden</c> / <c>openapi ignore</c>
    /// comment annotation. Ignored when the OpenAPI plugin is not loaded.
    /// </summary>
    public bool OpenApiHide { get; set; } = false;
    /// <summary>
    /// Tags emitted on this endpoint in the OpenAPI document. When null, the plugin defaults to the
    /// routine's schema name (existing behavior). When set (via <c>openapi tag &lt;name&gt;</c> /
    /// <c>openapi tags &lt;a&gt;,&lt;b&gt;</c>), these values replace the default. Drives grouping in
    /// Swagger UI / ReDoc, so this is the lever for "partner" vs "internal" sections in a shared
    /// document. Ignored when the OpenAPI plugin is not loaded.
    /// </summary>
    public string[]? OpenApiTags { get; set; } = null;

    /// <summary>
    /// Dictionary of parameter names to SQL expressions that resolve their values server-side.
    /// Key = actual parameter name (e.g., "_token"), Value = SQL expression template (e.g., "select api_token from tokens where user_id = {_user_id}").
    /// Resolved parameters cannot be overridden by client input.
    /// </summary>
    public Dictionary<string, string>? ResolvedParameterExpressions { get; set; }

    /// <summary>
    /// List of parameter names that are extracted from the URL path.
    /// For example, path "/products/{p_id}" would have PathParameters = ["p_id"].
    /// These parameters are populated from ASP.NET Core RouteValues.
    /// </summary>
    public string[]? PathParameters { get; set; } = null;

    /// <summary>
    /// HashSet for O(1) case-insensitive lookup of path parameter names.
    /// Lazily initialized when PathParameters is set and first accessed.
    /// </summary>
    internal HashSet<string>? PathParametersHashSet { get; private set; } = null;

    /// <summary>
    /// Returns true if this endpoint has any path parameters defined.
    /// </summary>
    public bool HasPathParameters => PathParameters is not null && PathParameters.Length > 0;

    /// <summary>
    /// Ensures the PathParametersHashSet is initialized for fast lookups.
    /// Call this after setting PathParameters.
    /// </summary>
    internal void EnsurePathParametersHashSet()
    {
        if (PathParameters is not null && PathParametersHashSet is null)
        {
            PathParametersHashSet = new HashSet<string>(PathParameters, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Finds the matching path parameter name for a given parameter name (case-insensitive).
    /// Returns null if no match is found.
    /// </summary>
    internal string? FindMatchingPathParameter(string convertedName, string? actualName)
    {
        if (PathParameters is null) return null;

        // Use HashSet for O(1) contains check, then find exact match for the return value
        EnsurePathParametersHashSet();

        if (PathParametersHashSet!.Contains(convertedName))
        {
            // Find the exact string from the array to use as route key
            foreach (var pathParam in PathParameters)
            {
                if (string.Equals(pathParam, convertedName, StringComparison.OrdinalIgnoreCase))
                {
                    return pathParam;
                }
            }
        }

        if (actualName is not null && PathParametersHashSet.Contains(actualName))
        {
            foreach (var pathParam in PathParameters)
            {
                if (string.Equals(pathParam, actualName, StringComparison.OrdinalIgnoreCase))
                {
                    return pathParam;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// When true, this endpoint acts as a reverse proxy.
    /// Incoming requests are forwarded to ProxyHost + Path, and the response is returned to the client.
    /// </summary>
    public bool IsProxy { get; set; } = false;

    /// <summary>
    /// The proxy host URL for this endpoint (e.g., "https://api.example.com").
    /// If null, uses ProxyOptions.Host from global configuration.
    /// </summary>
    public string? ProxyHost { get; set; } = null;

    /// <summary>
    /// Optional HTTP method override for the proxy request.
    /// If null, uses the same method as the incoming request.
    /// </summary>
    public Method? ProxyMethod { get; set; } = null;

    /// <summary>
    /// Computed during endpoint creation: true if any routine parameter matches a proxy response field name.
    /// When true, the routine will be invoked with proxy response data.
    /// When false, the proxy response is returned directly without invoking the routine.
    /// </summary>
    internal bool HasProxyResponseParameters { get; set; } = false;

    /// <summary>
    /// Set of parameter names that receive proxy response data.
    /// </summary>
    internal HashSet<string>? ProxyResponseParameterNames { get; set; } = null;

    /// <summary>
    /// When true, this endpoint executes the PostgreSQL function first, then forwards
    /// the function's result body as the request body to an upstream proxy service.
    /// The upstream response is returned to the client.
    /// </summary>
    public bool IsProxyOut { get; set; } = false;

    /// <summary>
    /// The proxy host URL for proxy_out endpoints (e.g., "https://render-service.internal").
    /// If null, uses ProxyOptions.Host from global configuration.
    /// </summary>
    public string? ProxyOutHost { get; set; } = null;

    /// <summary>
    /// HTTP method for the proxy_out request (e.g., POST, PUT).
    /// </summary>
    public Method? ProxyOutMethod { get; set; } = null;

    /// <summary>
    /// Dictionary of parameter validations. Key is the parameter name, value is the list of validation rules to apply.
    /// Configured via comment annotations using "validate _param using rule_name" syntax.
    /// </summary>
    public Dictionary<string, List<ValidationRule>>? ParameterValidations { get; set; } = null;

    /// <summary>
    /// When true, only the first row is returned and the result is serialized as a JSON object
    /// instead of a JSON array. If the query returns multiple rows, only the first row is used.
    /// Configured via the "single" comment annotation.
    /// </summary>
    public bool ReturnSingleRecord { get; set; } = false;

    /// <summary>
    /// When true, composite type columns in the response are serialized as nested JSON objects.
    /// For example, a column "req" of type "my_request(id int, name text)" becomes {"req": {"id": 1, "name": "test"}}
    /// instead of the default flat structure {"id": 1, "name": "test"}.
    /// </summary>
    public bool? NestedJsonForCompositeTypes { get; set; } = null;

    /// <summary>
    /// When true, the endpoint is forced to behave as void — all statements are executed
    /// but no result is returned. Returns 204 No Content.
    /// Configured via the "void" comment annotation.
    /// </summary>
    public bool Void { get; set; } = false;
}
