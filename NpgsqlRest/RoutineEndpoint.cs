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
    string? infoEventsStreamingPath = null,
    InfoEventsScope infoEventsScope = InfoEventsScope.Self,
    HashSet<string>? infoEventsRoles = null)
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
    public string? InfoEventsStreamingPath { get; set; } = infoEventsStreamingPath;
    public InfoEventsScope InfoEventsScope { get; set; } = infoEventsScope;
    public HashSet<string>? InfoEventsRoles { get; set; } = infoEventsRoles;
    public Auth.EndpointBasicAuthOptions? BasicAuth { get; set; } = null;
    public RetryStrategy? RetryStrategy { get; set; } = null;
    public string? RateLimiterPolicy { get; set; } = null;
    public string? ErrorCodePolicy { get; set; } = null;
    public TimeSpan? CommandTimeout { get; set; } = null;
}
