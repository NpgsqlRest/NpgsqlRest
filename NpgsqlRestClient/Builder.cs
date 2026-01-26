using System.Collections.Frozen;
using System.IO.Compression;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Npgsql;
using NpgsqlRest;
using NpgsqlRestClient.Fido2;
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
    
    public bool LoggingEnabled { get; private set; } = false;
    
    public Microsoft.Extensions.Logging.ILogger? Logger { get; private set; } = null;
    public bool UseHttpsRedirection { get; private set; } = false;
    public bool UseHsts { get; private set; } = false;
    public BearerTokenConfig? BearerTokenConfig { get; private set; } = null;
    public JwtTokenConfig? JwtTokenConfig { get; private set; } = null;
    public string? ConnectionString { get; private set; } = null;
    public string? ConnectionName { get; private set; } = null;
    public ExternalAuthConfig? ExternalAuthConfig { get; private set; } = null;
    public PasskeyConfig? PasskeyConfig { get; private set; } = null;
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
            // Apply default behavior: remove traceId (matches default when config exists)
            Instance.Services.AddProblemDetails(options =>
            {
                options.CustomizeProblemDetails = ctx =>
                {
                    ctx.ProblemDetails.Extensions.Remove("traceId");
                };
            });
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

    public enum KeyEncryptionMethod
    {
        None,
        Certificate,
        Dpapi // Windows only
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
            var getAllElementsCommand = _config.GetConfigStr("GetAllElementsCommand", dataProtectionCfg) ?? "select get_data_protection_keys()";
            var storeElementCommand = _config.GetConfigStr("StoreElementCommand", dataProtectionCfg) ?? "call store_data_protection_keys($1,$2)";
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

        var keyEncryption = _config.GetConfigEnum<KeyEncryptionMethod?>("KeyEncryption", dataProtectionCfg) ?? KeyEncryptionMethod.None;
        if (keyEncryption == KeyEncryptionMethod.Certificate)
        {
            var certPath = _config.GetConfigStr("CertificatePath", dataProtectionCfg);
            if (string.IsNullOrEmpty(certPath))
            {
                throw new ArgumentException("CertificatePath value in DataProtection can't be null or empty when using Certificate KeyEncryption");
            }
            var certPassword = _config.GetConfigStr("CertificatePassword", dataProtectionCfg);
            if (string.IsNullOrEmpty(certPassword))
            {
                dataProtectionBuilder.ProtectKeysWithCertificate(System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(certPath, null));
            }
            else
            {
                dataProtectionBuilder.ProtectKeysWithCertificate(System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(certPath, certPassword));
            }
            Logger?.LogDebug("Data Protection keys encrypted with certificate from {certPath}", certPath);
        }
        else if (keyEncryption == KeyEncryptionMethod.Dpapi)
        {
            if (OperatingSystem.IsWindows() is false)
            {
                throw new PlatformNotSupportedException("DPAPI key encryption is only supported on Windows");
            }
            var protectToLocalMachine = _config.GetConfigBool("DpapiLocalMachine", dataProtectionCfg);
#pragma warning disable CA1416 // Validate platform compatibility - already checked above
            dataProtectionBuilder.ProtectKeysWithDpapi(protectToLocalMachine);
#pragma warning restore CA1416
            Logger?.LogDebug("Data Protection keys encrypted with DPAPI (LocalMachine: {protectToLocalMachine})", protectToLocalMachine);
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
        bool jwtAuth = false;

        if (_config.Exists(authCfg) is true)
        {
            cookieAuth = _config.GetConfigBool("CookieAuth", authCfg);
            bearerTokenAuth = _config.GetConfigBool("BearerTokenAuth", authCfg);
            jwtAuth = _config.GetConfigBool("JwtAuth", authCfg);
        }

        if (cookieAuth is false && bearerTokenAuth is false && jwtAuth is false)
        {
            return;
        }

        var cookieScheme = _config.GetConfigStr("CookieAuthScheme", authCfg) ?? CookieAuthenticationDefaults.AuthenticationScheme;
        var tokenScheme = _config.GetConfigStr("BearerTokenAuthScheme", authCfg) ?? BearerTokenDefaults.AuthenticationScheme;
        var jwtScheme = _config.GetConfigStr("JwtAuthScheme", authCfg) ?? JwtBearerDefaults.AuthenticationScheme;

        // Build default scheme name based on enabled auth methods
        var enabledSchemes = new List<string>();
        if (cookieAuth) enabledSchemes.Add(cookieScheme);
        if (bearerTokenAuth) enabledSchemes.Add(tokenScheme);
        if (jwtAuth) enabledSchemes.Add(jwtScheme);

        string defaultScheme = enabledSchemes.Count switch
        {
            1 => enabledSchemes[0],
            _ => string.Join("_and_", enabledSchemes)
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

        if (jwtAuth is true)
        {
            var jwtSecret = _config.GetConfigStr("JwtSecret", authCfg);
            if (string.IsNullOrEmpty(jwtSecret))
            {
                throw new InvalidOperationException("JwtSecret must be configured when JwtAuth is enabled. The secret must be at least 32 characters for HS256.");
            }
            if (jwtSecret.Length < 32)
            {
                throw new InvalidOperationException("JwtSecret must be at least 32 characters long for HS256 algorithm.");
            }

            var expireMinutes = _config.GetConfigInt("JwtExpireMinutes", authCfg) ?? 60;
            var refreshExpireDays = _config.GetConfigInt("JwtRefreshExpireDays", authCfg) ?? 7;
            var clockSkew = Parser.ParsePostgresInterval(_config.GetConfigStr("JwtClockSkew", authCfg)) ?? TimeSpan.FromMinutes(5);

            JwtTokenConfig = new()
            {
                Scheme = jwtScheme,
                Secret = jwtSecret,
                Issuer = _config.GetConfigStr("JwtIssuer", authCfg),
                Audience = _config.GetConfigStr("JwtAudience", authCfg),
                ExpireMinutes = expireMinutes,
                RefreshExpireDays = refreshExpireDays,
                ValidateIssuer = _config.GetConfigBool("JwtValidateIssuer", authCfg),
                ValidateAudience = _config.GetConfigBool("JwtValidateAudience", authCfg),
                ValidateLifetime = _config.GetConfigBool("JwtValidateLifetime", authCfg, true),
                ValidateIssuerSigningKey = _config.GetConfigBool("JwtValidateIssuerSigningKey", authCfg, true),
                ClockSkew = clockSkew,
                RefreshPath = _config.GetConfigStr("JwtRefreshPath", authCfg)
            };

            auth.AddJwtBearer(jwtScheme, options =>
            {
                options.TokenValidationParameters = JwtTokenConfig.GetTokenValidationParameters();
                options.SaveToken = true;
            });

            Logger?.LogDebug(
                "Using JWT Authentication with scheme {jwtScheme}. Access token expires in {expireMinutes} minutes. Refresh token expires in {refreshExpireDays} days. Refresh path is {RefreshPath}",
                jwtScheme,
                expireMinutes,
                refreshExpireDays,
                JwtTokenConfig.RefreshPath);
        }

        // Create policy scheme if multiple auth methods are enabled
        if (enabledSchemes.Count > 1)
        {
            auth.AddPolicyScheme(defaultScheme, defaultScheme, options =>
            {
                // runs on each request
                options.ForwardDefaultSelector = context =>
                {
                    string? authorization = context.Request.Headers[HeaderNames.Authorization];

                    if (string.IsNullOrEmpty(authorization) is false && authorization.StartsWith("Bearer "))
                    {
                        // For Bearer tokens, we need to determine if it's JWT or Microsoft Bearer Token
                        // JWT tokens are in format: xxxxx.yyyyy.zzzzz (three Base64 parts separated by dots)
                        var token = authorization["Bearer ".Length..].Trim();

                        if (jwtAuth && IsJwtToken(token))
                        {
                            return jwtScheme;
                        }

                        if (bearerTokenAuth)
                        {
                            return tokenScheme;
                        }
                    }

                    // Default to cookie auth if enabled
                    if (cookieAuth)
                    {
                        return cookieScheme;
                    }

                    // Fallback to first enabled scheme
                    return enabledSchemes[0];
                };
            });
        }

        if (cookieAuth || bearerTokenAuth || jwtAuth)
        {
            ExternalAuthConfig = new ExternalAuthConfig();
            ExternalAuthConfig.Build(authCfg, _config, this);
        }
    }

    private static bool IsJwtToken(string token)
    {
        // JWT tokens have exactly 3 parts separated by dots
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        // Each part should be valid Base64Url
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Builds the PasskeyConfig from configuration settings.
    /// </summary>
    public void BuildPasskeyAuthentication()
    {
        var authCfg = _config.Cfg.GetSection("Auth");
        var passkeyCfg = authCfg?.GetSection("PasskeyAuth");

        if (passkeyCfg is null || _config.GetConfigBool("Enabled", passkeyCfg) is not true)
        {
            return;
        }

        PasskeyConfig = new PasskeyConfig
        {
            Enabled = true,
            EnableRegister = _config.GetConfigBool("EnableRegister", passkeyCfg, false),
            RelyingPartyId = _config.GetConfigStr("RelyingPartyId", passkeyCfg),
            RelyingPartyName = _config.GetConfigStr("RelyingPartyName", passkeyCfg) ?? Instance.Environment.ApplicationName,
            RelyingPartyOrigins = _config.GetConfigEnumerable("RelyingPartyOrigins", passkeyCfg)?.ToArray() ?? [],
            AddPasskeyOptionsPath = _config.GetConfigStr("AddPasskeyOptionsPath", passkeyCfg) ?? "/api/passkey/add/options",
            AddPasskeyPath = _config.GetConfigStr("AddPasskeyPath", passkeyCfg) ?? "/api/passkey/add",
            RegistrationOptionsPath = _config.GetConfigStr("RegistrationOptionsPath", passkeyCfg) ?? "/api/passkey/register/options",
            RegistrationPath = _config.GetConfigStr("RegistrationPath", passkeyCfg) ?? "/api/passkey/register",
            LoginOptionsPath = _config.GetConfigStr("LoginOptionsPath", passkeyCfg) ?? "/api/passkey/login/options",
            LoginPath = _config.GetConfigStr("LoginPath", passkeyCfg) ?? "/api/passkey/login",
            ChallengeTimeoutMinutes = _config.GetConfigInt("ChallengeTimeoutMinutes", passkeyCfg) ?? 5,
            UserVerificationRequirement = _config.GetConfigStr("UserVerificationRequirement", passkeyCfg) ?? "preferred",
            ResidentKeyRequirement = _config.GetConfigStr("ResidentKeyRequirement", passkeyCfg) ?? "preferred",
            AttestationConveyance = _config.GetConfigStr("AttestationConveyance", passkeyCfg) ?? "none",
            // GROUP 1: Challenge Commands
            ChallengeAddExistingUserCommand = _config.GetConfigStr("ChallengeAddExistingUserCommand", passkeyCfg) ?? "select * from passkey_challenge_add_existing($1,$2)",
            ChallengeRegistrationCommand = _config.GetConfigStr("ChallengeRegistrationCommand", passkeyCfg) ?? "select * from passkey_challenge_registration($1)",
            ChallengeAuthenticationCommand = _config.GetConfigStr("ChallengeAuthenticationCommand", passkeyCfg) ?? "select * from passkey_challenge_authentication($1,$2)",
            // GROUP 2: Challenge Verify Command
            ChallengeVerifyCommand = _config.GetConfigStr("ChallengeVerifyCommand", passkeyCfg) ?? "select * from passkey_verify_challenge($1,$2)",
            ValidateSignCount = _config.GetConfigBool("ValidateSignCount", passkeyCfg, true),
            // GROUP 3: Authentication Data Command
            AuthenticateDataCommand = _config.GetConfigStr("AuthenticateDataCommand", passkeyCfg) ?? "select * from passkey_authenticate_data($1)",
            // GROUP 4: Complete Commands
            AddExistingUserCompleteCommand = _config.GetConfigStr("AddExistingUserCompleteCommand", passkeyCfg) ?? "select * from passkey_add_existing_complete($1,$2,$3,$4,$5,$6,$7,$8)",
            RegistrationCompleteCommand = _config.GetConfigStr("RegistrationCompleteCommand", passkeyCfg) ?? "select * from passkey_registration_complete($1,$2,$3,$4,$5,$6,$7,$8)",
            AuthenticateCompleteCommand = _config.GetConfigStr("AuthenticateCompleteCommand", passkeyCfg) ?? "select * from passkey_authenticate_complete($1,$2,$3,$4)",
            ClientAnalyticsIpKey = _config.GetConfigStr("ClientAnalyticsIpKey", passkeyCfg) ?? "ip",
            StatusColumnName = _config.GetConfigStr("StatusColumnName", passkeyCfg) ?? "status",
            MessageColumnName = _config.GetConfigStr("MessageColumnName", passkeyCfg) ?? "message",
            ChallengeColumnName = _config.GetConfigStr("ChallengeColumnName", passkeyCfg) ?? "challenge",
            ChallengeIdColumnName = _config.GetConfigStr("ChallengeIdColumnName", passkeyCfg) ?? "challenge_id",
            UserNameColumnName = _config.GetConfigStr("UserNameColumnName", passkeyCfg) ?? "user_name",
            UserDisplayNameColumnName = _config.GetConfigStr("UserDisplayNameColumnName", passkeyCfg) ?? "user_display_name",
            UserHandleColumnName = _config.GetConfigStr("UserHandleColumnName", passkeyCfg) ?? "user_handle",
            ExcludeCredentialsColumnName = _config.GetConfigStr("ExcludeCredentialsColumnName", passkeyCfg) ?? "exclude_credentials",
            AllowCredentialsColumnName = _config.GetConfigStr("AllowCredentialsColumnName", passkeyCfg) ?? "allow_credentials",
            PublicKeyColumnName = _config.GetConfigStr("PublicKeyColumnName", passkeyCfg) ?? "public_key",
            PublicKeyAlgorithmColumnName = _config.GetConfigStr("PublicKeyAlgorithmColumnName", passkeyCfg) ?? "public_key_algorithm",
            SignCountColumnName = _config.GetConfigStr("SignCountColumnName", passkeyCfg) ?? "sign_count"
        };

        Logger?.LogDebug(
            "Using Passkey Authentication: RP ID={RelyingPartyId}, RP Name={RelyingPartyName}, Origins={Origins}, " +
            "UV={UserVerification}, RK={ResidentKey}",
            PasskeyConfig.RelyingPartyId ?? "(auto)",
            PasskeyConfig.RelyingPartyName ?? "(app name)",
            PasskeyConfig.RelyingPartyOrigins.Length > 0 ? string.Join(", ", PasskeyConfig.RelyingPartyOrigins) : "(any)",
            PasskeyConfig.UserVerificationRequirement,
            PasskeyConfig.ResidentKeyRequirement);
    }

    public bool BuildCors()
    {
        var corsCfg = _config.Cfg.GetSection("Cors");
        if (_config.Exists(corsCfg) is false || _config.GetConfigBool("Enabled", corsCfg) is false)
        {
            return false;
        }

        string[] allowedOrigins = _config.GetConfigEnumerable("AllowedOrigins", corsCfg)?.ToArray() ?? [];
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
            throw new InvalidOperationException(
                "No connection string configured. Please provide a connection string in the 'ConnectionStrings' section of your configuration file, " +
                "or set the PGHOST, PGDATABASE, PGUSER, and PGPASSWORD environment variables with 'Config.AddEnvironmentVariables: true'.");
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
            Enabled = _config.GetConfigBool("Enabled", retryCfg, true),
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
    
    public enum CacheType { Memory, Redis, Hybrid }

    public CacheType ConfigureCacheServices()
    {
        var cacheCfg = _config.Cfg.GetSection("CacheOptions");
        if (cacheCfg is null || _config.GetConfigBool("Enabled", cacheCfg) is false)
        {
            return CacheType.Memory;
        }

        var type = _config.GetConfigEnum<CacheType?>("Type", cacheCfg) ?? CacheType.Memory;

        if (type == CacheType.Hybrid)
        {
            var redisConfiguration = _config.GetConfigStr("RedisConfiguration", cacheCfg);
            var useRedisBackend = _config.GetConfigBool("HybridCacheUseRedisBackend", cacheCfg, false);

            // Only register Redis as L2 cache if explicitly enabled
            if (useRedisBackend && !string.IsNullOrEmpty(redisConfiguration))
            {
                Instance.Services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = redisConfiguration;
                });
                Logger?.LogDebug("HybridCache services configured with Redis L2 at: {RedisConfiguration}", redisConfiguration);
            }
            else
            {
                Logger?.LogDebug("HybridCache services configured with in-memory only (no Redis L2)");
            }

            // Register HybridCache with configuration
            Instance.Services.AddHybridCache(options =>
            {
                options.MaximumKeyLength = _config.GetConfigInt("HybridCacheMaximumKeyLength", cacheCfg) ?? 1024;
                options.MaximumPayloadBytes = _config.GetConfigInt("HybridCacheMaximumPayloadBytes", cacheCfg) ?? 1024 * 1024;

                var defaultExpiration = Parser.ParsePostgresInterval(_config.GetConfigStr("HybridCacheDefaultExpiration", cacheCfg));
                var localExpiration = Parser.ParsePostgresInterval(_config.GetConfigStr("HybridCacheLocalCacheExpiration", cacheCfg));

                options.DefaultEntryOptions = new HybridCacheEntryOptions
                {
                    Expiration = defaultExpiration,
                    LocalCacheExpiration = localExpiration ?? defaultExpiration
                };
            });
        }

        return type;
    }

    public CacheOptions BuildCacheOptions(WebApplication app, CacheType configuredCacheType)
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

        options.MaxCacheableRows = _config.GetConfigInt("MaxCacheableRows", cacheCfg);
        options.UseHashedCacheKeys = _config.GetConfigBool("UseHashedCacheKeys", cacheCfg);
        options.HashKeyThreshold = _config.GetConfigInt("HashKeyThreshold", cacheCfg) ?? 256;
        options.InvalidateCacheSuffix = _config.GetConfigStr("InvalidateCacheSuffix", cacheCfg);

        if (configuredCacheType == CacheType.Memory)
        {
            options.MemoryCachePruneIntervalSeconds =
                _config.GetConfigInt("MemoryCachePruneIntervalSeconds", cacheCfg) ?? 60;
            options.DefaultRoutineCache = new RoutineCache();
            Logger?.LogDebug("Using in-memory routine cache with prune interval of {MemoryCachePruneIntervalSeconds} seconds. MaxCacheableRows={MaxCacheableRows}, UseHashedCacheKeys={UseHashedCacheKeys}, HashKeyThreshold={HashKeyThreshold}",
                options.MemoryCachePruneIntervalSeconds, options.MaxCacheableRows, options.UseHashedCacheKeys, options.HashKeyThreshold);
        }
        else if (configuredCacheType == CacheType.Redis)
        {
            var configuration = _config.GetConfigStr("RedisConfiguration", cacheCfg) ??
                                "localhost:6379,abortConnect=false,ssl=false,connectTimeout=10000,syncTimeout=5000,connectRetry=3";

            try
            {
                var redisCache = new RedisCache(configuration, Logger, options);
                options.DefaultRoutineCache = redisCache;
                Logger?.LogDebug("Using Redis routine cache with configuration: {RedisConfiguration}. MaxCacheableRows={MaxCacheableRows}, UseHashedCacheKeys={UseHashedCacheKeys}, HashKeyThreshold={HashKeyThreshold}",
                    configuration, options.MaxCacheableRows, options.UseHashedCacheKeys, options.HashKeyThreshold);
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
        else if (configuredCacheType == CacheType.Hybrid)
        {
            var useRedisBackend = _config.GetConfigBool("HybridCacheUseRedisBackend", cacheCfg, false);
            var redisConfiguration = _config.GetConfigStr("RedisConfiguration", cacheCfg);

            try
            {
                var hybridCache = app.Services.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>();
                var hybridCacheWrapper = new HybridCacheWrapper(hybridCache, Logger, options);
                options.DefaultRoutineCache = hybridCacheWrapper;

                if (useRedisBackend && !string.IsNullOrEmpty(redisConfiguration))
                {
                    Logger?.LogDebug("Using HybridCache (L1: in-memory, L2: Redis at {RedisConfiguration}). MaxCacheableRows={MaxCacheableRows}, UseHashedCacheKeys={UseHashedCacheKeys}, HashKeyThreshold={HashKeyThreshold}",
                        redisConfiguration, options.MaxCacheableRows, options.UseHashedCacheKeys, options.HashKeyThreshold);
                }
                else
                {
                    Logger?.LogDebug("Using HybridCache (in-memory only, with stampede protection). MaxCacheableRows={MaxCacheableRows}, UseHashedCacheKeys={UseHashedCacheKeys}, HashKeyThreshold={HashKeyThreshold}",
                        options.MaxCacheableRows, options.UseHashedCacheKeys, options.HashKeyThreshold);
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to initialize HybridCache");
                Logger?.LogWarning("Falling back to in-memory cache due to HybridCache initialization failure");
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
        var message = _config.GetConfigStr("StatusMessage", rateLimiterCfg);
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
                        _config.GetConfigInt("ReplenishmentPeriodSeconds", sectionCfg) ?? 10,
                        _config.GetConfigInt("QueueLimit", sectionCfg) ?? 10,
                        _config.GetConfigBool("AutoReplenishment", sectionCfg, true));
                }
                else if (type == RateLimiterType.Concurrency)
                {
                    options.AddConcurrencyLimiter(name, config =>
                    {
                        config.PermitLimit = _config.GetConfigInt("PermitLimit", sectionCfg) ?? 10;
                        config.QueueLimit = _config.GetConfigInt("QueueLimit", sectionCfg) ?? 5;
                        config.QueueProcessingOrder = _config.GetConfigBool("OldestFirst", sectionCfg, true) is true ? QueueProcessingOrder.OldestFirst : QueueProcessingOrder.NewestFirst;
                    });
                    Logger?.LogDebug("Using Concurrency rate limiter with name {Name}: PermitLimit={PermitLimit}, QueueLimit={QueueLimit}, OldestFirst={OldestFirst}",
                        name,
                        _config.GetConfigInt("PermitLimit", sectionCfg) ?? 10,
                        _config.GetConfigInt("QueueLimit", sectionCfg) ?? 5,
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

    public ProxyOptions BuildProxyOptions()
    {
        var cfg = _config.NpgsqlRestCfg.GetSection("ProxyOptions");
        var options = new ProxyOptions
        {
            Enabled = _config.GetConfigBool("Enabled", cfg),
            Host = _config.GetConfigStr("Host", cfg),
            DefaultTimeout = Parser.ParsePostgresInterval(_config.GetConfigStr("DefaultTimeout", cfg)) ?? TimeSpan.FromSeconds(30),
            ForwardHeaders = _config.GetConfigBool("ForwardHeaders", cfg, true),
            ForwardResponseHeaders = _config.GetConfigBool("ForwardResponseHeaders", cfg, true),
            ResponseStatusCodeParameter = _config.GetConfigStr("ResponseStatusCodeParameter", cfg) ?? "_proxy_status_code",
            ResponseBodyParameter = _config.GetConfigStr("ResponseBodyParameter", cfg) ?? "_proxy_body",
            ResponseHeadersParameter = _config.GetConfigStr("ResponseHeadersParameter", cfg) ?? "_proxy_headers",
            ResponseContentTypeParameter = _config.GetConfigStr("ResponseContentTypeParameter", cfg) ?? "_proxy_content_type",
            ResponseSuccessParameter = _config.GetConfigStr("ResponseSuccessParameter", cfg) ?? "_proxy_success",
            ResponseErrorMessageParameter = _config.GetConfigStr("ResponseErrorMessageParameter", cfg) ?? "_proxy_error_message",
            ForwardUploadContent = _config.GetConfigBool("ForwardUploadContent", cfg)
        };

        // Parse ExcludeHeaders from array config
        var excludeHeadersSection = cfg?.GetSection("ExcludeHeaders");
        if (excludeHeadersSection is not null)
        {
            var headers = excludeHeadersSection.GetChildren().Select(x => x.Value).Where(x => x is not null).Cast<string>().ToArray();
            if (headers.Length > 0)
            {
                options.ExcludeHeaders = new HashSet<string>(headers, StringComparer.OrdinalIgnoreCase);
            }
        }

        // Parse ExcludeResponseHeaders from array config
        var excludeResponseHeadersSection = cfg?.GetSection("ExcludeResponseHeaders");
        if (excludeResponseHeadersSection is not null)
        {
            var headers = excludeResponseHeadersSection.GetChildren().Select(x => x.Value).Where(x => x is not null).Cast<string>().ToArray();
            if (headers.Length > 0)
            {
                options.ExcludeResponseHeaders = new HashSet<string>(headers, StringComparer.OrdinalIgnoreCase);
            }
        }

        if (options.Enabled)
        {
            Logger?.LogDebug("Proxy options enabled: Host={Host}, DefaultTimeout={DefaultTimeout}, ForwardHeaders={ForwardHeaders}, ForwardResponseHeaders={ForwardResponseHeaders}",
                options.Host,
                options.DefaultTimeout,
                options.ForwardHeaders,
                options.ForwardResponseHeaders);
        }

        return options;
    }

    /// <summary>
    /// Checks if a connection string is a multi-host connection string (has comma-separated hosts).
    /// </summary>
    public static bool IsMultiHostConnectionString(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return builder.Host?.Contains(',') ?? false;
    }

    /// <summary>
    /// Gets the target session attribute for a connection name from configuration.
    /// </summary>
    public TargetSessionAttributes GetTargetSessionAttribute(string? connectionName)
    {
        var multiHostCfg = _config.ConnectionSettingsCfg.GetSection("MultiHostConnectionTargets");
        if (_config.Exists(multiHostCfg) is false)
        {
            return TargetSessionAttributes.Any;
        }

        // Check per-connection override first
        if (connectionName is not null)
        {
            var byName = multiHostCfg.GetSection("ByConnectionName");
            var overrideValue = _config.GetConfigStr(connectionName, byName);
            if (overrideValue is not null && Enum.TryParse<TargetSessionAttributes>(overrideValue, true, out var result))
            {
                Logger?.LogDebug("Using target session attribute override '{TargetSession}' for connection '{ConnectionName}'", result, connectionName);
                return result;
            }
        }

        // Fall back to default
        var defaultValue = _config.GetConfigStr("Default", multiHostCfg) ?? "Any";
        if (Enum.TryParse<TargetSessionAttributes>(defaultValue, true, out var defaultResult))
        {
            return defaultResult;
        }
        return TargetSessionAttributes.Any;
    }

    /// <summary>
    /// Builds data sources dictionary for multi-host connections.
    /// </summary>
    public Dictionary<string, NpgsqlDataSource> BuildDataSources(string mainConnectionString)
    {
        var result = new Dictionary<string, NpgsqlDataSource>();

        // Build main data source if it's multi-host
        if (IsMultiHostConnectionString(mainConnectionString))
        {
            var target = GetTargetSessionAttribute(ConnectionName);
            var multiHost = new NpgsqlDataSourceBuilder(mainConnectionString).BuildMultiHost();
            result["_default"] = multiHost.WithTargetSession(target);
            Logger?.LogDebug("Built multi-host data source for main connection with target session '{TargetSession}'", target);
        }

        // Build additional connection data sources
        if (_config.GetConfigBool("UseMultipleConnections", _config.NpgsqlRestCfg, false))
        {
            foreach (var section in _config.Cfg.GetSection("ConnectionStrings").GetChildren())
            {
                if (section?.Key is null || string.Equals(ConnectionName, section.Key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var (connStr, _) = BuildConnection(section.Key, section.Value!, isMain: false, skipRetryOpts: true);
                if (connStr is null || !IsMultiHostConnectionString(connStr))
                {
                    continue;
                }

                var target = GetTargetSessionAttribute(section.Key);
                var multiHost = new NpgsqlDataSourceBuilder(connStr).BuildMultiHost();
                result[section.Key] = multiHost.WithTargetSession(target);
                Logger?.LogDebug("Built multi-host data source for connection '{ConnectionName}' with target session '{TargetSession}'", section.Key, target);
            }
        }

        return result;
    }

    /// <summary>
    /// Builds the main data source (handles both single-host and multi-host).
    /// </summary>
    public NpgsqlDataSource BuildMainDataSource(string connectionString)
    {
        if (IsMultiHostConnectionString(connectionString))
        {
            var target = GetTargetSessionAttribute(ConnectionName);
            var multiHost = new NpgsqlDataSourceBuilder(connectionString).BuildMultiHost();
            Logger?.LogDebug("Using multi-host data source with target session '{TargetSession}'", target);
            return multiHost.WithTargetSession(target);
        }
        return new NpgsqlDataSourceBuilder(connectionString).Build();
    }

    public ValidationOptions BuildValidationOptions()
    {
        var validationCfg = _config.Cfg.GetSection("ValidationOptions");
        var defaults = new ValidationOptions();

        if (validationCfg.Exists() is false || _config.GetConfigBool("Enabled", validationCfg) is false)
        {
            Logger?.LogDebug("Validation options disabled or not configured. Using defaults.");
            return defaults;
        }

        var result = new ValidationOptions
        {
            Rules = new Dictionary<string, ValidationRule>()
        };

        var rulesCfg = validationCfg.GetSection("Rules");
        foreach (var ruleSection in rulesCfg.GetChildren())
        {
            var ruleName = ruleSection.Key;
            if (string.IsNullOrEmpty(ruleName))
            {
                continue;
            }

            var type = _config.GetConfigEnum<ValidationType?>("Type", ruleSection);
            if (type is null)
            {
                Logger?.LogWarning("Validation rule '{RuleName}' has no valid Type specified, skipping.", ruleName);
                continue;
            }

            var pattern = _config.GetConfigStr("Pattern", ruleSection);
            var minLength = _config.GetConfigInt("MinLength", ruleSection);
            var maxLength = _config.GetConfigInt("MaxLength", ruleSection);

            // Skip rules with missing required properties for specific types
            if (type == ValidationType.Regex && string.IsNullOrEmpty(pattern))
            {
                Logger?.LogWarning("Validation rule '{RuleName}' has Type 'Regex' but no Pattern specified, skipping.", ruleName);
                continue;
            }
            if (type == ValidationType.MinLength && minLength is null)
            {
                Logger?.LogWarning("Validation rule '{RuleName}' has Type 'MinLength' but no MinLength specified, skipping.", ruleName);
                continue;
            }
            if (type == ValidationType.MaxLength && maxLength is null)
            {
                Logger?.LogWarning("Validation rule '{RuleName}' has Type 'MaxLength' but no MaxLength specified, skipping.", ruleName);
                continue;
            }

            var rule = new ValidationRule
            {
                Type = type.Value,
                Pattern = pattern,
                MinLength = minLength,
                MaxLength = maxLength,
                Message = _config.GetConfigStr("Message", ruleSection) ?? $"Validation failed for parameter '{{0}}'",
                StatusCode = _config.GetConfigInt("StatusCode", ruleSection) ?? 400
            };

            result.Rules[ruleName] = rule;
            Logger?.LogDebug("Registered validation rule '{RuleName}': {Rule}", ruleName, rule);
        }

        if (result.Rules.Count == 0)
        {
            Logger?.LogDebug("No validation rules configured, using defaults.");
            return defaults;
        }

        Logger?.LogDebug("Using {Count} validation rules: {Rules}",
            result.Rules.Count,
            string.Join(", ", result.Rules.Keys));

        return result;
    }
}