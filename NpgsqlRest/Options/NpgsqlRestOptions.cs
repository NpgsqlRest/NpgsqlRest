using Microsoft.Extensions.Primitives;
using Npgsql;
using NpgsqlRest.Auth;
using NpgsqlRest.Defaults;

namespace NpgsqlRest;

/// <summary>
/// Options for the NpgsqlRest middleware.
/// </summary>
public class NpgsqlRestOptions
{
    /// <summary>
    /// Gets the current instance of NpgsqlRestOptions. This is set automatically by the UseNpgsqlRest middleware.
    /// </summary>
    public static NpgsqlRestOptions Options { get; internal set; } = null!;
    
    /// <summary>
    /// Static Logger instance. This is set automatically by the UseNpgsqlRest middleware.
    /// </summary>
    public static ILogger? Logger { get; internal set; } = null!;

    /// <summary>
    /// Options for the NpgsqlRest middleware with default values.
    /// </summary>
    public NpgsqlRestOptions()
    {
        // Default values are set directly in property initializers
    }

    /// <summary>
    /// Options for the NpgsqlRest middleware.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string</param>
    public NpgsqlRestOptions(string connectionString)
    {
        ConnectionString = connectionString;
        DataSource = null;
    }

    /// <summary>
    /// Options for the NpgsqlRest middleware with connection string and data source.
    /// </summary>
    /// <param name="dataSource">NpgsqlDataSource instance</param>
    public NpgsqlRestOptions(NpgsqlDataSource dataSource)
    {
        ConnectionString = null;
        DataSource = dataSource;
    }

    /// <summary>
    /// The connection string to the database. This is the optional value if the `DataSource` option is set. 
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The data source object that will be used to create a connection to the database. 
    /// If this option is set, the connection string will be ignored. 
    /// </summary>
    public NpgsqlDataSource? DataSource { get; set; }

    /// <summary>
    /// Dictionary of connection strings. The key is the connection name and the value is the connection string.
    /// This option is used when the RoutineEndpoint has a connection name defined.
    /// This allows the middleware to use different connection strings for different routines.
    /// For example, some routines might use the primary database connection string, while others might use a read-only connection string from the replica servers.
    /// </summary>
    public IDictionary<string, string>? ConnectionStrings { get; set; }

    /// <summary>
    /// Dictionary of data sources by connection name. This is used for multi-host connection support.
    /// When a connection name is specified in a routine endpoint, the middleware will first check this dictionary for a data source.
    /// If not found, it falls back to the ConnectionStrings dictionary.
    /// Use this for multi-host failover/load-balancing scenarios where you need NpgsqlMultiHostDataSource with specific target session attributes.
    /// </summary>
    public IDictionary<string, NpgsqlDataSource>? DataSources { get; set; }
    
    /// <summary>
    /// The connection name in ConnectionStrings dictionary that will be used to execute the metadata query. If this value is null, the default connection string or data source will be used.
    /// </summary>
    public string? MetadataQueryConnectionName { get; set; } = null;
    
    /// <summary>
    /// Set the search path to this schema that contains the metadata query function. Default is `public`.
    /// </summary>
    public string? MetadataQuerySchema { get; set; } = "public";

    /// <summary>
    /// Retry options for the connection opening.
    /// </summary>
    public ConnectionRetryOptions ConnectionRetryOptions { get; set; } = new();
    
    /// <summary>
    /// Command retry options for all commands.
    /// </summary>
    public CommandRetryOptions CommandRetryOptions { get; set; } = new();

    /// <summary>
    /// Filter schema names [similar to](https://www.postgresql.org/docs/current/functions-matching.html#FUNCTIONS-SIMILARTO-REGEXP) this parameter or `null` to ignore this parameter.
    /// </summary>
    public string? SchemaSimilarTo { get; set; }

    /// <summary>
    /// Filter schema names NOT [similar to](https://www.postgresql.org/docs/current/functions-matching.html#FUNCTIONS-SIMILARTO-REGEXP) this parameter or `null` to ignore this parameter.
    /// </summary>
    public string? SchemaNotSimilarTo { get; set; }

    /// <summary>
    /// List of schema names to be included or  `null` to ignore this parameter.
    /// </summary>
    public string[]? IncludeSchemas { get; set; }

    /// <summary>
    /// List of schema names to be excluded or  `null` to ignore this parameter. 
    /// </summary>
    public string[]? ExcludeSchemas { get; set; }

    /// <summary>
    /// Filter names [similar to](https://www.postgresql.org/docs/current/functions-matching.html#FUNCTIONS-SIMILARTO-REGEXP) this parameter or `null` to ignore this parameter.
    /// </summary>
    public string? NameSimilarTo { get; set; }

    /// <summary>
    /// Filter names NOT [similar to](https://www.postgresql.org/docs/current/functions-matching.html#FUNCTIONS-SIMILARTO-REGEXP) this parameter or `null` to ignore this parameter.
    /// </summary>
    public string? NameNotSimilarTo { get; set; }

    /// <summary>
    /// List of names to be included or `null` to ignore this parameter.
    /// </summary>
    public string[]? IncludeNames { get; set; }

    /// <summary>
    /// List of names to be excluded or `null` to ignore this parameter.
    /// </summary>
    public string[]? ExcludeNames { get; set; }

    /// <summary>
    /// The URL prefix string for every URL created by the default URL builder or `null` to ignore the URL prefix.
    /// </summary>
    public string? UrlPathPrefix { get; set; } = "/api";

    /// <summary>
    /// Custom function delegate that receives routine and options parameters and returns constructed URL path string for routine. Default the default URL builder that transforms snake case names to kebab case names.
    /// </summary>
    public Func<Routine, NpgsqlRestOptions, string> UrlPathBuilder { get; set; } = DefaultUrlBuilder.CreateUrl;

    /// <summary>
    /// Set to NpgsqlDataSource or NpgsqlConnection to use the connection objects from the service provider. Default is None.
    /// </summary>
    public ServiceProviderObject ServiceProviderMode { get; set; } = ServiceProviderObject.None;

    /// <summary>
    /// Callback function that is executed just after the new endpoint is created. Set the RoutineEndpoint to null to disable endpoint.
    /// </summary>
    public Action<RoutineEndpoint?>? EndpointCreated { get; set; } = null;

    /// <summary>
    /// Custom function callback that receives names from PostgreSQL (parameter names, column names, etc), and is expected to return the same or new name. It offers an opportunity to convert names based on certain conventions. The default converter converts snake case names into camel case names.
    /// </summary>
    public Func<string?, string?> NameConverter { get; set; } = DefaultNameConverter.ConvertToCamelCase;

    /// <summary>
    /// When set to true, it will force all created endpoints to require authorization. Authorization requirements for individual endpoints can be changed with the `EndpointCreated` function callback, or by using comment annotations.
    /// </summary>
    public bool RequiresAuthorization { get; set; }
    
    /// <summary>
    ///  Change the logger name with this option. 
    /// </summary>
    public string? LoggerName { get; set; } = "NpgsqlRest";
    
    /// <summary>
    /// When this value is true, all connection events are logged (depending on the level). This is usually triggered by the PostgreSQL [`RAISE` statements](https://www.postgresql.org/docs/current/plpgsql-errors-and-messages.html). Set to false to disable logging these events.
    /// </summary>
    public bool LogConnectionNoticeEvents { get; set; } = true;

    /// <summary>
    /// MessageOnly - Log only connection messages.
    /// FirstStackFrameAndMessage - Log first stack frame and the message.
    /// FullStackAndMessage - Log full stack trace and message.
    /// </summary>
    public PostgresConnectionNoticeLoggingMode LogConnectionNoticeEventsMode { get; set; } = PostgresConnectionNoticeLoggingMode.FirstStackFrameAndMessage;

    /// <summary>
    /// Set this option to true to log information for every executed command and query (including parameters and parameter values).
    /// </summary>
    public bool LogCommands { get; set; }

    /// <summary>
    /// Set this option to true to include parameter values when logging commands. This only applies when `LogCommands` is true.
    /// </summary>
    public bool LogCommandParameters { get; set; }

    /// <summary>
    /// Sets the wait time (in seconds) on database commands, before terminating the attempt to execute a command and generating an error. This value when it is not null will override the `NpgsqlCommand` which is 30 seconds. Command timeout property for individual endpoints can be changed with the `EndpointCreated` function callback, or by using comment annotations.
    /// </summary>
    public TimeSpan? CommandTimeout { get; set; }

    /// <summary>
    /// When not null, forces a method type for all created endpoints. Method types are `GET`, `PUT`, `POST`, `DELETE`, `HEAD`, `OPTIONS`, `TRACE`, `PATCH` or `CONNECT`. When this value is null (default), the method type is always `GET` when the routine volatility option is not volatile or the routine name starts with, `get_`, contains `_get_` or ends with `_get` (case insensitive). Otherwise, it is `POST`. This option for individual endpoints can be changed with the `EndpointCreated` function callback, or by using comment annotations.
    /// </summary>
    public Method? DefaultHttpMethod { get; set; }

    /// <summary>
    /// When not null, sets the request parameter position (request parameter types) for all created endpoints. Values are `QueryString` (parameters are sent using query string) or `BodyJson` (parameters are sent using JSON request body). When this value is null (default), request parameter type is `QueryString` for all `GET` and `DELETE` endpoints, otherwise, request parameter type is `BodyJson`. This option for individual endpoints can be changed with the `EndpointCreated` function callback, or by using comment annotations.
    /// </summary>
    public RequestParamType? DefaultRequestParamType { get; set; }

    /// <summary>
    /// Custom parameter validation method. When this callback option is not null, it will be executed for every database parameter created. The input structure will contain a current HTTP context that offers the opportunity to alter the response and cancel the request: If the current HTTP response reference has started or the status code is different than 200 OK, command execution will be canceled and the response will be returned.
    /// </summary>
    public Action<ParameterValidationValues>? ValidateParameters { get; set; }

    /// <summary>
    /// Custom parameter validation method, asynchronous version. When this callback option is not null, it will be executed for every database parameter created. The input structure will contain a current HTTP context that offers the opportunity to alter the response and cancel the request: If the current HTTP response reference has started or the status code is different than 200 OK, command execution will be canceled and the response will be returned.
    /// </summary>
    public Func<ParameterValidationValues, Task>? ValidateParametersAsync { get; set; }

    /// <summary>
    /// Configure how the comment annotations will behave. `Ignore` will create all endpoints and ignore comment annotations. `ParseAll` (default) will create all endpoints and parse comment annotations to alter the endpoint. `OnlyWithHttpTag` will only create endpoints that contain the `HTTP` tag in the comments and then parse comment annotations.
    /// </summary>
    public CommentsMode CommentsMode { get; set; } = CommentsMode.OnlyWithHttpTag;

    /// <summary>
    /// Configure how to send request headers to PostgreSQL routines execution:
    /// - `Ignore` (default) don't send any request headers to routines.
    /// - `Context` sets a context variable for the current session `context.headers` containing JSON string with current request headers. This executes `set_config('context.headers', headers, false)` before any routine executions.
    /// - `Parameter` sends request headers to the routine parameter defined with the `RequestHeadersParameterName` option. Parameter with this name must exist, must be one of the JSON or text types and must have the default value defined.
    ///
    /// This option for individual endpoints can be changed with the `EndpointCreated` function callback, or by using comment annotations.
    /// </summary>
    public RequestHeadersMode RequestHeadersMode { get; set; } = RequestHeadersMode.Ignore;

    /// <summary>
    /// Name of the context variable that will receive the request headers when RequestHeadersMode is set to Context.
    /// </summary>
    public string RequestHeadersContextKey { get; set; } = "request.headers";

    /// <summary>
    /// Sets a parameter name that will receive a request headers JSON when the `Parameter` value is used in `RequestHeadersMode` options. A parameter with this name must exist, must be one of the JSON or text types and must have the default value defined. This option for individual endpoints can be changed with the `EndpointCreated` function callback, or by using comment annotations.
    /// </summary>
    public string RequestHeadersParameterName { get; set; } = "headers";

    /// <summary>
    /// Callback, if defined will be executed after all endpoints are created and receive an array of routine info and endpoint info tuples `(Routine routine, RoutineEndpoint endpoint)`. Used mostly for code generation.
    /// </summary>
    public Action<RoutineEndpoint[]>? EndpointsCreated { get; set; }
    
    /// <summary>
    /// Callback, if defined will be executed after every route handler is created and receive the `RouteHandlerBuilder` instance and the `RoutineEndpoint` instance. Used mostly to add custom metadata to the endpoint or add custom filters or other configurations.
    /// </summary>
    public Action<RouteHandlerBuilder, RoutineEndpoint>? RouteHandlerCreated { get; set; }
    
    /// <summary>
    /// Asynchronous callback function that will be called after every database command is created and before it has been executed. It receives a tuple parameter with routine info, created command and current HTTP context. Command instance and HTTP context offer the opportunity to execute the command and return a completely different, custom response format.
    /// </summary>
    public Func<RoutineEndpoint, NpgsqlCommand, HttpContext, Task>? CommandCallbackAsync { get; set; }

    /// <summary>
    /// List of `IEndpointCreateHandler` type handlers executed sequentially after endpoints are created. Used to add the different code generation plugins.
    /// </summary>
    public IEnumerable<IEndpointCreateHandler> EndpointCreateHandlers { get; set; } = Array.Empty<IEndpointCreateHandler>();

    /// <summary>
    /// Action callback executed after routine sources are created and before they are processed into endpoints. Receives a parameter with the list of `IRoutineSource` instances. This list will always contain a single item - functions and procedures source. Use this callback to modify the routine source list and add new sources from plugins.
    /// </summary>
    public Action<List<IRoutineSource>> SourcesCreated { get; set; } = s => { };

    /// <summary>
    /// Sets the default behavior of plain text responses when the execution returns the `NULL` value from the database. `EmptyString` (default) returns an empty string response with status code 200 OK. `NullLiteral` returns a string literal `NULL` with the status code 200 OK. `NoContent` returns status code 204 NO CONTENT. This option for individual endpoints can be changed with the `EndpointCreated` function callback, or by using comment annotations.
    /// </summary>
    public TextResponseNullHandling TextResponseNullHandling { get; set; } = TextResponseNullHandling.EmptyString;

    /// <summary>
    /// Sets the default behavior on how to pass the `NULL` values with query strings. `EmptyString` empty string values are interpreted as `NULL` values. This limits sending empty strings via query strings. `NullLiteral` literal string values `NULL` (case insensitive) are interpreted as `NULL` values. `Ignore` (default) `NULL` values are ignored, query string receives only empty strings. This option for individual endpoints can be changed with the `EndpointCreated` function callback, or by using comment annotations.
    /// </summary>
    public QueryStringNullHandling QueryStringNullHandling { get; set; } = QueryStringNullHandling.Ignore;

    /// <summary>
    /// The number of rows to buffer in the string builder before sending the response. The default is 25.
    /// This applies to rows in JSON object array when returning records from the database.
    /// Set to 0 to disable buffering and write a response for each row.
    /// Set to 1 to buffer the entire array (all rows).
    /// Notes: 
    /// - Disabling buffering can have a slight negative impact on performance since buffering is far less expensive than writing to the response stream.
    /// - Setting higher values can have a negative impact on memory usage, especially when returning large datasets.
    /// </summary>
    public ulong BufferRows { get; set; } = 25;

    /// <summary>
    /// Default Authentication Options
    /// </summary>
    public NpgsqlRestAuthenticationOptions AuthenticationOptions { get; set; } = new();
    
    /// <summary>
    /// Map PostgreSql Error Codes (see https://www.postgresql.org/docs/current/errcodes-appendix.html) to HTTP Status Codes
    /// </summary>
    public ErrorHandlingOptions ErrorHandlingOptions { get; set; } = new();
    
    /// <summary>
    /// Callback executed immediately before connection is opened. Use this callback to adjust connection settings such as application name.
    /// </summary>
    public Action<NpgsqlConnection, RoutineEndpoint, HttpContext>? BeforeConnectionOpen { get; set; }

    /// <summary>
    /// Custom request headers dictionary that will be added to NpgsqlRest requests. 
    /// Note: these values are added to the request headers dictionary before they are sent as a context or parameter to the PostgreSQL routine and as such not visible to the browser debugger.
    /// </summary>
    public Dictionary<string, StringValues> CustomRequestHeaders { get; set; } = [];

    /// <summary>
    /// Routine sources default list.
    /// </summary>
    public List<IRoutineSource> RoutineSources { get; set; } = [new RoutineSource()];
    
    /// <summary>
    /// Cache options
    /// </summary>
    public CacheOptions CacheOptions { get; set; } = new CacheOptions();
    
    /// <summary>
    /// Default Upload Options
    /// </summary>
    public NpgsqlRestUploadOptions UploadOptions { get; set; } = new();

    /// <summary>
    /// Name of the request ID header that will be used to track requests. This is used to correlate requests with streaming connection ids.
    /// </summary>
    public string ExecutionIdHeaderName { get; set; } = "X-NpgsqlRest-ID";
    
    /// <summary>
    /// Server-sent events notice levels that will be sent to connected clients. When SSE path is set, generate SSE events for PostgreSQL notice messages with this level or higher.
    /// </summary>
    public PostgresNoticeLevels DefaultSseEventNoticeLevel { get; set; } = PostgresNoticeLevels.INFO;

    /// <summary>
    /// Collection of custom server-sent events response headers that will be added to the response when connected to the endpoint that is configured to return server-sent events.
    /// </summary>
    public Dictionary<string, StringValues> SseResponseHeaders { get; set; } = [];
    
    /// <summary>
    /// Default rate limiting policy for all requests. Policy must be configured within application rate limiting options.
    /// This can be overridden by comment annotations in the database or setting policy for specific endpoints.
    /// </summary>
    public string? DefaultRateLimitingPolicy { get; set; } = null;
    
    public HttpClientOptions HttpClientOptions { get; set; } = new();

    /// <summary>
    /// Options for reverse proxy functionality.
    /// When enabled, endpoints marked with 'proxy' annotation will forward requests to another URL.
    /// </summary>
    public ProxyOptions ProxyOptions { get; set; } = new();
}
