using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using NpgsqlRest;
using NpgsqlRest.Defaults;
using NpgsqlRestClient;
using NpgsqlRestClient.Fido2;

using Npgsql;
using NpgsqlRest.CrudSource;
using NpgsqlRest.HttpFiles;
using NpgsqlRest.TsClient;

Stopwatch sw = new();
sw.Start();

if (args.Length >= 1 && (string.Equals(args[0], "-h", StringComparison.CurrentCultureIgnoreCase) ||
    string.Equals(args[0], "--help", StringComparison.CurrentCultureIgnoreCase) ||
    string.Equals(args[0], "/?", StringComparison.CurrentCultureIgnoreCase)))
{
    var _ = new Out();
    _.Logo();
    _.Line("Usage:");
    _.Line([
        ("npgsqlrest", "Run with the optional default configuration files: appsettings.json and appsettings.Development.json. If these file are not found, default configuration setting is used (see https://github.com/NpgsqlRest/NpgsqlRest/blob/master/NpgsqlRestClient/appsettings.json)."),
        ("npgsqlrest [files...]", "Run with the custom configuration files. All configuration files are required. Any configuration values will override default values in order of appearance."),
        ("npgsqlrest [file1 -o file2...]", "Use the -o switch to mark the next configuration file as optional. The first file after the -o switch is optional."),
        ("npgsqlrest [file1 --optional file2...]", "Use --optional switch to mark the next configuration file as optional. The first file after the --optional switch is optional."),
        ("Note:", "Values in the later file will override the values in the previous one."),
        (" ", " "),
        ("npgsqlrest [--key=value]", "Override the configuration with this key with a new value (case insensitive, use : to separate sections). "),
        (" ", " "),
        ("npgsqlrest -v, --version", "Show version information."),
        ("npgsqlrest -h, --help", "Show command line help."),
        ("npgsqlrest --config", "Dump current configuration to console and exit."),
        ("npgsqlrest --hash [value]", "Hash value with default hasher and print to console."),
        ("npgsqlrest --basic_auth [username] [password]", "Print out basic basic auth header value in format 'Authorization: Basic base64(username:password)'."),
        ("npgsqlrest --encrypt [value]", "Encrypt string using default data protection and print to console."),
        (" ", " "),
        (" ", " "),
        ("Examples:", " "),
        ("Example: use two config files", "npgsqlrest appsettings.json appsettings.Development.json"),
        ("Example: second config file optional", "npgsqlrest appsettings.json -o appsettings.Development.json"),
        ("Example: override ApplicationName config", "npgsqlrest --applicationname=Test"),
        ("Example: override Auth:CookieName config", "npgsqlrest --auth:cookiename=Test"),
        (" ", " "),
        ]);
    return;
}

if (args.Length >= 1 && (string.Equals(args[0], "-v", StringComparison.CurrentCultureIgnoreCase) ||
    string.Equals(args[0], "--version", StringComparison.CurrentCultureIgnoreCase) ||
    string.Equals(args[0], "/v", StringComparison.CurrentCultureIgnoreCase)))
{
    var _ = new Out();
    _.Logo();
    var versions = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.Split(' ');
    _.Line("Versions:");
    _.Line([
        (versions[0], versions[1]),
        (" ", " "),
        ("NpgsqlRest", System.Reflection.Assembly.GetAssembly(typeof(NpgsqlRestOptions))?.GetName().Version?.ToString() ?? "-"),
        ("NpgsqlRestClient", System.Reflection.Assembly.GetAssembly(typeof(Program))?.GetName().Version?.ToString() ?? "-"),
        ("NpgsqlRest.HttpFiles", System.Reflection.Assembly.GetAssembly(typeof(HttpFileOptions))?.GetName().Version?.ToString() ?? "-"),
        ("NpgsqlRest.TsClient", System.Reflection.Assembly.GetAssembly(typeof(TsClientOptions))?.GetName().Version?.ToString() ?? "-"),
        ("NpgsqlRest.CrudSource", System.Reflection.Assembly.GetAssembly(typeof(CrudSource))?.GetName().Version?.ToString() ?? "-"),
        ("NpgsqlRest.OpenApi", System.Reflection.Assembly.GetAssembly(typeof(NpgsqlRest.OpenAPI.OpenApiOptions))?.GetName().Version?.ToString() ?? "-"),
        (" ", " "),
        ("Npgsql", System.Reflection.Assembly.GetAssembly(typeof(NpgsqlConnection))?.GetName()?.Version?.ToString() ?? "-"),
        ("ExcelDataReader", System.Reflection.Assembly.GetAssembly(typeof(ExcelDataReader.IExcelDataReader))?.GetName().Version?.ToString() ?? "-"),
        ("Serilog.AspNetCore", System.Reflection.Assembly.GetAssembly(typeof(Serilog.AspNetCore.RequestLoggingOptions))?.GetName().Version?.ToString() ?? "-"),
        ("Serilog.Sinks.OpenTelemetry", System.Reflection.Assembly.GetAssembly(typeof(Serilog.Sinks.OpenTelemetry.OpenTelemetrySinkOptions))?.GetName().Version?.ToString() ?? "-"),
        ("System.Text.Json", System.Reflection.Assembly.GetAssembly(typeof(System.Text.Json.JsonCommentHandling))?.GetName().Version?.ToString() ?? "-"),
        ("StackExchange.Redis", System.Reflection.Assembly.GetAssembly(typeof(StackExchange.Redis.ConnectionMultiplexer))?.GetName().Version?.ToString() ?? "-"),
        ("Microsoft.Extensions.Caching.Hybrid", System.Reflection.Assembly.GetAssembly(typeof(Microsoft.Extensions.Caching.Hybrid.HybridCache))?.GetName().Version?.ToString() ?? "-"),
        ("Microsoft.Extensions.Caching.StackExchangeRedis", System.Reflection.Assembly.GetAssembly(typeof(Microsoft.Extensions.Caching.StackExchangeRedis.RedisCache))?.GetName().Version?.ToString() ?? "-"),
        ("Microsoft.AspNetCore.Authentication.JwtBearer", System.Reflection.Assembly.GetAssembly(typeof(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions))?.GetName().Version?.ToString() ?? "-"),
        (" ", " "),
        ("CurrentDirectory", Directory.GetCurrentDirectory()),
        ("BaseDirectory", AppContext.BaseDirectory)
    ]);
    _.NL();
    return;
}

if (args.Length >= 2 && string.Equals(args[0], "--hash", StringComparison.CurrentCultureIgnoreCase))
{
    new Out().Logo();
    Console.ForegroundColor = ConsoleColor.Red;
    var hasher = new NpgsqlRest.Auth.PasswordHasher();
    Console.WriteLine(hasher.HashPassword(args[1]));
    Console.ResetColor();
    return;
}

if (args.Length >= 3 && string.Equals(args[0], "--basic_auth", StringComparison.CurrentCultureIgnoreCase))
{
    new Out().Logo();
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine(string.Concat("Authorization: Basic ", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{args[1]}:{args[2]}"))));
    Console.ResetColor();
    return;
}

var config = new Config();
var builder = new Builder(config);
var appInstance = new App(config, builder);

config.Build(args,["--encrypt"]);

if (args.Length >= 1 && string.Equals(args[0], "--config", StringComparison.CurrentCultureIgnoreCase))
{
    new Out().Logo();
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine(config.Serialize());
    return;
}

var cmdRetryOpts = builder.BuildCommandRetryOptions();
RetryStrategy? cmdRetryStrategy = null;
if (cmdRetryOpts.Enabled && string.IsNullOrEmpty(cmdRetryOpts.DefaultStrategy))
{
    cmdRetryOpts.Strategies.TryGetValue(cmdRetryOpts.DefaultStrategy, out cmdRetryStrategy);
}

builder.BuildInstance();
builder.Instance.Services.AddRouting();
builder.BuildLogger(cmdRetryStrategy);
builder.Logger?.LogInformation("NpgsqlRest version {version}",
    System.Reflection.Assembly.GetAssembly(typeof(Program))?.GetName()?.Version?.ToString() ?? "-");
var errorHandlingOptions = builder.BuildErrorHandlingOptions();
var (rateLimiterDefaultPolicy, rateLimiterEnabled) = builder.BuildRateLimiter();

var (connectionString, retryOpts) = builder.BuildConnectionString();
if (connectionString is null)
{
    return;
}
var connectionStrings = 
    config.GetConfigBool("UseMultipleConnections", config.NpgsqlRestCfg, false) ? 
        builder.BuildConnectionStringDict() : 
        null;

var dataProtectionName = builder.BuildDataProtection(cmdRetryStrategy);
builder.BuildAuthentication();
builder.BuildPasskeyAuthentication();
var usingCors = builder.BuildCors();
var compressionEnabled = builder.ConfigureResponseCompression();
var antiForgeryUsed = builder.ConfigureAntiForgery();
var cacheType = builder.ConfigureCacheServices();

WebApplication app = builder.Build();

if (rateLimiterEnabled)
{
    app.UseRateLimiter();
}

// dump encrypted text and exit
if (args.Length >= 1 && string.Equals(args[0], "--encrypt", StringComparison.CurrentCultureIgnoreCase))
{
    new Out().Logo();
    Console.ForegroundColor = ConsoleColor.Red;
    if (dataProtectionName is null)
    {
        Console.WriteLine("Data protection is not configured, cannot encrypt.");
        Console.ResetColor();
        return;
    }
    var provider = app.Services.GetRequiredService<IDataProtectionProvider>();
    var protector = provider.CreateProtector(dataProtectionName);
    Console.WriteLine(protector.Protect(args[1]));
    return;
}

appInstance.Configure(app, () =>
{
    sw.Stop();
    var message = config.Cfg?.GetSection("StartupMessage")?.Value ?? "Started in {time}, listening on {urls}, version {version}";
    if (string.IsNullOrEmpty(message) is false)
    {
        builder.Logger?.LogInformation(
            Formatter.FormatString(
                message.AsSpan(), 
                new Dictionary<string, string> { 
                    ["time"] = sw.ToString(),
                    ["urls"] = string.Join(";", app.Urls.ToArray()),
                    ["version"] = System.Reflection.Assembly.GetAssembly(typeof(Program))?.GetName()?.Version?.ToString() ?? "-",
                    ["environment"] = builder.Instance.Environment.EnvironmentName,
                    ["application"] = builder.Instance.Environment.ApplicationName
                }).ToString());
    }
});

var (authenticationOptions, authCfg) = appInstance.CreateNpgsqlRestAuthenticationOptions(app, dataProtectionName);

if (usingCors)
{
    app.UseCors();
}
if (compressionEnabled)
{
    app.UseResponseCompression();
}
if (antiForgeryUsed)
{
    app.UseAntiforgery();
}
appInstance.ConfigureStaticFiles(app, authenticationOptions);

// Build data source (handles both single-host and multi-host connections)
await using var dataSource = builder.BuildMainDataSource(connectionString);

// Build additional multi-host data sources for named connections
var dataSources = builder.BuildDataSources(connectionString);
if (dataSources.Count > 0)
{
    // Register disposal of additional data sources
    app.Lifetime.ApplicationStopping.Register(() =>
    {
        foreach (var ds in dataSources.Values)
        {
            ds.Dispose();
        }
    });
}

var logConnectionNoticeEventsMode = config.GetConfigEnum<PostgresConnectionNoticeLoggingMode?>("LogConnectionNoticeEventsMode", config.NpgsqlRestCfg) ?? PostgresConnectionNoticeLoggingMode.FirstStackFrameAndMessage;

appInstance.ConfigureThreadPool();

NpgsqlRestOptions options = new()
{
    DataSource = dataSource,
    DataSources = dataSources.Count > 0 ? dataSources : null,
    ServiceProviderMode = ServiceProviderObject.None,
    ConnectionStrings = connectionStrings,
    ConnectionRetryOptions = retryOpts,
    CommandTimeout = Parser.ParsePostgresInterval(config.GetConfigStr("CommandTimeout", config.NpgsqlRestCfg)),
    MetadataQueryConnectionName = config.GetConfigStr("MetadataQueryConnectionName", config.ConnectionSettingsCfg),
    MetadataQuerySchema = config.GetConfigStr("MetadataQuerySchema", config.ConnectionSettingsCfg) ?? "public",
    SchemaSimilarTo = config.GetConfigStr("SchemaSimilarTo", config.NpgsqlRestCfg),
    SchemaNotSimilarTo = config.GetConfigStr("SchemaNotSimilarTo", config.NpgsqlRestCfg),
    IncludeSchemas = config.GetConfigEnumerable("IncludeSchemas", config.NpgsqlRestCfg)?.ToArray(),
    ExcludeSchemas = config.GetConfigEnumerable("ExcludeSchemas", config.NpgsqlRestCfg)?.ToArray(),
    NameSimilarTo = config.GetConfigStr("NameSimilarTo", config.NpgsqlRestCfg),
    NameNotSimilarTo = config.GetConfigStr("NameNotSimilarTo", config.NpgsqlRestCfg),
    IncludeNames = config.GetConfigEnumerable("IncludeNames", config.NpgsqlRestCfg)?.ToArray(),
    ExcludeNames = config.GetConfigEnumerable("ExcludeNames", config.NpgsqlRestCfg)?.ToArray(),
    UrlPathPrefix = config.GetConfigStr("UrlPathPrefix", config.NpgsqlRestCfg) ?? "/api",
    UrlPathBuilder = config.GetConfigBool("KebabCaseUrls", config.NpgsqlRestCfg, true) ? DefaultUrlBuilder.CreateUrl : appInstance.CreateUrl,
    NameConverter = config.GetConfigBool("CamelCaseNames", config.NpgsqlRestCfg, true) ? DefaultNameConverter.ConvertToCamelCase : n => n?.Trim('"'),
    RequiresAuthorization = config.GetConfigBool("RequiresAuthorization", config.NpgsqlRestCfg, true),

    LoggerName = config.GetConfigStr("ApplicationName", config.Cfg),
    LogConnectionNoticeEvents = config.GetConfigBool("LogConnectionNoticeEvents", config.NpgsqlRestCfg, true),
    LogCommands = config.GetConfigBool("LogCommands", config.NpgsqlRestCfg),
    LogCommandParameters = config.GetConfigBool("LogCommandParameters", config.NpgsqlRestCfg),
    LogConnectionNoticeEventsMode = logConnectionNoticeEventsMode,
    
    DefaultHttpMethod = config.GetConfigEnum<Method?>("DefaultHttpMethod", config.NpgsqlRestCfg),
    DefaultRequestParamType = config.GetConfigEnum<RequestParamType?>("DefaultRequestParamType", config.NpgsqlRestCfg),
    QueryStringNullHandling = config.GetConfigEnum<QueryStringNullHandling?>("QueryStringNullHandling", config.NpgsqlRestCfg) ?? QueryStringNullHandling.Ignore,
    TextResponseNullHandling = config.GetConfigEnum<TextResponseNullHandling?>("TextResponseNullHandling", config.NpgsqlRestCfg) ?? TextResponseNullHandling.EmptyString,
    CommentsMode = config.GetConfigEnum<CommentsMode?>("CommentsMode", config.NpgsqlRestCfg) ?? CommentsMode.OnlyWithHttpTag,
    RequestHeadersMode = config.GetConfigEnum<RequestHeadersMode?>("RequestHeadersMode", config.NpgsqlRestCfg) ?? RequestHeadersMode.Parameter,
    RequestHeadersContextKey = config.GetConfigStr("RequestHeadersContextKey", config.NpgsqlRestCfg) ?? "request.headers",
    RequestHeadersParameterName = config.GetConfigStr("RequestHeadersParameterName", config.NpgsqlRestCfg) ?? "_headers",

    EndpointCreated = appInstance.CreateEndpointCreatedHandler(authCfg),
    ValidateParameters = null,
    ErrorHandlingOptions = errorHandlingOptions,
    BeforeConnectionOpen = appInstance.BeforeConnectionOpen(connectionString, authenticationOptions),
    AuthenticationOptions = authenticationOptions,
    EndpointCreateHandlers = appInstance.CreateCodeGenHandlers(connectionString),
    CustomRequestHeaders = builder.GetCustomHeaders(),
    ExecutionIdHeaderName = config.GetConfigStr("ExecutionIdHeaderName", config.NpgsqlRestCfg) ?? "X-NpgsqlRest-ID",
    DefaultSseEventNoticeLevel = config.GetConfigEnum<PostgresNoticeLevels?>("DefaultServerSentEventsEventNoticeLevel", config.NpgsqlRestCfg) ?? PostgresNoticeLevels.INFO,
    SseResponseHeaders = builder.GetSseResponseHeaders(),

    RoutineSources = appInstance.CreateRoutineSources(),
    UploadOptions = appInstance.CreateUploadOptions(),
    
    CacheOptions = builder.BuildCacheOptions(app, cacheType),
    DefaultRateLimitingPolicy = rateLimiterDefaultPolicy,
    HttpClientOptions = builder.BuildHttpClientOptions(),
    ProxyOptions = builder.BuildProxyOptions(),
    ValidationOptions = builder.BuildValidationOptions(),
};

app.UseNpgsqlRest(options);

if (builder.ExternalAuthConfig?.Enabled is true)
{
    new ExternalAuth(
        builder.ExternalAuthConfig,
        connectionString,
        app,
        options,
        cmdRetryStrategy,
        logConnectionNoticeEventsMode);
}

if (builder.PasskeyConfig?.Enabled is true)
{
    app.UsePasskeyAuth(
        builder.PasskeyConfig,
        options,
        cmdRetryOpts,
        logConnectionNoticeEventsMode);
}

if (builder.BearerTokenConfig is not null)
{
    new TokenRefreshAuth(builder.BearerTokenConfig, app, builder.Logger);
}

if (builder.JwtTokenConfig is not null)
{
    new JwtRefreshAuth(builder.JwtTokenConfig, app, builder.Logger);
}

app.Run();
