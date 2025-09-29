namespace NpgsqlRest;

public static partial class Log
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Could not read a value from {commandText} mapped to {method} {path}")]
    public static partial void CouldNotReadCommand(this ILogger logger, string commandText, string method, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not parse JSON body {body}, skipping path {path}.")]
    public static partial void CouldNotParseJson(this ILogger logger, string body, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not parse JSON body {body}, skipping path {path}. Error: {error}")]
    public static partial void CouldNotParseJson(this ILogger logger, string body, string path, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Endpoint {method} {url} changed request parameter type from body to query string because body will be used for parameter named \"{bodyParameterName}\".")]
    public static partial void EndpointTypeChangedBodyParam(this ILogger logger, string method, string url, string bodyParameterName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Endpoint {method} {url} changed request parameter type from body to query string because body will be used for the upload.")]
    public static partial void EndpointTypeChangedUpload(this ILogger logger, string method, string url);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Endpoint {method} {url} changed method to {now} because it is designated as the upload.")]
    public static partial void EndpointMethodChangedUpload(this ILogger logger, string method, string url, string now);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Created endpoint {urlInfo}")]
    public static partial void EndpointCreated(this ILogger logger, string urlInfo);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Endpoint {urlInfo} has INFO notification streaming at path {eventPath}")]
    public static partial void EndpointInfoStreamingPath(this ILogger logger, string urlInfo, string eventPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid URL path segment '{urlPathSegment}' in comment for {description}. Using default '{defaultUrl}'")]
    public static partial void InvalidUrlPathSegmentComment(this ILogger logger, string urlPathSegment, string description, string defaultUrl);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set HTTP by the comment annotation to {method} {url}")]
    public static partial void CommentSetHttp(this ILogger logger, string description, Method method, string url);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid parameter type '{type}' in comment for {description} Allowed values are QueryString or Query or BodyJson or Json. Using default '{defaultType}'")]
    public static partial void InvalidParameterTypeComment(this ILogger logger, string type, string description, RequestParamType defaultType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set REQUEST PARAMETER TYPE by the comment annotation to {requestParamType}")]
    public static partial void CommentSetParameterType(this ILogger logger, string description, RequestParamType requestParamType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set REQUIRED AUTHORIZATION by the comment annotation.")]
    public static partial void CommentSetAuth(this ILogger logger, string description);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set REQUIRED AUTHORIZATION FOR ROLES {roles} by the comment annotation.")]
    public static partial void CommentSetAuthRoles(this ILogger logger, string description, HashSet<string> roles);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set ALLOW ANONYMOUS by the comment annotation.")]
    public static partial void CommentSetAnon(this ILogger logger, string description);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set COMMAND TIMEOUT by the comment annotation to {parsedTimeout} seconds")]
    public static partial void CommentSetTimeout(this ILogger logger, string description, string parsedTimeout);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid command timeout '{timeout}' in comment for {description}. Using default command timeout '{defaultTimeout}'")]
    public static partial void InvalidTimeoutComment(this ILogger logger, string timeout, string description, int? defaultTimeout);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid request headers mode '{mode}' in comment for {description} Allowed values are Ignore or Context or Parameter. Using default '{defaultRequestHeadersMode}'")]
    public static partial void InvalidRequestHeadersModeComment(this ILogger logger, string mode, string description, RequestHeadersMode defaultRequestHeadersMode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set REQUEST HEADERS MODE by the comment annotation to \"{requestHeadersMode}\"")]
    public static partial void CommentSetRequestHeadersMode(this ILogger logger, string description, string requestHeadersMode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set REQUEST HEADERS PARAMETER NAME by the comment annotation to \"{requestHeadersParamName}\"")]
    public static partial void CommentSetRequestHeadersParamName(this ILogger logger, string description, string requestHeadersParamName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set BODY PARAMETER NAME by the comment annotation to \"{bodyParamName}\"")]
    public static partial void CommentSetBodyParamName(this ILogger logger, string description, string bodyParamName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid text response null handling mode '{mode}' in comment for {description}. Allowed values are EmptyString or NullLiteral or NoContent. Using default '{textResponseNullHandling}'")]
    public static partial void InvalidResponseNullHandlingModeComment(this ILogger logger, string mode, string description, TextResponseNullHandling textResponseNullHandling);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set TEXT RESPONSE NULL HANDLING by the comment annotation to \"{textResponseNullHandling}\"")]
    public static partial void CommentSetTextResponseNullHandling(this ILogger logger, string description, string textResponseNullHandling);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid query string null handling mode '{mode}' in comment for {description}. Allowed values are EmptyString or NullLiteral or Ignore. Using default '{queryStringNullHandling}'\"")]
    public static partial void InvalidQueryStringNullHandlingComment(this ILogger logger, string mode, string description, QueryStringNullHandling queryStringNullHandling);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set QUERY STRING NULL HANDLING by the comment annotation to \"{queryStringNullHandling}\"")]
    public static partial void CommentSetQueryStringNullHandling(this ILogger logger, string description, QueryStringNullHandling queryStringNullHandling);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set Content-Type HEADER by the comment annotation to \"{headerValue}\"")]
    public static partial void CommentSetContentType(this ILogger logger, string description, string headerValue);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set {headerName} HEADER by the comment annotation to \"{headerValue}\"")]
    public static partial void CommentSetHeader(this ILogger logger, string description, string headerName, string headerValue);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set LOGIN RESPONSE by the comment annotation.")]
    public static partial void CommentSetLogin(this ILogger logger, string description);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set LOGOUT RESPONSE by the comment annotation.")]
    public static partial void CommentSetLogout(this ILogger logger, string description);

    [LoggerMessage(Level = LogLevel.Error, Message = "{description} is designated as login routine and it returns a status field that is not either boolean or numeric.")]
    public static partial void WrongStatusType(this ILogger logger, string description);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Endpoints are using Login and the DefaultAuthenticationType is null. DefaultAuthenticationType was set to {name}")]
    public static partial void SetDefaultAuthenticationType(this ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set BUFFER ROWS by the comment annotation to {parsedBuffer}")]
    public static partial void CommentBufferRows(this ILogger logger, string description, string parsedBuffer);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid buffer rows '{buffer}' in comment for {description}. Using the default buffer rows '{defaultBufferRows}'")]
    public static partial void InvalidBufferRows(this ILogger logger, string buffer, string description, ulong defaultBufferRows);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set RAW MODE by the comment annotation.")]
    public static partial void CommentSetRawMode(this ILogger logger, string description);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set SEPARATOR by the comment annotation to {value}.")]
    public static partial void CommentSetRawValueSeparator(this ILogger logger, string description, string value);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set NEW LINE by the comment annotation to {value}.")]
    public static partial void CommentSetRawNewLineSeparator(this ILogger logger, string description, string value);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set COLUMN NAMES by the comment annotation.")]
    public static partial void CommentRawSetColumnNames(this ILogger logger, string description);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{description} has set CACHED by the comment annotation, routine doesn't return a single value. Routine will NOT be cached. Only single values can be cached.")]
    public static partial void CommentInvalidCache(this ILogger logger, string description);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{description} has set CACHED PARAMETER NAME to {param} by the comment annotation, but that parameter doesn't exists on this routine either converted or original. This cache parameter will be ignored.")]
    public static partial void CommentInvalidCacheParam(this ILogger logger, string description, string param);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set CACHED with parameters {cachedParams} by the comment annotation.")]
    public static partial void CommentCached(this ILogger logger, string description, IEnumerable<string> cachedParams);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set SECURITY SENSITIVE to TRUE by the comment annotation.")]
    public static partial void CommentSecuritySensitive(this ILogger logger, string description);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set CACHE EXPIRES IN to {value} by the comment annotation.")]
    public static partial void CommentCacheExpiresIn(this ILogger logger, string description, TimeSpan value);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{description} can't set CACHE EXPIRES IN value by the comment annotation. Invalid interval value: {value}")]
    public static partial void InvalidCacheExpiresIn(this ILogger logger, string description, string value);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{description} tried to set CONNECTION NAME to {conn} but that connection could not be found in the ConnectionStrings dictionary.")]
    public static partial void CommentInvalidConnectionName(this ILogger logger, string description, string conn);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{description} tried to set CONNECTION NAME but the connection name was not initialized. Did you forget to set the connection name?")]
    public static partial void CommentEmptyConnectionName(this ILogger logger, string description);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set CONNECTION NAME to {conn} by the comment annotation.")]
    public static partial void CommentConnectionName(this ILogger logger, string description, string conn);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has been DISABLED by the comment annotation.")]
    public static partial void CommentDisabled(this ILogger logger, string description);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{description} parameter name {param} does not exists in parameter collection either as original or translated name. HASH OF could not be set by the comment annotation.")]
    public static partial void CommentParamNotExistsCantHash(this ILogger logger, string description, string param);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set parameter {param1} as HASH OF parameter {param2} by the comment annotation.")]
    public static partial void CommentParamIsHashOf(this ILogger logger, string description, string param1, string param2);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set UPLOAD by the comment annotation.")]
    public static partial void CommentUpload(this ILogger logger, string description);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{description} parameter name {param} does not exists in parameter collection either as original or translated name. UPLOAD METADATA PARAMETER could not be set by the comment annotation.")]
    public static partial void CommentUploadWrongMetadataParam(this ILogger logger, string description, string param);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set UPLOAD METADATA PARAMETER to {param} by the comment annotation.")]
    public static partial void CommentUploadMetadataParam(this ILogger logger, string description, string param);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Password verification failed for attempted login: path={path} userId={userId}, username={userName}")]
    public static partial void VerifyPasswordFailed(this ILogger logger, string? path, string? userId, string? userName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{description} upload handler {handler} doesn't exists and it will be ignored. Available handlers: {handlers}")]
    public static partial void CommentUploadHandlerNotExists(this ILogger logger, string description, string handler, HashSet<string> handlers);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{description} upload handler are not defined.")]
    public static partial void CommentUploadNoHandlers(this ILogger logger, string description);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set to use the first available UPLOAD HANDLER {handler} by the comment annotation.")]
    public static partial void CommentUploadFirstAvaialbleHandler(this ILogger logger, string description, string? handler);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set to use UPLOAD HANDLER {handler} by the comment annotation.")]
    public static partial void CommentUploadSingleHandler(this ILogger logger, string description, string? handler);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set to use multiple UPLOAD HANDLERES {handlers} by the comment annotation.")]
    public static partial void CommentUploadHandlers(this ILogger logger, string description, string[]? handlers);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Login endpoint {endpoint} failed to locate the password parameter in parameter collection {parameters}. Password parameter is the first that contains \"{contains}\" text in parameter name.")]
    public static partial void CantFindPasswordParameter(this ILogger logger, string endpoint, string?[]? parameters, string contains);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set {paramName} TO {paramValue}.")]
    public static partial void CommentSetCustomParemeter(this ILogger logger, string description, string paramName, string paramValue);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set USER CONTEXT to TRUE by the comment annotation.")]
    public static partial void CommentUserContext(this ILogger logger, string description);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set USER PARAMETERS to TRUE by the comment annotation.")]
    public static partial void CommentUserParameters(this ILogger logger, string description);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set INFO EVENTS STREAMING PATH to {path} by the comment annotation.")]
    public static partial void CommentInfoStreamingPath(this ILogger logger, string description, string? path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set INFO EVENTS STREAMING SCOPE to {scope} by the comment annotation.")]
    public static partial void CommentInfoStreamingScope(this ILogger logger, string description, InfoEventsScope scope);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set INFO EVENTS STREAMING SCOPE to AUTHENTICATED with roles {roles} by the comment annotation.")]
    public static partial void CommentInfoStreamingScopeRoles(this ILogger logger, string description, HashSet<string> roles);
    
    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has BASIC AUTH enabled by the comment annotation.")]
    public static partial void BasicAuthEnabled(this ILogger logger, string description);
    
    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has ser BASIC AUTH REALM to {realm} by the comment annotation.")]
    public static partial void BasicAuthRealmSet(this ILogger logger, string description, string realm);
    
    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set BASIC AUTH CHALLENGE COMMAND set to {command} by the comment annotation.")]
    public static partial void BasicAuthChallengeCommandSet(this ILogger logger, string description, string command);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set BASIC AUTH USER set to {user} by the comment annotation.")]
    public static partial void BasicAuthUserAdded(this ILogger logger, string description, string user);
    
    [LoggerMessage(Level = LogLevel.Warning, Message = "{description} has failed to set BASIC AUTH USER by the comment annotation.")]
    public static partial void BasicAuthUserFailed(this ILogger logger, string description);
    
    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set RETRY STRATEGY to {name} by the comment annotation.")]
    public static partial void RetryStrategySet(this ILogger logger, string description, string name);
    
    [LoggerMessage(Level = LogLevel.Warning, Message = "{description} has failed to set RETRY STRATEGY by the comment annotation. Strategy {name} not found in the list of strategies.")]
    public static partial void RetryStrategyNotFound(this ILogger logger, string description, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to open connection on attempt {attempt}. Retrying in {delay}ms. Error: {error}")]
    public static partial void FailedToOpenConnectionRetry(this ILogger logger, int attempt, double delay, string error);
    
    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to open connection after {totalAttempts} attempts.")]
    public static partial void FailedToOpenConnectionAfter(this ILogger logger, Exception exception, int totalAttempts);
    
    [LoggerMessage(Level = LogLevel.Error, Message = "Non-retryable error occurred while opening connection: {error}")]
    public static partial void FailedToOpenNonRetryableConnection(this ILogger logger, Exception exception, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to execute command on attempt {attempt}. Retrying in {delay}ms. Error: {error}")]
    public static partial void FailedToExecuteCommandRetry(this ILogger logger, int attempt, double delay, string error);
    
    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to execute command after {totalAttempts} attempts.")]
    public static partial void FailedToExecuteCommandAfter(this ILogger logger, Exception exception, int totalAttempts);
    [LoggerMessage(Level = LogLevel.Error, Message = "Non-retryable error occurred while executing command: {error}")]
    public static partial void FailedToExecuteNonRetryableCommand(this ILogger logger, Exception exception, string error);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{description} has set RATE LIMITER POLICY NAME to {name} by the comment annotation.")]
    public static partial void RateLimiterPolicySet(this ILogger logger, string description, string name);
}
