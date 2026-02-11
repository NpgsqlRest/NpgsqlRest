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
        ("npgsqlrest --version --json", "Show version information as machine-readable JSON."),
        ("npgsqlrest -h, --help", "Show command line help."),
        ("npgsqlrest [files...] [--key=value] --config", "Dump current configuration as JSON to console and exit. Syntax highlighted in terminal, plain JSON when piped."),
        ("npgsqlrest [files...] --validate", "Validate configuration keys and test database connection, then exit."),
        ("npgsqlrest [files...] --validate --json", "Validate and output results as machine-readable JSON."),
        ("npgsqlrest --config-schema", "Output JSON Schema for appsettings.json. Syntax highlighted in terminal, plain JSON when piped."),
        ("npgsqlrest --annotations", "Output all supported comment annotations as JSON. Syntax highlighted in terminal, plain JSON when piped."),
        ("npgsqlrest [files...] --endpoints", "Connect to database, discover endpoints, output as JSON, then exit. Syntax highlighted in terminal, plain JSON when piped."),
        (" ", " "),
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
    bool jsonOutput = args.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));
    if (jsonOutput)
    {
        var versionJson = CliJson.GetVersionJson();
        Console.WriteLine(versionJson.ToJsonString(CliJson.JsonOptions));
        return;
    }

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
        ("SpreadCheetah", System.Reflection.Assembly.GetAssembly(typeof(SpreadCheetah.Spreadsheet))?.GetName().Version?.ToString() ?? "-"),
        ("Serilog.AspNetCore", System.Reflection.Assembly.GetAssembly(typeof(Serilog.AspNetCore.RequestLoggingOptions))?.GetName().Version?.ToString() ?? "-"),
        ("Serilog.Sinks.OpenTelemetry", System.Reflection.Assembly.GetAssembly(typeof(Serilog.Sinks.OpenTelemetry.OpenTelemetrySinkOptions))?.GetName().Version?.ToString() ?? "-"),
        ("System.Text.Json", System.Reflection.Assembly.GetAssembly(typeof(System.Text.Json.JsonCommentHandling))?.GetName().Version?.ToString() ?? "-"),
        ("StackExchange.Redis", System.Reflection.Assembly.GetAssembly(typeof(StackExchange.Redis.ConnectionMultiplexer))?.GetName().Version?.ToString() ?? "-"),
        ("Microsoft.Extensions.Caching.Hybrid", System.Reflection.Assembly.GetAssembly(typeof(Microsoft.Extensions.Caching.Hybrid.HybridCache))?.GetName().Version?.ToString() ?? "-"),
        ("Microsoft.Extensions.Caching.StackExchangeRedis", System.Reflection.Assembly.GetAssembly(typeof(Microsoft.Extensions.Caching.StackExchangeRedis.RedisCache))?.GetName().Version?.ToString() ?? "-"),
        ("Microsoft.AspNetCore.Authentication.JwtBearer", System.Reflection.Assembly.GetAssembly(typeof(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions))?.GetName().Version?.ToString() ?? "-"),
        ("AspNetCore.HealthChecks.NpgSql", System.Reflection.Assembly.GetAssembly(typeof(HealthChecks.NpgSql.NpgSqlHealthCheck))?.GetName().Version?.ToString() ?? "-"),
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

if (args.Length >= 1 && string.Equals(args[0], "--config-schema", StringComparison.CurrentCultureIgnoreCase))
{
    var schema = ConfigSchemaGenerator.Generate();
    new Out().JsonHighlight(schema.ToJsonString(CliJson.JsonOptions));
    return;
}

if (args.Length >= 1 && string.Equals(args[0], "--annotations", StringComparison.CurrentCultureIgnoreCase))
{
    var annotations = NpgsqlRest.Defaults.DefaultCommentParser.GetAnnotationReference();
    new Out().JsonHighlight(annotations.ToJsonString(CliJson.JsonOptions));
    return;
}

var config = new Config();
var builder = new Builder(config);
var appInstance = new App(config, builder);

try
{
    config.Build(args, ["--encrypt"]);
}
catch (ArgumentException ex)
{
    var _ = new Out();
    _.Logo();
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine(ex.Message);
    Console.ResetColor();
    Console.Error.WriteLine();
    Console.Error.WriteLine("Run with --help for usage information.");
    return;
}

if (args.Any(a => string.Equals(a, "--config", StringComparison.CurrentCultureIgnoreCase)))
{
    new Out().JsonHighlight(config.Serialize());
    return;
}

if (args.Any(a => string.Equals(a, "--validate", StringComparison.CurrentCultureIgnoreCase)))
{
    bool jsonOutput = args.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));
    var (valMode, valWarnings) = config.ValidateConfigKeys();
    bool configValid = valWarnings.Count == 0;

    // Test database connection
    string? connectionError = null;
    var connStr = config.GetConfigStr("Default", config.Cfg.GetSection("ConnectionStrings"));
    if (connStr is not null)
    {
        try
        {
            using var conn = new NpgsqlConnection(connStr);
            conn.Open();
            conn.Close();
        }
        catch (Exception ex)
        {
            connectionError = ex.Message;
        }
    }

    bool valid = configValid && connectionError is null;
    if (jsonOutput)
    {
        var result = new System.Text.Json.Nodes.JsonObject
        {
            ["valid"] = valid,
            ["configValid"] = configValid,
            ["validationMode"] = valMode,
            ["warnings"] = new System.Text.Json.Nodes.JsonArray(
                valWarnings.Select(w => (System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create(w)!).ToArray()),
            ["connectionTest"] = connectionError is null ? "ok" : connectionError
        };
        Console.WriteLine(result.ToJsonString(CliJson.JsonOptions));
    }
    else
    {
        var _ = new Out();
        _.Logo();
        if (valWarnings.Count > 0)
        {
            _.Line($"Config validation ({valMode}): {valWarnings.Count} unknown key(s):", ConsoleColor.Yellow);
            foreach (var warning in valWarnings)
            {
                _.Line($"  - {warning}", ConsoleColor.Yellow);
            }
        }
        else
        {
            _.Line("Config validation: OK", ConsoleColor.Green);
        }
        if (connectionError is not null)
        {
            _.Line($"Connection test: FAILED - {connectionError}", ConsoleColor.Red);
        }
        else if (connStr is not null)
        {
            _.Line("Connection test: OK", ConsoleColor.Green);
        }
        else
        {
            _.Line("Connection test: No default connection string configured", ConsoleColor.Yellow);
        }
    }

    Environment.ExitCode = valid ? 0 : 1;
    return;
}

var cmdRetryOpts = builder.BuildCommandRetryOptions();
RetryStrategy? cmdRetryStrategy = null;
if (cmdRetryOpts.Enabled && string.IsNullOrEmpty(cmdRetryOpts.DefaultStrategy))
{
    cmdRetryOpts.Strategies.TryGetValue(cmdRetryOpts.DefaultStrategy, out cmdRetryStrategy);
}

bool endpointsMode = args.Any(a => string.Equals(a, "--endpoints", StringComparison.OrdinalIgnoreCase));

builder.BuildInstance();
builder.Instance.Services.AddRouting();
if (!endpointsMode)
{
    builder.BuildLogger(cmdRetryStrategy);
    builder.ClientLogger?.LogInformation("NpgsqlRest version {version}",
        System.Reflection.Assembly.GetAssembly(typeof(Program))?.GetName()?.Version?.ToString() ?? "-");
}

// Validate configuration keys against known defaults
var (validationMode, configWarnings) = config.ValidateConfigKeys();
if (configWarnings.Count > 0)
{
    if (string.Equals(validationMode, "Error", StringComparison.OrdinalIgnoreCase))
    {
        foreach (var warning in configWarnings)
        {
            builder.ClientLogger?.LogError("Unknown configuration key: {Key}", warning);
        }
        return;
    }
    foreach (var warning in configWarnings)
    {
        builder.ClientLogger?.LogWarning("Unknown configuration key: {Key}", warning);
    }
}

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
var securityHeadersConfig = builder.BuildSecurityHeaders();
var forwardedHeadersEnabled = builder.BuildForwardedHeaders();
var (healthChecksEnabled, healthChecksCacheDuration) = builder.BuildHealthChecks(connectionString);
var (statsEnabled, statsCacheDuration) = builder.BuildStats();

// Add OutputCache if any endpoint uses caching
if ((healthChecksEnabled && healthChecksCacheDuration.HasValue) || (statsEnabled && statsCacheDuration.HasValue))
{
    builder.Instance.Services.AddOutputCache();
}

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
        builder.ClientLogger?.LogInformation(
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
}, forwardedHeadersEnabled);

// Security headers middleware (after HSTS, before other middleware)
appInstance.ConfigureSecurityHeaders(app, securityHeadersConfig, antiForgeryUsed);

var (authenticationOptions, authCfg) = appInstance.CreateNpgsqlRestAuthenticationOptions(app, dataProtectionName);

if (usingCors)
{
    app.UseCors();
}
if (compressionEnabled)
{
    app.UseResponseCompression();
}
if ((healthChecksEnabled && healthChecksCacheDuration.HasValue) || (statsEnabled && statsCacheDuration.HasValue))
{
    app.UseOutputCache();
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

    // LoggerName defaults to "NpgsqlRest" - allows separate log level configuration from client
    LogConnectionNoticeEvents = config.GetConfigBool("LogConnectionNoticeEvents", config.NpgsqlRestCfg, true),
    LogCommands = config.GetConfigBool("LogCommands", config.NpgsqlRestCfg),
    LogCommandParameters = config.GetConfigBool("LogCommandParameters", config.NpgsqlRestCfg),
    LogConnectionNoticeEventsMode = logConnectionNoticeEventsMode,
    DebugLogEndpointCreateEvents = config.GetConfigBool("DebugLogEndpointCreateEvents", config.NpgsqlRestCfg, true),
    DebugLogCommentAnnotationEvents = config.GetConfigBool("DebugLogCommentAnnotationEvents", config.NpgsqlRestCfg, true),
    
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
    EndpointCreateHandlers = appInstance.CreateCodeGenHandlers(connectionString, args),
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
    TableFormatHandlers = appInstance.CreateTableFormatHandlers(),
};

app.UseNpgsqlRest(options);

if (endpointsMode)
{
    using var ms = new MemoryStream();
    using (var writer = new System.Text.Json.Utf8JsonWriter(ms, new System.Text.Json.JsonWriterOptions { Indented = true }))
    {
        EndpointCapture.WriteEndpointsJson(writer);
    }
    new Out().JsonHighlight(System.Text.Encoding.UTF8.GetString(ms.ToArray()));
    return;
}

if (builder.ExternalAuthConfig?.Enabled is true)
{
    new ExternalAuth(
        builder.ExternalAuthConfig,
        connectionString,
        app,
        options,
        cmdRetryStrategy,
        logConnectionNoticeEventsMode,
        builder.ClientLogger);
}

if (builder.PasskeyConfig?.Enabled is true)
{
    app.UsePasskeyAuth(
        builder.PasskeyConfig,
        options,
        cmdRetryOpts,
        logConnectionNoticeEventsMode,
        builder.ClientLogger);
}

if (builder.BearerTokenConfig is not null)
{
    new TokenRefreshAuth(builder.BearerTokenConfig, app, builder.ClientLogger);
}

if (builder.JwtTokenConfig is not null)
{
    new JwtRefreshAuth(builder.JwtTokenConfig, app, builder.ClientLogger);
}

// Health check endpoints
if (healthChecksEnabled)
{
    var healthCfg = config.Cfg.GetSection("HealthChecks");
    var path = config.GetConfigStr("Path", healthCfg) ?? "/health";
    var readyPath = config.GetConfigStr("ReadyPath", healthCfg) ?? "/health/ready";
    var livePath = config.GetConfigStr("LivePath", healthCfg) ?? "/health/live";
    var healthRequireAuthorization = config.GetConfigBool("RequireAuthorization", healthCfg);
    var rateLimiterPolicy = config.GetConfigStr("RateLimiterPolicy", healthCfg);

    var healthEndpoint = app.MapHealthChecks(path);
    var readyEndpoint = app.MapHealthChecks(readyPath, new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready")
    });
    var liveEndpoint = app.MapHealthChecks(livePath, new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => false // Always healthy if app is running
    });

    if (healthRequireAuthorization)
    {
        healthEndpoint.AddEndpointFilter(AuthorizationFilter);
        readyEndpoint.AddEndpointFilter(AuthorizationFilter);
        liveEndpoint.AddEndpointFilter(AuthorizationFilter);

        static async ValueTask<object?> AuthorizationFilter(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            if (context.HttpContext.User?.Identity?.IsAuthenticated is false)
            {
                return Results.Problem(statusCode: 401, title: "Unauthorized");
            }
            return await next(context);
        }
    }

    if (rateLimiterPolicy is not null)
    {
        healthEndpoint.RequireRateLimiting(rateLimiterPolicy);
        readyEndpoint.RequireRateLimiting(rateLimiterPolicy);
        liveEndpoint.RequireRateLimiting(rateLimiterPolicy);
    }

    if (healthChecksCacheDuration.HasValue)
    {
        healthEndpoint.CacheOutput(policy => policy.Expire(healthChecksCacheDuration.Value).SetVaryByQuery([]));
        readyEndpoint.CacheOutput(policy => policy.Expire(healthChecksCacheDuration.Value).SetVaryByQuery([]));
        liveEndpoint.CacheOutput(policy => policy.Expire(healthChecksCacheDuration.Value).SetVaryByQuery([]));
    }
}

// Stats endpoints
if (statsEnabled)
{
    var statsCfg = config.Cfg.GetSection("Stats");
    var routinesPath = config.GetConfigStr("RoutinesStatsPath", statsCfg) ?? "/stats/routines";
    var tablesPath = config.GetConfigStr("TablesStatsPath", statsCfg) ?? "/stats/tables";
    var indexesPath = config.GetConfigStr("IndexesStatsPath", statsCfg) ?? "/stats/indexes";
    var activityPath = config.GetConfigStr("ActivityPath", statsCfg) ?? "/stats/activity";
    var statsRequireAuthorization = config.GetConfigBool("RequireAuthorization", statsCfg);
    var statsAuthorizedRoles = config.GetConfigEnumerable("AuthorizedRoles", statsCfg)?.ToArray();
    var statsRoleClaimType = authenticationOptions.DefaultRoleClaimType;
    var rateLimiterPolicy = config.GetConfigStr("RateLimiterPolicy", statsCfg);
    var outputFormat = config.GetConfigStr("OutputFormat", statsCfg) ?? "html";
    var schemaSimilarTo = config.GetConfigStr("SchemaSimilarTo", statsCfg);

    // Resolve connection string for stats endpoints
    var statsConnectionName = config.GetConfigStr("ConnectionName", statsCfg);
    var statsConnectionString = connectionString;
    if (statsConnectionName is not null && connectionStrings?.TryGetValue(statsConnectionName, out var namedConnStr) == true)
    {
        statsConnectionString = namedConnStr;
    }

    var routinesEndpoint = app.MapGet(routinesPath, async (HttpContext context) =>
        await StatsEndpoints.HandleRoutinesStats(context, statsConnectionString!, outputFormat, schemaSimilarTo, statsRequireAuthorization, statsAuthorizedRoles, statsRoleClaimType, builder.ClientLogger));

    var tablesEndpoint = app.MapGet(tablesPath, async (HttpContext context) =>
        await StatsEndpoints.HandleTablesStats(context, statsConnectionString!, outputFormat, schemaSimilarTo, statsRequireAuthorization, statsAuthorizedRoles, statsRoleClaimType, builder.ClientLogger));

    var indexesEndpoint = app.MapGet(indexesPath, async (HttpContext context) =>
        await StatsEndpoints.HandleIndexesStats(context, statsConnectionString!, outputFormat, schemaSimilarTo, statsRequireAuthorization, statsAuthorizedRoles, statsRoleClaimType, builder.ClientLogger));

    var activityEndpoint = app.MapGet(activityPath, async (HttpContext context) =>
        await StatsEndpoints.HandleActivityStats(context, statsConnectionString!, outputFormat, statsRequireAuthorization, statsAuthorizedRoles, statsRoleClaimType, builder.ClientLogger));

    if (rateLimiterPolicy is not null)
    {
        routinesEndpoint.RequireRateLimiting(rateLimiterPolicy);
        tablesEndpoint.RequireRateLimiting(rateLimiterPolicy);
        indexesEndpoint.RequireRateLimiting(rateLimiterPolicy);
        activityEndpoint.RequireRateLimiting(rateLimiterPolicy);
    }

    if (statsCacheDuration.HasValue)
    {
        routinesEndpoint.CacheOutput(policy => policy.Expire(statsCacheDuration.Value).SetVaryByQuery([]));
        tablesEndpoint.CacheOutput(policy => policy.Expire(statsCacheDuration.Value).SetVaryByQuery([]));
        indexesEndpoint.CacheOutput(policy => policy.Expire(statsCacheDuration.Value).SetVaryByQuery([]));
        activityEndpoint.CacheOutput(policy => policy.Expire(statsCacheDuration.Value).SetVaryByQuery([]));
    }
}

app.Run();
