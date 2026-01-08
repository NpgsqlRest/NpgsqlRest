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
    HashSet<string>? sseEventsRoles = null)
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
    public Auth.EndpointBasicAuthOptions? BasicAuth { get; set; } = null;
    public RetryStrategy? RetryStrategy { get; set; } = null;
    public string? RateLimiterPolicy { get; set; } = null;
    public string? ErrorCodePolicy { get; set; } = null;
    public TimeSpan? CommandTimeout { get; set; } = null;
    /// <summary>
    /// When true, this endpoint is a cache invalidation endpoint.
    /// Instead of executing the routine, it removes the cached entry for the given parameters.
    /// </summary>
    public bool InvalidateCache { get; set; } = false;

    /// <summary>
    /// List of parameter names that are extracted from the URL path.
    /// For example, path "/products/{p_id}" would have PathParameters = ["p_id"].
    /// These parameters are populated from ASP.NET Core RouteValues.
    /// </summary>
    public string[]? PathParameters { get; set; } = null;

    /// <summary>
    /// Returns true if this endpoint has any path parameters defined.
    /// </summary>
    public bool HasPathParameters => PathParameters is not null && PathParameters.Length > 0;

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
    /// Dictionary of parameter validations. Key is the parameter name, value is the list of validation rules to apply.
    /// Configured via comment annotations using "validate _param using rule_name" syntax.
    /// </summary>
    public Dictionary<string, List<ValidationRule>>? ParameterValidations { get; set; } = null;
}
