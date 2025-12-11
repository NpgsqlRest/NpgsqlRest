using System.Collections.Frozen;
using System.IO.Compression;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Npgsql;
using NpgsqlRest;
using Serilog;
using Serilog.Extensions.Logging;
using Serilog.Sinks.OpenTelemetry;

namespace NpgsqlRestClient;

public class Builder
{
    private readonly Config _config;
    
    public Builder(Config config)
    {
        _config = config;
    }
    
    public WebApplicationBuilder Instance { get; private set; }  = default!;

    // public bool LogToConsole { get; private set; } = false;
    // public bool LogToFile { get; private set; } = false;
    // public bool LogToPostgres { get; private set; } = false;
    // public bool LogToOpenTelemetry { get; private set; } = false;
    
    public bool LoggingEnabled { get; private set; } = false;
    
    public Microsoft.Extensions.Logging.ILogger? Logger { get; private set; } = null;
    public bool UseHttpsRedirection { get; private set; } = false;
    public bool UseHsts { get; private set; } = false;
    public BearerTokenConfig? BearerTokenConfig { get; private set; } = null;
    public string? ConnectionString { get; private set; } = null;
    public string? ConnectionName { get; private set; } = null;
    public ExternalAuthConfig? ExternalAuthConfig { get; private set; } = null;
    public bool SslEnabled { get; private set; } = false;

    public void BuildInstance()
    {
        var staticFilesCfg = _config.Cfg.GetSection("StaticFiles");
        string? webRootPath = staticFilesCfg is not null && _config.GetConfigBool("Enabled", staticFilesCfg) is true ? _config.GetConfigStr("RootPath", staticFilesCfg) ?? "wwwroot" : null;

        var options = new WebApplicationOptions()
        {
            ApplicationName = _config.GetConfigStr("ApplicationName") ?? Path.GetFileName(Environment.CurrentDirectory),
            WebRootPath = webRootPath,
            EnvironmentName = _config.GetConfigStr("EnvironmentName") ?? "Production",
        };
        Instance = WebApplication.CreateEmptyBuilder(options);
        Instance.WebHost.UseKestrelCore();

        var kestrelConfig = _config.Cfg.GetSection("Kestrel");
        if (kestrelConfig is not null && kestrelConfig.Exists())
        {
            Instance.WebHost.ConfigureKestrel((context, options) =>
            {
                options.Configure(kestrelConfig);

                options.DisableStringReuse = _config.GetConfigBool("DisableStringReuse", kestrelConfig, options.DisableStringReuse);
                options.AllowAlternateSchemes = _config.GetConfigBool("AllowAlternateSchemes", kestrelConfig, options.AllowAlternateSchemes);
                options.AllowSynchronousIO = _config.GetConfigBool("AllowSynchronousIO", kestrelConfig, options.AllowSynchronousIO);
                options.AllowResponseHeaderCompression = _config.GetConfigBool("AllowResponseHeaderCompression", kestrelConfig, options.AllowResponseHeaderCompression);
                options.AddServerHeader = _config.GetConfigBool("AddServerHeader", kestrelConfig, options.AddServerHeader);
                options.AllowHostHeaderOverride = _config.GetConfigBool("AllowSynchronousIO", kestrelConfig, options.AllowHostHeaderOverride);

                var limitsSection = kestrelConfig.GetSection("Limits");
                if (limitsSection.Exists())
                {
                    limitsSection.Bind(options.Limits);
                }
            });
        }

        var urls = _config.GetConfigStr("Urls");
        if (urls is not null)
        {
            Instance.WebHost.UseUrls(urls.Split(';'));
        }
        else
        {
           Instance.WebHost.UseUrls("http://localhost:8080");
        }

        var ssqlCfg = _config.Cfg.GetSection("Ssl");
        if (_config.Exists(ssqlCfg) is true)
        {
            if (_config.GetConfigBool("Enabled", ssqlCfg) is true)
            {
                SslEnabled = true;
                Instance.WebHost.UseKestrelHttpsConfiguration();
                UseHttpsRedirection = _config.GetConfigBool("UseHttpsRedirection", ssqlCfg, true);
                UseHsts = _config.GetConfigBool("UseHsts", ssqlCfg, true);
            }
        }
        else
        {
            UseHttpsRedirection = false;
            UseHsts = false;
        }
    }

    public WebApplication Build() => Instance.Build();

    public void BuildLogger(RetryStrategy? cmdRetryStrategy)
    {
        var logCfg = _config.Cfg.GetSection("Log");
        Logger = null;
        var logToConsole = _config.GetConfigBool("ToConsole", logCfg, true);
        var logToFile = _config.GetConfigBool("ToFile", logCfg);
        var filePath = _config.GetConfigStr("FilePath", logCfg) ?? "logs/log.txt";
        var logToPostgres = _config.GetConfigBool("ToPostgres", logCfg);
        var postgresCommand = _config.GetConfigStr("PostgresCommand", logCfg);
        
        var logToOpenTelemetry = _config.GetConfigBool("ToOpenTelemetry", logCfg);

        if (
            logToConsole is true || 
            (logToFile is true) || 
            (logToPostgres is true && postgresCommand is not null) || 
            (logToOpenTelemetry is true))
        {
            LoggingEnabled = true;
            var loggerConfig = new LoggerConfiguration().MinimumLevel.Verbose();
            bool npgsqlRestAdded = false;
            bool systemAdded = false;
            bool microsoftAdded = false;
            var appName = _config.GetConfigStr("ApplicationName", _config.Cfg);
            var envName = _config.GetConfigStr("EnvironmentName", _config.Cfg) ?? "Production";
            string npgsqlRestLoggerName = string.IsNullOrEmpty(appName) ? "NpgsqlRest": appName;
            
            foreach (var level in logCfg?.GetSection("MinimalLevels")?.GetChildren() ?? [])
            {
                var key = level.Key;
                var value = _config.GetEnum<Serilog.Events.LogEventLevel?>(level.Value);
                if (value is not null && key is not null)
                {
                    
                    if (string.Equals(key, "NpgsqlRest", StringComparison.OrdinalIgnoreCase))
                    {
                        loggerConfig.MinimumLevel.Override(npgsqlRestLoggerName, value.Value);
                        npgsqlRestAdded = true;
                    }
                    if (string.Equals(key, "System", StringComparison.OrdinalIgnoreCase))
                    {
                        loggerConfig.MinimumLevel.Override(key, value.Value);
                        systemAdded = true;
                    }
                    if (string.Equals(key, "Microsoft", StringComparison.OrdinalIgnoreCase))
                    {
                        loggerConfig.MinimumLevel.Override(key, value.Value);
                        microsoftAdded = true;
                    }
                    else
                    {
                        loggerConfig.MinimumLevel.Override(key, value.Value);
                    }
                }
            }
            if (npgsqlRestAdded is false)
            {
                loggerConfig.MinimumLevel.Override(npgsqlRestLoggerName, Serilog.Events.LogEventLevel.Information);
            }
            if (systemAdded is false)
            {
                loggerConfig.MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning);
            }
            if (microsoftAdded is false)
            {
                loggerConfig.MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning);
            }

            string outputTemplate = _config.GetConfigStr("OutputTemplate", logCfg) ?? 
                "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj} [{SourceContext}]{NewLine}{Exception}";
            
            List<string> logDebug = new(4);
            
            if (logToConsole is true)
            {
                var minLevel = _config.GetConfigEnum<Serilog.Events.LogEventLevel?>("ConsoleMinimumLevel", logCfg) ?? Serilog.Events.LogEventLevel.Verbose;
                loggerConfig = loggerConfig.WriteTo.Console(
                    restrictedToMinimumLevel: minLevel,
                    outputTemplate: outputTemplate,
                    theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code);
                
                logDebug.Add(string.Concat("Console (minimum level: ", minLevel.ToString(), ")"));
            }
            if (logToFile is true)
            {
                var minLevel = _config.GetConfigEnum<Serilog.Events.LogEventLevel?>("FileMinimumLevel", logCfg) ?? Serilog.Events.LogEventLevel.Verbose;
                loggerConfig = loggerConfig.WriteTo.File(
                    restrictedToMinimumLevel: minLevel,
                    path: filePath ?? "logs/log.txt",
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: _config.GetConfigInt("FileSizeLimitBytes", logCfg) ?? 30000000,
                    retainedFileCountLimit: _config.GetConfigInt("RetainedFileCountLimit", logCfg) ?? 30,
                    rollOnFileSizeLimit: _config.GetConfigBool("RollOnFileSizeLimit", logCfg, defaultVal: true),
                    outputTemplate: outputTemplate);
                
                logDebug.Add(string.Concat("Rolling File (minimum level: ", minLevel.ToString(), ")"));
            }
            if (logToPostgres is true && postgresCommand is not null)
            {
                var minLevel = _config.GetConfigEnum<Serilog.Events.LogEventLevel?>("PostgresMinimumLevel", logCfg) ?? Serilog.Events.LogEventLevel.Verbose;
                loggerConfig = loggerConfig.WriteTo.Postgres(
                    postgresCommand, 
                    restrictedToMinimumLevel: minLevel,
                    connectionString: ConnectionString,
                    cmdRetryStrategy);
                
                logDebug.Add(string.Concat("PostgreSQL Database (minimum level: ", minLevel.ToString(), ", command: ", postgresCommand, ")"));
            }

            if (logToOpenTelemetry)
            {
                var minLevel = _config.GetConfigEnum<Serilog.Events.LogEventLevel?>("OTLPMinimumLevel", logCfg) ?? Serilog.Events.LogEventLevel.Verbose;
                var endpoint = _config.GetConfigStr("OTLPEndpoint", logCfg) ?? "http://localhost:4317";
                var protocol = _config.GetConfigEnum<OtlpProtocol?>("OTLPProtocol", logCfg) ?? OtlpProtocol.Grpc;
                loggerConfig = loggerConfig.WriteTo.OpenTelemetry(options => 
                {
                    options.Endpoint = endpoint;
                    options.Protocol = protocol;
                    var formatDict = new Dictionary<string, string>
                    {
                        ["application"] = appName ?? "NpgsqlRest",
                        ["environment"] = envName
                    };
                    if (logCfg is not null)
                    {
                        options.ResourceAttributes =
                            (_config.GetConfigDict(logCfg.GetSection("OTLResourceAttributes")) ??
                             new Dictionary<string, string>())
                            .ToDictionary(kv => kv.Key,
                                kv => (object)Formatter.FormatString(kv.Value.AsSpan(), formatDict).ToString());
                        var headers = _config.GetConfigDict(logCfg.GetSection("OTLPHeaders"));
                        if (headers is not null && headers.Count > 0)
                        {
                            options.Headers = headers;
                        }
                    }
                    options.LevelSwitch = new Serilog.Core.LoggingLevelSwitch(minLevel);
                });
                
                logDebug.Add(string.Concat("OpenTelemetry (minimum level: ", minLevel.ToString(), ", endpoint: ", endpoint, ", protocol: ", protocol.ToString(), ")"));
            }
            var serilog = loggerConfig.CreateLogger();
            
            Logger = string.IsNullOrEmpty(appName) ? 
                new SerilogLoggerFactory(serilog.ForContext("SourceContext", "NpgsqlRest")).CreateLogger("NpgsqlRest") : 
                new SerilogLoggerFactory(serilog.ForContext("SourceContext", appName)).CreateLogger(appName);

            Instance.Host.UseSerilog(serilog);

            var providerString = _config.Cfg.Providers.Select(p =>
            {
                var str = p.ToString();
                if (p is Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider j)
                {
                    if(File.Exists(j.Source.Path) is false)
                    {
                        str = str?.Replace("(Optional)", "(Missing)");
                    }
                }
                return str;
            }).Aggregate((a, b) => string.Concat(a, ", ", b));
            
            Logger?.LogDebug("----> Starting with configuration(s): {providerString}", providerString);
            if (logDebug.Count > 0)
            {
                Logger?.LogDebug("----> Logging enabled: {logDebug}", string.Join(", ", logDebug));
            }
        }
    }

    public ErrorHandlingOptions BuildErrorHandlingOptions()
    {
        var errConfig = _config.Cfg.GetSection("ErrorHandlingOptions");
        var defaults = new ErrorHandlingOptions();
        if (errConfig.Exists() is false)
        {
            // Always register ProblemDetails for AOT compatibility
            Instance.Services.AddProblemDetails();
            var defaultTimeoutLog = defaults.TimeoutErrorMapping?.ToString() ?? "disabled";
            var defaultPoliciesLog = defaults.ErrorCodePolicies.Count > 0
                ? string.Join("; ", defaults.ErrorCodePolicies.Select(p =>
                    $"{p.Key}: [{string.Join(", ", p.Value.Select(m => $"{m.Key}->{m.Value.StatusCode}"))}]"))
                : "none";
            Logger?.LogDebug("Using default error handling options: DefaultErrorCodePolicy={DefaultErrorCodePolicy}, TimeoutErrorMapping=[{TimeoutErrorMapping}], ErrorCodePolicies=[{ErrorCodePolicies}]",
                defaults.DefaultErrorCodePolicy ?? "Default",
                defaultTimeoutLog,
                defaultPoliciesLog);
            return defaults;
        }

        var removeTypeUrl = _config.GetConfigBool("RemoveTypeUrl", errConfig);
        var removeTraceId = _config.GetConfigBool("RemoveTraceId", errConfig, true);
        // Always register ProblemDetails for AOT compatibility
        Instance.Services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                // Remove the type field completely
                if (removeTypeUrl is true)
                {
                    ctx.ProblemDetails.Type = null;
                }

                // Remove the traceId extension
                if (removeTraceId is true)
                {
                    ctx.ProblemDetails.Extensions.Remove("traceId");
                }
            };
        });

        var result = new ErrorHandlingOptions
        {
            DefaultErrorCodePolicy = _config.GetConfigStr("DefaultErrorCodePolicy", errConfig) ?? defaults.DefaultErrorCodePolicy
        };
        var timeoutErrorMapping = errConfig.GetSection("TimeoutErrorMapping");
        if (timeoutErrorMapping.Exists())
        {
            var statusCode = _config.GetConfigInt("StatusCode", timeoutErrorMapping);
            if (statusCode is not null)
            {
                result.TimeoutErrorMapping = new()
                {
                    StatusCode = statusCode.Value,
                    Title = _config.GetConfigStr("Title", timeoutErrorMapping),
                    Details = _config.GetConfigStr("Details", timeoutErrorMapping),
                    Type = _config.GetConfigStr("Type", timeoutErrorMapping),
                };
            }
        }
        else
        {
            result.TimeoutErrorMapping = defaults.TimeoutErrorMapping;
        }

        result.ErrorCodePolicies = new Dictionary<string, Dictionary<string, ErrorCodeMappingOptions>>();

        var policiesCfg = errConfig.GetSection("ErrorCodePolicies");

        foreach (var policySection in policiesCfg.GetChildren())
        {
            var policy = _config.GetConfigStr("Name", policySection);
            if (string.IsNullOrEmpty(policy))
            {
                continue;
            }

            var mappingCfg = policySection.GetSection("ErrorCodes");
            var mappingDict = new Dictionary<string, ErrorCodeMappingOptions>();
            foreach (var mappingSection in mappingCfg.GetChildren())
            {
                var errorCode = mappingSection.Key;
                if (string.IsNullOrEmpty(errorCode))
                {
                    continue;
                }
                var statusCode = _config.GetConfigInt("StatusCode", mappingSection);
                if (statusCode is null)
                {
                    continue;
                }
                mappingDict.TryAdd(errorCode, new()
                {
                    StatusCode = statusCode.Value,
                    Title = _config.GetConfigStr("Title", mappingSection),
                    Details = _config.GetConfigStr("Details", mappingSection),
                    Type = _config.GetConfigStr("Type", mappingSection),
                });
            }

            if (mappingDict.Count > 0)
            {
                result.ErrorCodePolicies[policy] = mappingDict;
            }
        }
        
        if (result.ErrorCodePolicies.Count == 0)
        {
            result.ErrorCodePolicies = defaults.ErrorCodePolicies;
        }

        // Log error handling options
        var timeoutLog = result.TimeoutErrorMapping is not null
            ? result.TimeoutErrorMapping.ToString()
            : "disabled";

        var policiesLog = result.ErrorCodePolicies.Count > 0
            ? string.Join("; ", result.ErrorCodePolicies.Select(p =>
                $"{p.Key}: [{string.Join(", ", p.Value.Select(m => $"{m.Key}->{m.Value.StatusCode}"))}]"))
            : "none";

        Logger?.LogDebug("Using error handling options: DefaultErrorCodePolicy={DefaultErrorCodePolicy}, RemoveTypeUrl={RemoveTypeUrl}, RemoveTraceId={RemoveTraceId}, TimeoutErrorMapping=[{TimeoutErrorMapping}], ErrorCodePolicies=[{ErrorCodePolicies}]",
            result.DefaultErrorCodePolicy ?? "Default",
            removeTypeUrl,
            removeTraceId,
            timeoutLog,
            policiesLog);

        return result;
    }
    
    public enum DataProtectionStorage
    {
        Default,
        FileSystem,
        Database
    }

    public string? BuildDataProtection(RetryStrategy? cmdRetryStrategy)
    {
        var dataProtectionCfg = _config.Cfg.GetSection("DataProtection");
        if (_config.Exists(dataProtectionCfg) is false || _config.GetConfigBool("Enabled", dataProtectionCfg) is false)
        {
            return null;
        }
        var dataProtectionBuilder = Instance.Services.AddDataProtection();
        
        var encryptionAlgorithm = _config.GetConfigEnum<EncryptionAlgorithm?>("EncryptionAlgorithm", dataProtectionCfg);
        var validationAlgorithm = _config.GetConfigEnum<ValidationAlgorithm?>("ValidationAlgorithm", dataProtectionCfg);
        
        if (encryptionAlgorithm is not null || validationAlgorithm is not null)
        {
            dataProtectionBuilder.UseCryptographicAlgorithms(new AuthenticatedEncryptorConfiguration
            {
                EncryptionAlgorithm = encryptionAlgorithm ?? EncryptionAlgorithm.AES_256_CBC,
                ValidationAlgorithm = validationAlgorithm ?? ValidationAlgorithm.HMACSHA256
            });
        }
        
        DirectoryInfo? dirInfo = null;
        var storage = _config.GetConfigEnum<DataProtectionStorage?>("Storage", dataProtectionCfg) ?? DataProtectionStorage.Default;
        if (storage == DataProtectionStorage.FileSystem)
        {
            var path = _config.GetConfigStr("FileSystemPath", dataProtectionCfg);
            if (string.IsNullOrEmpty(path) is true)
            {
                throw new ArgumentException("FileSystemPath value in DataProtection can't be null or empty when using FileSystem Storage");
            }
            dirInfo = new DirectoryInfo(path);
            dataProtectionBuilder.PersistKeysToFileSystem(dirInfo);
        }
        else if (storage == DataProtectionStorage.Database)
        {
            var getAllElementsCommand = _config.GetConfigStr("GetAllElementsCommand", dataProtectionCfg) ?? "select data from get_all_data_protection_elements()";
            var storeElementCommand = _config.GetConfigStr("StoreElementCommand", dataProtectionCfg) ?? "call store_data_protection_element($1,$2)";
            Instance.Services.Configure<KeyManagementOptions>(options =>
            {
                options.XmlRepository = new DbDataProtection(
                    ConnectionString,
                    getAllElementsCommand,
                    storeElementCommand,
                    cmdRetryStrategy,
                    Logger);
            });
        }

        var expiresInDays = _config.GetConfigInt("DefaultKeyLifetimeDays", dataProtectionCfg) ?? 90;
        dataProtectionBuilder.SetDefaultKeyLifetime(TimeSpan.FromDays(expiresInDays));

        var customAppName = _config.GetConfigStr("CustomApplicationName", dataProtectionCfg);
        if (string.IsNullOrEmpty(customAppName) is true)
        {
            customAppName = Instance.Environment.ApplicationName;
        }
        dataProtectionBuilder.SetApplicationName(customAppName);

        if (storage == DataProtectionStorage.Default)
        {
            Logger?.LogDebug("Using Data Protection for application {customAppName} with default provider. Expiration in {expiresInDays} days.",
                customAppName,
                expiresInDays);
        }
        else
        {
            Logger?.LogDebug($"Using Data Protection for application {{customAppName}} in{(dirInfo is null ? " " : " directory ")}{{dirInfo}}. Expiration in {{expiresInDays}} days.",
                customAppName,
                dirInfo?.FullName ?? "database",
                expiresInDays);
        }

        return customAppName;
    }

    public void BuildAuthentication()
    {
        var authCfg = _config.Cfg.GetSection("Auth");
        bool cookieAuth = false;
        bool bearerTokenAuth = false;
        if (_config.Exists(authCfg) is true)
        {
            cookieAuth = _config.GetConfigBool("CookieAuth", authCfg);
            bearerTokenAuth = _config.GetConfigBool("BearerTokenAuth", authCfg);
        }

        if (cookieAuth is true || bearerTokenAuth is true)
        {
            var cookieScheme = _config.GetConfigStr("CookieAuthScheme", authCfg) ?? CookieAuthenticationDefaults.AuthenticationScheme;
            var tokenScheme = _config.GetConfigStr("BearerTokenAuthScheme", authCfg) ?? BearerTokenDefaults.AuthenticationScheme;
            string defaultScheme = (cookieAuth, bearerTokenAuth) switch
            {
                (true, true) => string.Concat(cookieScheme, "_and_", tokenScheme),
                (true, false) => cookieScheme,
                (false, true) => tokenScheme,
                _ => throw new NotImplementedException(),
            };
            var auth = Instance.Services.AddAuthentication(defaultScheme);

            if (cookieAuth is true)
            {
                var days = _config.GetConfigInt("CookieValidDays", authCfg) ?? 14;
                auth.AddCookie(cookieScheme, options =>
                {
                    options.ExpireTimeSpan = TimeSpan.FromDays(days);
                    var name = _config.GetConfigStr("CookieName", authCfg);
                    if (string.IsNullOrEmpty(name) is false)
                    {
                        options.Cookie.Name = _config.GetConfigStr("CookieName", authCfg);
                    }
                    options.Cookie.Path = _config.GetConfigStr("CookiePath", authCfg);
                    options.Cookie.Domain = _config.GetConfigStr("CookieDomain", authCfg);
                    options.Cookie.MaxAge = _config.GetConfigBool("CookieMultiSessions", authCfg, true) is true ? TimeSpan.FromDays(days) : null;
                    options.Cookie.HttpOnly = _config.GetConfigBool("CookieHttpOnly", authCfg, true) is true;
                });
                Logger?.LogDebug("Using Cookie Authentication with scheme {cookieScheme}. Cookie expires in {days} days.", cookieScheme, days);
            }
            if (bearerTokenAuth is true)
            {
                var hours = _config.GetConfigInt("BearerTokenExpireHours", authCfg) ?? 1;
                BearerTokenConfig = new()
                {
                    Scheme = tokenScheme,
                    RefreshPath = _config.GetConfigStr("BearerTokenRefreshPath", authCfg)
                };
                auth.AddBearerToken(tokenScheme, options =>
                {
                    options.BearerTokenExpiration = TimeSpan.FromHours(hours);
                    options.RefreshTokenExpiration = TimeSpan.FromHours(hours);
                    options.Validate();
                });
                Logger?.LogDebug(
                    "Using Bearer Token Authentication with scheme {tokenScheme}. Token expires in {hours} hours. Refresh path is {RefreshPath}", 
                    tokenScheme, 
                    hours, 
                    BearerTokenConfig.RefreshPath);
            }
            if (cookieAuth is true && bearerTokenAuth is true)
            {
                auth.AddPolicyScheme(defaultScheme, defaultScheme, options =>
                {
                    // runs on each request
                    options.ForwardDefaultSelector = context =>
                    {
                        // filter by auth type
                        string? authorization = context.Request.Headers[HeaderNames.Authorization];
                        if (string.IsNullOrEmpty(authorization) is false && authorization.StartsWith("Bearer "))
                        {
                            return tokenScheme;
                        }
                        // otherwise always check for cookie auth
                        return cookieScheme;
                    };
                });
            }

            if (cookieAuth || bearerTokenAuth)
            {
                ExternalAuthConfig = new ExternalAuthConfig();
                ExternalAuthConfig.Build(authCfg, _config, this);
            }
        }
    }

    public bool BuildCors()
    {
        var corsCfg = _config.Cfg.GetSection("Cors");
        if (_config.Exists(corsCfg) is false || _config.GetConfigBool("Enabled", corsCfg) is false)
        {
            return false;
        }

        string[] allowedOrigins = _config.GetConfigEnumerable("AllowedOrigins", corsCfg)?.ToArray() ?? ["*"];
        var allowedMethods = _config.GetConfigEnumerable("AllowedMethods", corsCfg)?.ToArray() ?? ["*"];
        var allowedHeaders = _config.GetConfigEnumerable("AllowedHeaders", corsCfg)?.ToArray() ?? ["*"];

        Instance.Services.AddCors(options => options.AddDefaultPolicy(policy =>
        {
            Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder builder = policy;
            if (allowedOrigins.Contains("*"))
            {
                builder = builder.AllowAnyOrigin();
                Logger?.LogDebug("CORS policy allows any origins.");
            }
            else
            {
                builder = builder.WithOrigins(allowedOrigins);
                Logger?.LogDebug("CORS policy allows origins: {allowedOrigins}", allowedOrigins);
            }

            if (allowedMethods.Contains("*"))
            {
                builder = builder.AllowAnyMethod();
                Logger?.LogDebug("CORS policy allows any methods.");
            }
            else
            {
                builder = builder.WithMethods(allowedMethods);
                Logger?.LogDebug("CORS policy allows methods: {allowedMethods}", allowedMethods);
            }

            if (allowedHeaders.Contains("*"))
            {
                builder = builder.AllowAnyHeader();
                Logger?.LogDebug("CORS policy allows any headers.");
            }
            else
            {
                builder = builder.WithHeaders(allowedHeaders);
                Logger?.LogDebug("CORS policy allows headers: {allowedHeaders}", allowedHeaders);
            }

            if (_config.GetConfigBool("AllowCredentials", corsCfg, true) is true)
            {
                Logger?.LogDebug("CORS policy allows credentials.");
                builder.AllowCredentials();
            }
            else
            {
                Logger?.LogDebug("CORS policy does not allow credentials.");
            }
            
            var preflightMaxAge = _config.GetConfigInt("PreflightMaxAgeSeconds", corsCfg) ?? 600;
            if (preflightMaxAge > 0)
            {
                Logger?.LogDebug("CORS policy preflight max age is set to {preflightMaxAge} seconds.", preflightMaxAge);
                builder.SetPreflightMaxAge(TimeSpan.FromSeconds(preflightMaxAge));
            }
            else
            {
                Logger?.LogDebug("CORS policy preflight max age is not set.");
            }
        }));
        return true;
    }

    public bool ConfigureResponseCompression()
    {
        var responseCompressionCfg = _config.Cfg.GetSection("ResponseCompression");
        if (_config.Exists(responseCompressionCfg) is false || _config.GetConfigBool("Enabled", responseCompressionCfg) is false)
        {
            return false;
        }

        var useBrotli = _config.GetConfigBool("UseBrotli", responseCompressionCfg, true);
        var useGzipFallback = _config.GetConfigBool("UseGzipFallback", responseCompressionCfg, true);

        if (useBrotli is false && useGzipFallback is false)
        {
            return false;
        }

        Instance.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = _config.GetConfigBool("EnableForHttps", responseCompressionCfg, false); 
            if (useBrotli is true)
            {
                options.Providers.Add<BrotliCompressionProvider>();
            }
            if (useGzipFallback is true)
            {
                options.Providers.Add<GzipCompressionProvider>();
            }
            options.MimeTypes = _config.GetConfigEnumerable("IncludeMimeTypes", responseCompressionCfg)?.ToArray() ?? [
                "text/plain",
                "text/css",
                "application/javascript",
                "text/html",
                "application/xml",
                "text/xml",
                "application/json",
                "text/json",
                "image/svg+xml",
                "font/woff",
                "font/woff2",
                "application/font-woff",
                "application/font-woff2"];
            options.ExcludedMimeTypes = _config.GetConfigEnumerable("ExcludeMimeTypes", responseCompressionCfg)?.ToArray() ?? [];
        });

        var level = _config.GetConfigEnum<CompressionLevel>("CompressionLevel", responseCompressionCfg);
        if (useBrotli is true)
        {
            Instance.Services.Configure<BrotliCompressionProviderOptions>(options =>
            {
                options.Level = level;
            });
        }
        if (useGzipFallback is true)
        {
            Instance.Services.Configure<GzipCompressionProviderOptions>(options =>
            {
                options.Level = level;
            });
        }

        return true;
    }

    public bool ConfigureAntiForgery()
    {
        var antiforgeryCfg = _config.Cfg.GetSection("Antiforgery");
        if (_config.Exists(antiforgeryCfg) is false || _config.GetConfigBool("Enabled", antiforgeryCfg) is false)
        {
            return false;
        }

        Instance.Services.AddAntiforgery(o =>
        {
            var str = _config.GetConfigStr("CookieName", antiforgeryCfg);
            if (string.IsNullOrEmpty(str) is false)
            {
                o.Cookie.Name = str;
            }
            str = _config.GetConfigStr("FormFieldName", antiforgeryCfg);
            if (string.IsNullOrEmpty(str) is false)
            {
                o.FormFieldName = str;
            }
            str = _config.GetConfigStr("HeaderName", antiforgeryCfg);
            if (string.IsNullOrEmpty(str) is false)
            {
                o.HeaderName = str;
            }
            o.SuppressXFrameOptionsHeader = _config.GetConfigBool("SuppressXFrameOptionsHeader", antiforgeryCfg, false);
            o.SuppressReadingTokenFromFormBody = _config.GetConfigBool("SuppressReadingTokenFromFormBody", antiforgeryCfg, false);

            Logger?.LogDebug("Using Antiforgery with cookie name {Cookie}, form field name {FormFieldName}, header name {HeaderName}",
                o.Cookie.Name,
                o.FormFieldName,
                o.HeaderName);
        });
        return true;
    }
    
    private (string?, ConnectionRetryOptions) BuildConnection(
        string? connectionName, 
        string connectionString, 
        bool isMain,
        bool skipRetryOpts)
    {
        if (_config.EnvDict is not null)
        {
            connectionString = Formatter.FormatString(connectionString.AsSpan(), _config.EnvDict).ToString();
        }
        
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString);
        if (_config.GetConfigBool("SetApplicationNameInConnection", _config.ConnectionSettingsCfg, true) is true)
        {
            connectionStringBuilder.ApplicationName = Instance.Environment.ApplicationName;
        }
        
        // Connection doesn't participate in ambient TransactionScope
        connectionStringBuilder.Enlist = false;
        // Connection doesn't have to have reset on close
        connectionStringBuilder.NoResetOnClose = true;

        connectionString = connectionStringBuilder.ConnectionString;

        var keys = connectionStringBuilder.Keys;
        foreach (var key in keys)
        {
            // if key contains password or key or certificate then remove from connectionStringBuilder
            if (key.Contains("password", StringComparison.OrdinalIgnoreCase) || key.Contains("key", StringComparison.OrdinalIgnoreCase) || key.Contains("certificate", StringComparison.OrdinalIgnoreCase))
            {
                connectionStringBuilder.Remove(key);
                connectionStringBuilder.Add(key, "******");
            }
        }
        if (isMain)
        {
            if (string.IsNullOrEmpty(connectionName) is true)
            {
                Logger?.LogDebug("Using main connection string: {ConnectionString}", connectionStringBuilder.ConnectionString);
            }
            else
            {
                Logger?.LogDebug("Using {connectionName} as main connection string: {ConnectionString}", connectionName, connectionStringBuilder.ConnectionString);
            }
        }
        else
        {
            if (string.Equals(ConnectionName, connectionName, StringComparison.Ordinal))
            {
                return (null, null!);
            }
            if (string.IsNullOrEmpty(connectionName) is true)
            {
                Logger?.LogDebug("Using additional connection string: {0}", connectionStringBuilder.ConnectionString);
            }
            else
            {
                Logger?.LogDebug("Using {connectionName} as additional connection string: {ConnectionString}", connectionName, connectionStringBuilder.ConnectionString);
            }
        }

        var retryOptions = new ConnectionRetryOptions();
        if (skipRetryOpts is false)
        {
            var retryCfg = _config.ConnectionSettingsCfg.GetSection("RetryOptions");
            retryOptions.Enabled = _config.GetConfigBool("Enabled", retryCfg, true);
            if (retryOptions.Enabled is true)
            {
                retryOptions.Strategy.RetrySequenceSeconds = 
                    _config.GetConfigEnumerable("RetrySequenceSeconds", retryCfg)? .Select(s => double.Parse(s)).ToArray()  ?? [1, 3, 6, 12];
                retryOptions.Strategy.ErrorCodes = _config.GetConfigEnumerable("ErrorCodes", retryCfg)?.ToHashSet() 
                                                   ?? [
                                                       "08000", "08003", "08006", "08001", "08004", // Connection failure codes
                                                       "55P03", // Lock not available
                                                       "55006", // Object in use
                                                       "53300", // Too many connections
                                                       "57P03", // Cannot connect now
                                                       "40001", // Serialization failure (can be retried)
                                                   ];
                Logger?.LogDebug("Using connection retry options with strategy: RetrySequenceSeconds={RetrySequenceSeconds}, ErrorCodes={ErrorCodes}",
                    string.Join(",", retryOptions.Strategy.RetrySequenceSeconds),
                    string.Join(",", retryOptions.Strategy.ErrorCodes));
            }
        }
        if (_config.GetConfigBool("TestConnectionStrings", _config.ConnectionSettingsCfg) is true)
        {
            using var conn = new NpgsqlConnection(connectionString);
            try
            {
                conn.OpenRetry(retryOptions, Logger);
            }
            finally
            {
                conn.Close();
            }
        }

        return (connectionString, retryOptions);
    }

    public (string?, ConnectionRetryOptions) BuildConnectionString()
    {
        string? connectionString;
        string? connectionName = _config.GetConfigStr("ConnectionName", _config.NpgsqlRestCfg);
        if (connectionName is not null)
        {
            connectionString = _config.Cfg.GetConnectionString(connectionName);
        }
        else
        {
            var section = _config.Cfg.GetSection("ConnectionStrings");
            var item = section.GetChildren().FirstOrDefault();
            connectionName = item?.Key;
            connectionString = item?.Value;
        }

        if (connectionString is null)
        {
            connectionString = "Host={PGHOST};Port=5432;Database={PGDATABASE};Username={PGUSER};Password={PGPASSWORD}";
        }

        var (result, retryOpts) = BuildConnection(connectionName, connectionString!, isMain: true, skipRetryOpts: false);

        // disable SQL rewriting to ensure that NpgsqlRest works with this option OFF.
        AppContext.SetSwitch("Npgsql.EnableSqlRewriting", false);

        ConnectionString = result;
        ConnectionName = connectionName;
        return (result, retryOpts);
    }

    public IDictionary<string, string> BuildConnectionStringDict()
    {
        var result = new Dictionary<string, string>();
        foreach (var section in _config.Cfg.GetSection("ConnectionStrings").GetChildren())
        {
            if (section?.Key is null)
            {
                continue;
            }

            if (string.Equals(ConnectionName, section.Key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var (conn, _) = BuildConnection(section?.Key, section?.Value!, isMain: false, skipRetryOpts: true);
            if (conn is not null)
            {
                result.Add(section?.Key!, conn!);
            }
        }
        return result.ToFrozenDictionary();
    }

    private string? _instanceId = null;

    public string InstanceId
    {
        get
        {
            _instanceId ??= Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            return _instanceId;
        }
    }

    public Dictionary<string, StringValues> GetCustomHeaders()
    {
        var result = new Dictionary<string, StringValues>();
        foreach(var section in _config.NpgsqlRestCfg.GetSection("CustomRequestHeaders").GetChildren())
        {
            result.Add(section.Key, section.Value);
        }

        var instIdName = _config.GetConfigStr("InstanceIdRequestHeaderName", _config.NpgsqlRestCfg);
        if (string.IsNullOrEmpty(instIdName) is false)
        {
            result.Add(instIdName, InstanceId);
        }
        return result;
    }

    public Dictionary<string, StringValues> GetSseResponseHeaders()
    {
        var result = new Dictionary<string, StringValues>();
        foreach (var section in _config.NpgsqlRestCfg.GetSection("ServerSentEventsResponseHeaders").GetChildren())
        {
            result.Add(section.Key, section.Value);
        }
        return result;
    }

    public CommandRetryOptions BuildCommandRetryOptions()
    {
        var retryCfg = _config.Cfg.GetSection("CommandRetryOptions");
        var options = new CommandRetryOptions
        {
            Enabled = _config.GetConfigBool("Enabled", retryCfg),
            DefaultStrategy = _config.GetConfigStr("DefaultStrategy", retryCfg) ?? "default"
        };
        if (options.Enabled is false)
        {
            return options;
        }
        foreach (var strategyCfg in retryCfg.GetSection("Strategies").GetChildren())
        {
            var strategy = new RetryStrategy
            {
                RetrySequenceSeconds = 
                    _config.GetConfigEnumerable("RetrySequenceSeconds", strategyCfg)? .Select(s => double.Parse(s)).ToArray()  ?? [0, 1, 2, 5, 10],
                ErrorCodes = _config.GetConfigEnumerable("ErrorCodes", strategyCfg)?.ToHashSet() 
                             ?? [
                                 // Serialization failures (MUST retry for correctness)
                                 "40001", // serialization_failure 
                                 "40P01", // deadlock_detected
                                 // Connection issues (Class 08)
                                 "08000", // connection_exception
                                 "08003", // connection_does_not_exist
                                 "08006", // connection_failure  
                                 "08001", // sqlclient_unable_to_establish_sqlconnection
                                 "08004", // sqlserver_rejected_establishment_of_sqlconnection
                                 "08007", // transaction_resolution_unknown
                                 "08P01", // protocol_violation
                                 // Resource constraints (Class 53)
                                 "53000", // insufficient_resources
                                 "53100", // disk_full
                                 "53200", // out_of_memory
                                 "53300", // too_many_connections
                                 "53400", // configuration_limit_exceeded
                                 // System errors (Class 58) 
                                 "57P01", // admin_shutdown
                                 "57P02", // crash_shutdown  
                                 "57P03", // cannot_connect_now
                                 "58000", // system_error
                                 "58030", // io_error
                                 // Lock acquisition issues (Class 55)
                                 "55P03", // lock_not_available
                                 "55006", // object_in_use
                                 "55000", // object_not_in_prerequisite_state
                             ]
            };
            options.Strategies[strategyCfg.Key] = strategy;
        }
        if (Logger is not null && options.Enabled is true)
        {
            if (options.Strategies.TryGetValue(options.DefaultStrategy, out var defStrat) is true)
            {
                Logger?.LogDebug("Using command retry options with default strategy '{DefaultStrategy}': RetrySequenceSeconds={RetrySequenceSeconds}, ErrorCodes={ErrorCodes}",
                    options.DefaultStrategy,
                    string.Join(",", defStrat.RetrySequenceSeconds),
                    string.Join(",", defStrat.ErrorCodes));
            }
            else
            {
                Logger?.LogWarning("Default command retry strategy '{DefaultStrategy}' not found in defined strategies.", options.DefaultStrategy);
            }
        }
        return options;
    }
    
    public enum CacheType { Memory, Redis }
    
    public CacheOptions BuildCacheOptions(WebApplication app)
    {
        var cacheCfg = _config.Cfg.GetSection("CacheOptions");
        var options = new CacheOptions()
        {
            DefaultRoutineCache = null
        };
        if (cacheCfg is null || _config.GetConfigBool("Enabled", cacheCfg) is false)
        {
            Logger?.LogDebug("Routine caching is disabled.");
            return options;
        }
        
        var type = _config.GetConfigEnum<CacheType?>("Type", cacheCfg) ?? CacheType.Memory;
        options.MaxCacheableRows = _config.GetConfigInt("MaxCacheableRows", cacheCfg);
        if (type == CacheType.Memory)
        {
            options.MemoryCachePruneIntervalSeconds =
                _config.GetConfigInt("MemoryCachePruneIntervalSeconds", cacheCfg) ?? 60;
            options.DefaultRoutineCache = new RoutineCache();
            Logger?.LogDebug("Using in-memory routine cache with prune interval of {MemoryCachePruneIntervalSeconds} seconds. MaxCacheableRows={MaxCacheableRows}", 
                options.MemoryCachePruneIntervalSeconds, options.MaxCacheableRows);
        }
        else if (type == CacheType.Redis)
        {
            var configuration = _config.GetConfigStr("RedisConfiguration", cacheCfg) ??
                                "localhost:6379,abortConnect=false,ssl=false,connectTimeout=10000,syncTimeout=5000,connectRetry=3";
            
            try
            {
                var redisCache = new RedisCache(configuration, Logger);
                options.DefaultRoutineCache = redisCache;
                Logger?.LogDebug("Using Redis routine cache with configuration: {RedisConfiguration}. MaxCacheableRows={MaxCacheableRows}", 
                    configuration, options.MaxCacheableRows);
                app.Lifetime.ApplicationStopping.Register(() => redisCache.Dispose());
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to initialize Redis cache with configuration: {RedisConfiguration}", configuration);
                Logger?.LogWarning("Falling back to in-memory cache due to Redis initialization failure");
                options.DefaultRoutineCache = new RoutineCache();
                options.MemoryCachePruneIntervalSeconds = _config.GetConfigInt("MemoryCachePruneIntervalSeconds", cacheCfg) ?? 60;
            }
        }
        
        return options;
    }
    
    public enum RateLimiterType { FixedWindow, SlidingWindow, TokenBucket, Concurrency }

    public (string? defaultPolicy, bool enabled) BuildRateLimiter()
    {
        var rateLimiterCfg = _config.Cfg.GetSection("RateLimiterOptions");
        if (_config.Exists(rateLimiterCfg) is false || _config.GetConfigBool("Enabled", rateLimiterCfg) is false)
        {
            return (null, false);
        }
        var policiesCfg = rateLimiterCfg.GetSection("Policies");
        if (_config.Exists(policiesCfg) is false)
        {
            return (null, false);
        }

        var defaultPolicy = _config.GetConfigStr("DefaultPolicy", rateLimiterCfg);
        var message = _config.GetConfigStr("Message", rateLimiterCfg);
        Instance.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = _config.GetConfigInt("StatusCode", rateLimiterCfg) ?? 429;
            if (string.IsNullOrEmpty(message) is false)
            {
                options.OnRejected = async (context, cancellationToken) =>
                {
                    await context.HttpContext.Response.WriteAsync(message, cancellationToken);
                };
            }
            foreach (var sectionCfg in policiesCfg.GetChildren())
            {
                var type = _config.GetConfigEnum<RateLimiterType?>("Type", sectionCfg);
                if (type is null)
                {
                    continue;
                }
                if (_config.GetConfigBool("Enabled", sectionCfg) is false)
                {
                    continue;
                }
                var name = _config.GetConfigStr("Name", sectionCfg) ?? type.ToString()!;
                
                if (type == RateLimiterType.FixedWindow)
                {
                    options.AddFixedWindowLimiter(name, config =>
                    {
                        config.PermitLimit = _config.GetConfigInt("PermitLimit", sectionCfg) ?? 100;
                        config.Window = TimeSpan.FromSeconds(_config.GetConfigInt("WindowSeconds", sectionCfg) ?? 60);
                        config.QueueLimit = _config.GetConfigInt("QueueLimit", sectionCfg) ?? 10;
                        config.AutoReplenishment = _config.GetConfigBool("AutoReplenishment", sectionCfg, true);
                    });
                    Logger?.LogDebug("Using Fixed Window rate limiter with name {Name}: PermitLimit={PermitLimit}, WindowSeconds={WindowSeconds}, QueueLimit={QueueLimit}, AutoReplenishment={AutoReplenishment}",
                        name,
                        _config.GetConfigInt("PermitLimit", sectionCfg) ?? 100,
                        _config.GetConfigInt("WindowSeconds", sectionCfg) ?? 60,
                        _config.GetConfigInt("QueueLimit", sectionCfg) ?? 10,
                        _config.GetConfigBool("AutoReplenishment", sectionCfg, true));
                }
                else if (type == RateLimiterType.SlidingWindow)
                {
                    options.AddSlidingWindowLimiter(name, config =>
                    {
                        config.PermitLimit = _config.GetConfigInt("PermitLimit", sectionCfg) ?? 100;
                        config.Window = TimeSpan.FromSeconds(_config.GetConfigInt("WindowSeconds", sectionCfg) ?? 60);
                        config.SegmentsPerWindow = _config.GetConfigInt("SegmentsPerWindow", sectionCfg) ?? 6;
                        config.QueueLimit = _config.GetConfigInt("QueueLimit", sectionCfg) ?? 10;
                        config.AutoReplenishment = _config.GetConfigBool("AutoReplenishment", sectionCfg, true);
                    });
                    Logger?.LogDebug("Using Sliding Window rate limiter with name {Name}: PermitLimit={PermitLimit}, WindowSeconds={WindowSeconds}, SegmentsPerWindow={SegmentsPerWindow}, QueueLimit={QueueLimit}, AutoReplenishment={AutoReplenishment}",
                        name,
                        _config.GetConfigInt("PermitLimit", sectionCfg) ?? 100,
                        _config.GetConfigInt("WindowSeconds", sectionCfg) ?? 60,
                        _config.GetConfigInt("SegmentsPerWindow", sectionCfg) ?? 6,
                        _config.GetConfigInt("QueueLimit", sectionCfg) ?? 10,
                        _config.GetConfigBool("AutoReplenishment", sectionCfg, true));
                }
                else if (type == RateLimiterType.TokenBucket)
                {
                    options.AddTokenBucketLimiter(name, config =>
                    {
                        config.TokenLimit = _config.GetConfigInt("TokenLimit", sectionCfg) ?? 100;
                        config.TokensPerPeriod = _config.GetConfigInt("TokensPerPeriod", sectionCfg) ?? 10;
                        config.ReplenishmentPeriod = TimeSpan.FromSeconds(_config.GetConfigInt("ReplenishmentPeriodSeconds", sectionCfg) ?? 10);
                        config.QueueLimit = _config.GetConfigInt("QueueLimit", sectionCfg) ?? 10;
                        config.AutoReplenishment = _config.GetConfigBool("AutoReplenishment", sectionCfg, true);
                    });
                    Logger?.LogDebug("Using Token Bucket rate limiter with name {Name}: TokenLimit={TokenLimit}, TokensPerPeriod={TokensPerPeriod}, ReplenishmentPeriodSeconds={ReplenishmentPeriodSeconds}, QueueLimit={QueueLimit}, AutoReplenishment={AutoReplenishment}",
                        name,
                        _config.GetConfigInt("TokenLimit", sectionCfg) ?? 100,
                        _config.GetConfigInt("TokensPerPeriod", sectionCfg) ?? 10,
                        _config.GetConfigInt("ReplenishmentPeriodSeconds", sectionCfg) ?? 1,
                        _config.GetConfigInt("QueueLimit", sectionCfg) ?? 10,
                        _config.GetConfigBool("AutoReplenishment", sectionCfg, true));
                }
                else if (type == RateLimiterType.Concurrency)
                {
                    options.AddConcurrencyLimiter(name, config =>
                    {
                        config.PermitLimit = _config.GetConfigInt("PermitLimit", sectionCfg) ?? 100;
                        config.QueueLimit = _config.GetConfigInt("QueueLimit", sectionCfg) ?? 10;
                        config.QueueProcessingOrder = _config.GetConfigBool("OldestFirst", sectionCfg, true) is true ? QueueProcessingOrder.OldestFirst : QueueProcessingOrder.NewestFirst;
                    });
                    Logger?.LogDebug("Using Concurrency rate limiter with name {Name}: PermitLimit={PermitLimit}, QueueLimit={QueueLimit}, OldestFirst={OldestFirst}",
                        name,
                        _config.GetConfigInt("PermitLimit", sectionCfg) ?? 100,
                        _config.GetConfigInt("QueueLimit", sectionCfg) ?? 10,
                        _config.GetConfigBool("OldestFirst", sectionCfg, true));
                }
            }
        });

        return (defaultPolicy, true);
    }

    public HttpClientOptions BuildHttpClientOptions()
    {
        var cfg = _config.NpgsqlRestCfg.GetSection("HttpClientOptions");
        var options = new HttpClientOptions
        {
            Enabled = _config.GetConfigBool("Enabled", cfg),
            ResponseStatusCodeField = _config.GetConfigStr("ResponseStatusCodeField", cfg) ?? "status_code",
            ResponseBodyField = _config.GetConfigStr("ResponseBodyField", cfg) ?? "body",
            ResponseHeadersField = _config.GetConfigStr("ResponseHeadersField", cfg) ?? "headers",
            ResponseContentTypeField = _config.GetConfigStr("ResponseContentTypeField", cfg) ?? "content_type",
            ResponseSuccessField = _config.GetConfigStr("ResponseSuccessField", cfg) ?? "success",
            ResponseErrorMessageField = _config.GetConfigStr("ResponseErrorMessageField", cfg) ?? "error_message"
        };

        if (options.Enabled)
        {
            Logger?.LogDebug("HTTP client options enabled: ResponseBodyField={ResponseBodyField}, ResponseStatusCodeField={ResponseStatusCodeField}, ResponseHeadersField={ResponseHeadersField}, ResponseContentTypeField={ResponseContentTypeField}, ResponseSuccessField={ResponseSuccessField}, ResponseErrorMessageField={ResponseErrorMessageField}",
                options.ResponseBodyField,
                options.ResponseStatusCodeField,
                options.ResponseHeadersField,
                options.ResponseContentTypeField,
                options.ResponseSuccessField,
                options.ResponseErrorMessageField);
        }

        return options;
    }
}