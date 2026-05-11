using System.Collections.Frozen;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.HttpOverrides;
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
    public Microsoft.Extensions.Logging.ILogger? ClientLogger { get; private set; } = null;
    public bool UseHttpsRedirection { get; private set; } = false;
    public bool UseHsts { get; private set; } = false;
    public BearerTokenConfig? BearerTokenConfig { get; private set; } = null;
    public JwtTokenConfig? JwtTokenConfig { get; private set; } = null;
    /// <summary>BearerToken-type entries registered under <c>Auth:Schemes</c>. Each carries its own scheme name and (optionally) refresh path.</summary>
    public List<BearerTokenConfig> AdditionalBearerTokenConfigs { get; } = [];
    /// <summary>Jwt-type entries registered under <c>Auth:Schemes</c>. Each carries its own scheme name, secret, validation options and (optionally) refresh path.</summary>
    public List<JwtTokenConfig> AdditionalJwtTokenConfigs { get; } = [];
    /// <summary>
    /// Every registered cookie scheme in registration order — main scheme first (when CookieAuth is on),
    /// then each Cookie-type entry under <c>Auth:Schemes</c>. Each tuple pairs the scheme name with the
    /// HTTP cookie name actually written to the request. Consumed by the cookie-aware
    /// <c>ForwardDefaultSelector</c> on the policy scheme so that requests bearing a named-scheme cookie
    /// authenticate under that scheme (not just the default).
    /// </summary>
    public List<(string SchemeName, string CookieName)> CookieSchemesInOrder { get; } = [];
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
            var transformedKestrelConfig = _config.TransformSection(kestrelConfig);

            Instance.WebHost.ConfigureKestrel((context, options) =>
            {
                options.Configure(transformedKestrelConfig);

                options.DisableStringReuse = _config.GetConfigBool("DisableStringReuse", kestrelConfig, options.DisableStringReuse);
                options.AllowAlternateSchemes = _config.GetConfigBool("AllowAlternateSchemes", kestrelConfig, options.AllowAlternateSchemes);
                options.AllowSynchronousIO = _config.GetConfigBool("AllowSynchronousIO", kestrelConfig, options.AllowSynchronousIO);
                options.AllowResponseHeaderCompression = _config.GetConfigBool("AllowResponseHeaderCompression", kestrelConfig, options.AllowResponseHeaderCompression);
                options.AddServerHeader = _config.GetConfigBool("AddServerHeader", kestrelConfig, options.AddServerHeader);
                options.AllowHostHeaderOverride = _config.GetConfigBool("AllowSynchronousIO", kestrelConfig, options.AllowHostHeaderOverride);

                var limitsSection = transformedKestrelConfig.GetSection("Limits");
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
            bool npgsqlRestClientAdded = false;
            bool systemAdded = false;
            bool microsoftAdded = false;
            var appName = _config.GetConfigStr("ApplicationName", _config.Cfg);
            var envName = _config.GetConfigStr("EnvironmentName", _config.Cfg) ?? "Production";
            string clientLoggerName = string.IsNullOrEmpty(appName) ? "NpgsqlRestClient" : appName;

            foreach (var level in logCfg?.GetSection("MinimalLevels")?.GetChildren() ?? [])
            {
                var key = level.Key;
                var value = _config.GetEnum<Serilog.Events.LogEventLevel?>(level.Value);
                if (value is not null && key is not null)
                {
                    if (string.Equals(key, "NpgsqlRest", StringComparison.OrdinalIgnoreCase))
                    {
                        loggerConfig.MinimumLevel.Override("NpgsqlRest", value.Value);
                        npgsqlRestAdded = true;
                    }
                    else if (string.Equals(key, "NpgsqlRestClient", StringComparison.OrdinalIgnoreCase))
                    {
                        loggerConfig.MinimumLevel.Override(clientLoggerName, value.Value);
                        npgsqlRestClientAdded = true;
                    }
                    else if (string.Equals(key, "System", StringComparison.OrdinalIgnoreCase))
                    {
                        loggerConfig.MinimumLevel.Override(key, value.Value);
                        systemAdded = true;
                    }
                    else if (string.Equals(key, "Microsoft", StringComparison.OrdinalIgnoreCase))
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
                loggerConfig.MinimumLevel.Override("NpgsqlRest", Serilog.Events.LogEventLevel.Information);
            }
            if (npgsqlRestClientAdded is false)
            {
                loggerConfig.MinimumLevel.Override(clientLoggerName, Serilog.Events.LogEventLevel.Information);
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
            var serilogFactory = new SerilogLoggerFactory(serilog);

            // Core library logger - always uses "NpgsqlRest" name
            Logger = serilogFactory.CreateLogger("NpgsqlRest");
            // Client application logger - uses ApplicationName or "NpgsqlRestClient"
            ClientLogger = serilogFactory.CreateLogger(clientLoggerName);

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
            
            ClientLogger?.LogDebug("----> Starting with configuration(s): {providerString}", providerString);
            if (logDebug.Count > 0)
            {
                ClientLogger?.LogDebug("----> Logging enabled: {logDebug}", string.Join(", ", logDebug));
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
            ClientLogger?.LogDebug("Using default error handling options: DefaultErrorCodePolicy={DefaultErrorCodePolicy}, TimeoutErrorMapping=[{TimeoutErrorMapping}], ErrorCodePolicies=[{ErrorCodePolicies}]",
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

        ClientLogger?.LogDebug("Using error handling options: DefaultErrorCodePolicy={DefaultErrorCodePolicy}, RemoveTypeUrl={RemoveTypeUrl}, RemoveTraceId={RemoveTraceId}, TimeoutErrorMapping=[{TimeoutErrorMapping}], ErrorCodePolicies=[{ErrorCodePolicies}]",
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
            ClientLogger?.LogDebug("Data Protection keys encrypted with certificate from {certPath}", certPath);
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
            ClientLogger?.LogDebug("Data Protection keys encrypted with DPAPI (LocalMachine: {protectToLocalMachine})", protectToLocalMachine);
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
            ClientLogger?.LogDebug("Using Data Protection for application {customAppName} with default provider. Expiration in {expiresInDays} days.",
                customAppName,
                expiresInDays);
        }
        else
        {
            ClientLogger?.LogDebug($"Using Data Protection for application {{customAppName}} in{(dirInfo is null ? " " : " directory ")}{{dirInfo}}. Expiration in {{expiresInDays}} days.",
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

        // Pre-scan Auth:Schemes for enabled Cookie-type entries so we can decide upfront whether a
        // policy scheme is needed (and, if so, whether the existing composite name collides with a
        // single main scheme's own name). Full registration runs later in RegisterAuthSchemes.
        var namedCookieSchemeCount = CountEnabledNamedCookieSchemes(authCfg);
        var totalCookieSchemes = (cookieAuth ? 1 : 0) + namedCookieSchemeCount;

        // Policy scheme triggers:
        //   - Existing case: more than one of {cookies, bearer, jwt} is enabled.
        //   - New case: cookies enabled plus one or more named cookie schemes — so requests bearing a
        //     named-scheme cookie can still authenticate against ASP.NET's default scheme.
        var needsPolicyScheme = enabledSchemes.Count > 1 || totalCookieSchemes > 1;

        // defaultScheme is what ASP.NET treats as the DefaultAuthenticateScheme. When we register a
        // policy scheme, this MUST be the policy scheme's name. The composite "_and_"-joined name is
        // distinct from any individual scheme. But when only one main scheme is enabled, we can't
        // reuse its name for the policy scheme (collides with the existing AddCookie/AddJwtBearer/...
        // registration), so a synthetic name is used instead.
        const string SyntheticPolicySchemeName = "NpgsqlRest_PolicyScheme";
        string defaultScheme;
        string? policySchemeName;
        if (enabledSchemes.Count > 1)
        {
            defaultScheme = string.Join("_and_", enabledSchemes);
            policySchemeName = defaultScheme;
        }
        else if (needsPolicyScheme)
        {
            defaultScheme = SyntheticPolicySchemeName;
            policySchemeName = SyntheticPolicySchemeName;
        }
        else
        {
            defaultScheme = enabledSchemes[0];
            policySchemeName = null;
        }

        // Fail fast if any of the four legacy integer time fields is still present in the user's config
        // (CookieValidDays / BearerTokenExpireHours / JwtExpireMinutes / JwtRefreshExpireDays). They were
        // removed in 3.13.0 in favor of the interval-notation fields.
        DetectLegacyAuthTimeFields(authCfg);

        var auth = Instance.Services.AddAuthentication(defaultScheme);

        if (cookieAuth is true)
        {
            var cookieValid = ResolveAuthTimeSpan("CookieValid", TimeSpan.FromDays(14), authCfg);
            var mainCookieNameRaw = _config.GetConfigStr("CookieName", authCfg);
            var mainSameSite = ResolveSameSiteMode("CookieSameSite", authCfg);
            var mainSecure = ResolveCookieSecurePolicy("CookieSecure", authCfg);
            WarnIfSameSiteNoneWithoutSecure(authCfg.Path, mainSameSite, mainSecure);
            auth.AddCookie(cookieScheme, options =>
            {
                options.ExpireTimeSpan = cookieValid;
                if (string.IsNullOrEmpty(mainCookieNameRaw) is false)
                {
                    options.Cookie.Name = mainCookieNameRaw;
                }
                options.Cookie.Path = _config.GetConfigStr("CookiePath", authCfg);
                options.Cookie.Domain = _config.GetConfigStr("CookieDomain", authCfg);
                options.Cookie.MaxAge = _config.GetConfigBool("CookieMultiSessions", authCfg, true) is true ? cookieValid : null;
                options.Cookie.HttpOnly = _config.GetConfigBool("CookieHttpOnly", authCfg, true) is true;
                if (mainSameSite is not null) options.Cookie.SameSite = mainSameSite.Value;
                if (mainSecure is not null) options.Cookie.SecurePolicy = mainSecure.Value;
            });
            // Track the resolved cookie name for the policy-scheme selector. When CookieName is unset,
            // ASP.NET defaults Cookie.Name to ".AspNetCore.<schemeName>".
            CookieSchemesInOrder.Add((cookieScheme,
                string.IsNullOrEmpty(mainCookieNameRaw) ? $".AspNetCore.{cookieScheme}" : mainCookieNameRaw));
            ClientLogger?.LogDebug(
                "Using Cookie Authentication with scheme {cookieScheme}. Cookie expires in {cookieValid}. SameSite={SameSite}, Secure={Secure}.",
                cookieScheme, cookieValid,
                mainSameSite?.ToString() ?? "<default>",
                mainSecure?.ToString() ?? "<default>");
        }

        if (bearerTokenAuth is true)
        {
            var bearerExpire = ResolveAuthTimeSpan("BearerTokenExpire", TimeSpan.FromHours(1), authCfg);
            BearerTokenConfig = new()
            {
                Scheme = tokenScheme,
                RefreshPath = _config.GetConfigStr("BearerTokenRefreshPath", authCfg)
            };
            auth.AddBearerToken(tokenScheme, options =>
            {
                options.BearerTokenExpiration = bearerExpire;
                options.RefreshTokenExpiration = bearerExpire;
                options.Validate();
            });
            ClientLogger?.LogDebug(
                "Using Bearer Token Authentication with scheme {tokenScheme}. Token expires in {bearerExpire}. Refresh path is {RefreshPath}",
                tokenScheme,
                bearerExpire,
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

            var jwtExpire = ResolveAuthTimeSpan("JwtExpire", TimeSpan.FromMinutes(60), authCfg);
            var jwtRefreshExpire = ResolveAuthTimeSpan("JwtRefreshExpire", TimeSpan.FromDays(7), authCfg);
            var clockSkew = Parser.ParsePostgresInterval(_config.GetConfigStr("JwtClockSkew", authCfg)) ?? TimeSpan.FromMinutes(5);

            JwtTokenConfig = new()
            {
                Scheme = jwtScheme,
                Secret = jwtSecret,
                Issuer = _config.GetConfigStr("JwtIssuer", authCfg),
                Audience = _config.GetConfigStr("JwtAudience", authCfg),
                Expire = jwtExpire,
                RefreshExpire = jwtRefreshExpire,
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

            ClientLogger?.LogDebug(
                "Using JWT Authentication with scheme {jwtScheme}. Access token expires in {jwtExpire}. Refresh token expires in {jwtRefreshExpire}. Refresh path is {RefreshPath}",
                jwtScheme,
                jwtExpire,
                jwtRefreshExpire,
                JwtTokenConfig.RefreshPath);
        }

        // Register additional authentication schemes from Auth:Schemes. Each enabled entry registers a
        // fully-fledged ASP.NET Core authentication scheme (Cookie, BearerToken, or Jwt) so a login
        // function can return that scheme's name in its `scheme` column to sign in under those options.
        RegisterAuthSchemes(auth, authCfg, cookieScheme, tokenScheme, jwtScheme);

        // Register the policy scheme whenever we have:
        //   - more than one of {cookies, bearer, jwt} enabled (legacy case), OR
        //   - more than one cookie scheme (main + named, or multiple named) — the named-schemes case.
        // For the cookie-only case the synthetic policy name is used (see SyntheticPolicySchemeName
        // above) to avoid colliding with the main cookie scheme's own AddCookie registration.
        if (needsPolicyScheme)
        {
            // Capture the cookie-scheme list by reference: it gets filled out below by
            // RegisterAuthSchemes → RegisterCookieSchemeFromConfig before any request is dispatched.
            var cookieSchemes = CookieSchemesInOrder;
            auth.AddPolicyScheme(policySchemeName!, policySchemeName!, options =>
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

                    // Cookie-aware dispatch: walk registered cookie schemes in order and return the
                    // first one whose configured cookie name is present in the request. This lets
                    // named-scheme cookies authenticate against their own scheme (so any endpoint that
                    // consults context.User — passkey endpoints, @authorize, plain IsAuthenticated
                    // checks — accepts them just like the main cookie).
                    foreach (var (schemeName, cookieName) in cookieSchemes)
                    {
                        if (context.Request.Cookies.ContainsKey(cookieName))
                        {
                            return schemeName;
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

    /// <summary>
    /// Resolves a <see cref="Microsoft.AspNetCore.Http.SameSiteMode"/> from a string config value. Accepts the four ASP.NET enum
    /// names case-insensitively (<c>Unspecified</c>, <c>None</c>, <c>Lax</c>, <c>Strict</c>). Returns
    /// null when the key is unset/empty so the caller can preserve ASP.NET's per-handler default.
    /// Throws on unknown values — silently falling back hides typos in security-relevant config.
    /// </summary>
    private Microsoft.AspNetCore.Http.SameSiteMode? ResolveSameSiteMode(string key, IConfigurationSection section)
    {
        var raw = _config.GetConfigStr(key, section);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (Enum.TryParse<Microsoft.AspNetCore.Http.SameSiteMode>(raw, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }
        throw new InvalidOperationException(
            $"Invalid value '{raw}' for {section.Path}:{key}. Expected one of: Unspecified, None, Lax, Strict.");
    }

    /// <summary>
    /// Resolves a <see cref="CookieSecurePolicy"/> from a string config value. Accepts the three
    /// ASP.NET enum names case-insensitively (<c>SameAsRequest</c>, <c>Always</c>, <c>None</c>).
    /// Returns null when unset so ASP.NET's default (<c>SameAsRequest</c>) applies. Throws on unknown
    /// values.
    /// </summary>
    private CookieSecurePolicy? ResolveCookieSecurePolicy(string key, IConfigurationSection section)
    {
        var raw = _config.GetConfigStr(key, section);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (Enum.TryParse<CookieSecurePolicy>(raw, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }
        throw new InvalidOperationException(
            $"Invalid value '{raw}' for {section.Path}:{key}. Expected one of: SameAsRequest, Always, None.");
    }

    /// <summary>
    /// Logs a warning at startup when a cookie scheme is configured with <c>SameSite=None</c> but
    /// without forcing <c>Secure=Always</c>. Browsers silently drop <c>SameSite=None</c> cookies that
    /// lack the <c>Secure</c> attribute, which produces a hard-to-diagnose "logs in, but no cookie
    /// arrives on the next request" symptom — especially under local HTTP testing.
    /// </summary>
    private void WarnIfSameSiteNoneWithoutSecure(string sectionPath, Microsoft.AspNetCore.Http.SameSiteMode? sameSite, CookieSecurePolicy? secure)
    {
        if (sameSite == Microsoft.AspNetCore.Http.SameSiteMode.None && secure != CookieSecurePolicy.Always)
        {
            ClientLogger?.LogWarning(
                "{Path}:CookieSameSite=None requires CookieSecure=Always — browsers drop SameSite=None cookies " +
                "without the Secure attribute. Set {Path}:CookieSecure to \"Always\".",
                sectionPath, sectionPath);
        }
    }

    /// <summary>
    /// Pre-scan helper: counts enabled Cookie-type entries under <c>Auth:Schemes</c> without registering
    /// them. Used by <see cref="BuildAuthentication"/> to decide upfront whether a policy scheme is
    /// needed (and what default scheme name to pass to <c>AddAuthentication</c>) before
    /// <see cref="RegisterAuthSchemes"/> runs the real registration. Validation and full processing
    /// happen later in <see cref="RegisterCookieSchemeFromConfig"/>; this method is intentionally lax —
    /// it skips entries whose <c>Type</c> isn't <c>Cookies</c> and trusts <see cref="RegisterAuthSchemes"/>
    /// to throw on shape errors.
    /// </summary>
    private int CountEnabledNamedCookieSchemes(IConfigurationSection authCfg)
    {
        var schemesCfg = authCfg.GetSection("Schemes");
        if (!schemesCfg.Exists())
        {
            return 0;
        }
        var count = 0;
        foreach (var sch in schemesCfg.GetChildren())
        {
            if (string.IsNullOrWhiteSpace(sch.Key))
            {
                continue;
            }
            if (_config.GetConfigBool("Enabled", sch, true) is false)
            {
                continue;
            }
            var typeStr = _config.GetConfigStr("Type", sch);
            if (string.Equals(typeStr, "Cookies", StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Resolves an auth-related <see cref="TimeSpan"/> from a Postgres-interval string at the given key.
    /// Returns <paramref name="defaultValue"/> when the field is null/empty/missing. Throws on
    /// syntactically invalid interval values (fail-fast at startup).
    ///
    /// Reads from the section provided in <paramref name="authCfg"/> — this is reused for both the root
    /// <c>Auth</c> section and individual <c>Auth:Schemes:&lt;name&gt;</c> sections.
    /// </summary>
    public TimeSpan ResolveAuthTimeSpan(string key, TimeSpan defaultValue, IConfigurationSection authCfg)
    {
        var raw = _config.GetConfigStr(key, authCfg);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }
        return Parser.ParsePostgresInterval(raw)
            ?? throw new InvalidOperationException(
                $"Invalid interval value for {authCfg.Path}:{key}: '{raw}'. Expected Postgres interval syntax (e.g. '14 days', '1 hour', '60 minutes').");
    }

    /// <summary>
    /// Throws if any of the four removed legacy auth time fields appears in the user's config. We
    /// hard-cut these in 3.13.0 (interval notation is the only supported form). Failing fast — rather
    /// than silently ignoring legacy fields — prevents the surprising case where an upgraded user's
    /// "this should be 30 days" expiration silently flips to the new field's default of 14 days.
    /// </summary>
    private void DetectLegacyAuthTimeFields(IConfigurationSection authCfg)
    {
        DetectLegacyAuthTimeField(authCfg, "CookieValidDays", "CookieValid", "e.g. \"14 days\"");
        DetectLegacyAuthTimeField(authCfg, "BearerTokenExpireHours", "BearerTokenExpire", "e.g. \"1 hour\"");
        DetectLegacyAuthTimeField(authCfg, "JwtExpireMinutes", "JwtExpire", "e.g. \"60 minutes\"");
        DetectLegacyAuthTimeField(authCfg, "JwtRefreshExpireDays", "JwtRefreshExpire", "e.g. \"7 days\"");
    }

    private void DetectLegacyAuthTimeField(IConfigurationSection authCfg, string legacyKey, string newKey, string example)
    {
        // Distinguish "key explicitly set" from "key absent" by checking the section directly. Calling
        // GetConfigStr/GetConfigInt would return null in both cases.
        var section = authCfg.GetSection(legacyKey);
        if (section.Value is null && !section.GetChildren().Any())
        {
            return;
        }
        throw new InvalidOperationException(
            $"Auth:{legacyKey} has been removed in 3.13.0. Use Auth:{newKey} with Postgres interval syntax instead ({example}). " +
            $"See changelog/v3.13.0.md for migration details.");
    }

    /// <summary>
    /// Registers additional authentication schemes declared under <c>Auth:Schemes</c>. Each enabled
    /// entry becomes a fully-fledged ASP.NET Core authentication scheme of type Cookies, BearerToken,
    /// or Jwt — login functions returning the scheme's name in the <c>scheme</c> column sign in under
    /// those options.
    ///
    /// All scheme types inherit any unset field from the root <c>Auth</c> section so blocks stay small.
    /// Validation (fail-fast at startup):
    /// <list type="bullet">
    ///   <item>Scheme name must not collide with the main scheme names (CookieAuthScheme,
    ///   BearerTokenAuthScheme, JwtAuthScheme).</item>
    ///   <item><c>Type</c> must be one of Cookies / BearerToken / Jwt (case-insensitive).</item>
    ///   <item>Explicit <c>CookieName</c> values must be unique across all cookie schemes.</item>
    ///   <item>Refresh paths must be unique across all schemes that define one.</item>
    ///   <item>Jwt schemes inherit <c>JwtSecret</c> from root if unset; secret must be ≥32 chars.</item>
    /// </list>
    ///
    /// BearerToken/Jwt scheme configs are appended to <see cref="AdditionalBearerTokenConfigs"/> /
    /// <see cref="AdditionalJwtTokenConfigs"/> so <see cref="App"/> can wire per-scheme refresh
    /// middlewares and the <see cref="JwtLoginHandler"/> can resolve the right config when a login
    /// function returns one of these scheme names.
    /// </summary>
    public void RegisterAuthSchemes(
        Microsoft.AspNetCore.Authentication.AuthenticationBuilder auth,
        IConfigurationSection authCfg,
        string cookieScheme,
        string tokenScheme,
        string jwtScheme)
    {
        var schemesCfg = authCfg.GetSection("Schemes");
        if (!schemesCfg.Exists())
        {
            return;
        }

        // Track CookieName values across the main cookie scheme + Cookie-type schemes that set one
        // explicitly. Schemes that don't set CookieName use ASP.NET's per-scheme `.AspNetCore.<name>`
        // default which auto-differs and is excluded from collision tracking.
        var explicitCookieNames = new HashSet<string>(StringComparer.Ordinal);
        var mainCookieName = _config.GetConfigStr("CookieName", authCfg);
        if (!string.IsNullOrEmpty(mainCookieName))
        {
            explicitCookieNames.Add(mainCookieName);
        }

        // Track refresh paths across the main scheme + every BearerToken/Jwt-type scheme that defines
        // one. Two middlewares listening on the same path would race — fail at startup instead.
        var refreshPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mainBearerRefreshPath = _config.GetConfigStr("BearerTokenRefreshPath", authCfg);
        if (!string.IsNullOrEmpty(mainBearerRefreshPath))
        {
            refreshPaths.Add(mainBearerRefreshPath);
        }
        var mainJwtRefreshPath = _config.GetConfigStr("JwtRefreshPath", authCfg);
        if (!string.IsNullOrEmpty(mainJwtRefreshPath))
        {
            refreshPaths.Add(mainJwtRefreshPath);
        }

        foreach (var schemeSection in schemesCfg.GetChildren())
        {
            var schemeName = schemeSection.Key;
            if (string.IsNullOrWhiteSpace(schemeName))
            {
                throw new InvalidOperationException("Auth:Schemes contains an entry with an empty name.");
            }

            if (_config.GetConfigBool("Enabled", schemeSection, true) is false)
            {
                ClientLogger?.LogDebug("Auth scheme {SchemeName} is disabled. Skipping.", schemeName);
                continue;
            }

            var typeStr = _config.GetConfigStr("Type", schemeSection);
            if (string.IsNullOrWhiteSpace(typeStr))
            {
                throw new InvalidOperationException(
                    $"Auth:Schemes:{schemeName}:Type is required. Valid: Cookies, BearerToken, Jwt.");
            }

            // Scheme name must not shadow any main scheme — otherwise AddX(name, …) would either
            // overwrite the main scheme's options or throw on duplicate registration.
            if (string.Equals(schemeName, cookieScheme, StringComparison.Ordinal)
                || string.Equals(schemeName, tokenScheme, StringComparison.Ordinal)
                || string.Equals(schemeName, jwtScheme, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Auth:Schemes:{schemeName}: scheme name collides with a main authentication scheme " +
                    $"(CookieAuthScheme/BearerTokenAuthScheme/JwtAuthScheme). Choose a distinct name.");
            }

            if (string.Equals(typeStr, "Cookies", StringComparison.OrdinalIgnoreCase))
            {
                RegisterCookieSchemeFromConfig(auth, authCfg, schemeSection, schemeName, mainCookieName, explicitCookieNames);
            }
            else if (string.Equals(typeStr, "BearerToken", StringComparison.OrdinalIgnoreCase))
            {
                RegisterBearerTokenSchemeFromConfig(auth, authCfg, schemeSection, schemeName, refreshPaths);
            }
            else if (string.Equals(typeStr, "Jwt", StringComparison.OrdinalIgnoreCase))
            {
                RegisterJwtSchemeFromConfig(auth, authCfg, schemeSection, schemeName, refreshPaths);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Auth:Schemes:{schemeName}:Type='{typeStr}' is not supported. Valid: Cookies, BearerToken, Jwt.");
            }
        }
    }

    private void RegisterCookieSchemeFromConfig(
        Microsoft.AspNetCore.Authentication.AuthenticationBuilder auth,
        IConfigurationSection authCfg,
        IConfigurationSection schemeSection,
        string schemeName,
        string? mainCookieName,
        HashSet<string> explicitCookieNames)
    {
        // CookieValid: scheme override wins, else inherit root's resolved value, else default 14 days.
        var expire = ResolveSchemeIntervalWithRootFallback(
            schemeSection, authCfg, "CookieValid", TimeSpan.FromDays(14));

        var schemeCookieName = _config.GetConfigStr("CookieName", schemeSection);
        if (string.IsNullOrEmpty(schemeCookieName))
        {
            schemeCookieName = mainCookieName;
        }
        else if (!explicitCookieNames.Add(schemeCookieName))
        {
            throw new InvalidOperationException(
                $"Auth:Schemes:{schemeName}:CookieName='{schemeCookieName}' collides with another scheme's CookieName. " +
                $"Each scheme (and the main cookie scheme) that sets CookieName explicitly must use a distinct value.");
        }

        var path = _config.GetConfigStr("CookiePath", schemeSection)
            ?? _config.GetConfigStr("CookiePath", authCfg);
        var domain = _config.GetConfigStr("CookieDomain", schemeSection)
            ?? _config.GetConfigStr("CookieDomain", authCfg);
        var multiSessions = _config.GetConfigBool("CookieMultiSessions", schemeSection,
            _config.GetConfigBool("CookieMultiSessions", authCfg, true));
        var httpOnly = _config.GetConfigBool("CookieHttpOnly", schemeSection,
            _config.GetConfigBool("CookieHttpOnly", authCfg, true));
        // SameSite / Secure: scheme-level value wins, else inherit root, else leave null (ASP.NET default).
        var sameSite = ResolveSameSiteMode("CookieSameSite", schemeSection)
            ?? ResolveSameSiteMode("CookieSameSite", authCfg);
        var secure = ResolveCookieSecurePolicy("CookieSecure", schemeSection)
            ?? ResolveCookieSecurePolicy("CookieSecure", authCfg);
        WarnIfSameSiteNoneWithoutSecure(schemeSection.Path, sameSite, secure);

        // Capture locals for the closure (avoid loop-variable issues).
        var name = schemeCookieName;
        var capturedPath = path;
        var capturedDomain = domain;
        var capturedMulti = multiSessions;
        var capturedHttp = httpOnly;
        var capturedExpire = expire;
        var capturedSameSite = sameSite;
        var capturedSecure = secure;
        auth.AddCookie(schemeName, options =>
        {
            options.ExpireTimeSpan = capturedExpire;
            if (!string.IsNullOrEmpty(name))
            {
                options.Cookie.Name = name;
            }
            options.Cookie.Path = capturedPath;
            options.Cookie.Domain = capturedDomain;
            options.Cookie.MaxAge = capturedMulti ? capturedExpire : null;
            options.Cookie.HttpOnly = capturedHttp;
            if (capturedSameSite is not null) options.Cookie.SameSite = capturedSameSite.Value;
            if (capturedSecure is not null) options.Cookie.SecurePolicy = capturedSecure.Value;
        });
        // Track for the policy-scheme cookie-aware selector. Empty name → ASP.NET-defaulted
        // .AspNetCore.<schemeName>.
        CookieSchemesInOrder.Add((schemeName,
            string.IsNullOrEmpty(name) ? $".AspNetCore.{schemeName}" : name));
        ClientLogger?.LogDebug(
            "Registered Auth Scheme {SchemeName} (Type=Cookies). Expires in {Expire}. MultiSessions={Multi}, HttpOnly={Http}, CookieName={CookieName}, SameSite={SameSite}, Secure={Secure}.",
            schemeName, expire, multiSessions, httpOnly,
            name ?? $"<default `.AspNetCore.{schemeName}`>",
            sameSite?.ToString() ?? "<default>",
            secure?.ToString() ?? "<default>");
    }

    private void RegisterBearerTokenSchemeFromConfig(
        Microsoft.AspNetCore.Authentication.AuthenticationBuilder auth,
        IConfigurationSection authCfg,
        IConfigurationSection schemeSection,
        string schemeName,
        HashSet<string> refreshPaths)
    {
        var expire = ResolveSchemeIntervalWithRootFallback(
            schemeSection, authCfg, "BearerTokenExpire", TimeSpan.FromHours(1));

        // RefreshPath is per-scheme (no root inheritance — each scheme owns its refresh middleware or
        // doesn't have one). Empty/null = no refresh middleware for this scheme.
        var refreshPath = _config.GetConfigStr("BearerTokenRefreshPath", schemeSection);
        if (!string.IsNullOrEmpty(refreshPath) && !refreshPaths.Add(refreshPath))
        {
            throw new InvalidOperationException(
                $"Auth:Schemes:{schemeName}:BearerTokenRefreshPath='{refreshPath}' collides with another scheme's refresh path. " +
                $"Each refresh path must be unique across the main scheme and every scheme that defines one.");
        }

        var capturedExpire = expire;
        auth.AddBearerToken(schemeName, options =>
        {
            options.BearerTokenExpiration = capturedExpire;
            options.RefreshTokenExpiration = capturedExpire;
            options.Validate();
        });

        AdditionalBearerTokenConfigs.Add(new BearerTokenConfig
        {
            Scheme = schemeName,
            RefreshPath = refreshPath
        });

        ClientLogger?.LogDebug(
            "Registered Auth Scheme {SchemeName} (Type=BearerToken). Expires in {Expire}. RefreshPath={RefreshPath}.",
            schemeName, expire, refreshPath ?? "<none>");
    }

    private void RegisterJwtSchemeFromConfig(
        Microsoft.AspNetCore.Authentication.AuthenticationBuilder auth,
        IConfigurationSection authCfg,
        IConfigurationSection schemeSection,
        string schemeName,
        HashSet<string> refreshPaths)
    {
        // Jwt schemes can override every JWT config field; if unset, inherit from root.
        var secret = _config.GetConfigStr("JwtSecret", schemeSection)
            ?? _config.GetConfigStr("JwtSecret", authCfg);
        if (string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException(
                $"Auth:Schemes:{schemeName} (Type=Jwt) requires JwtSecret either on the scheme itself or on the root Auth section.");
        }
        if (secret.Length < 32)
        {
            throw new InvalidOperationException(
                $"Auth:Schemes:{schemeName}:JwtSecret must be at least 32 characters long for HS256 algorithm.");
        }

        var issuer = _config.GetConfigStr("JwtIssuer", schemeSection)
            ?? _config.GetConfigStr("JwtIssuer", authCfg);
        var audience = _config.GetConfigStr("JwtAudience", schemeSection)
            ?? _config.GetConfigStr("JwtAudience", authCfg);

        var expire = ResolveSchemeIntervalWithRootFallback(
            schemeSection, authCfg, "JwtExpire", TimeSpan.FromMinutes(60));
        var refreshExpire = ResolveSchemeIntervalWithRootFallback(
            schemeSection, authCfg, "JwtRefreshExpire", TimeSpan.FromDays(7));
        var clockSkew = ResolveSchemeIntervalWithRootFallback(
            schemeSection, authCfg, "JwtClockSkew", TimeSpan.FromMinutes(5));

        var validateIssuer = _config.GetConfigBool("JwtValidateIssuer", schemeSection,
            _config.GetConfigBool("JwtValidateIssuer", authCfg, false));
        var validateAudience = _config.GetConfigBool("JwtValidateAudience", schemeSection,
            _config.GetConfigBool("JwtValidateAudience", authCfg, false));
        var validateLifetime = _config.GetConfigBool("JwtValidateLifetime", schemeSection,
            _config.GetConfigBool("JwtValidateLifetime", authCfg, true));
        var validateSigningKey = _config.GetConfigBool("JwtValidateIssuerSigningKey", schemeSection,
            _config.GetConfigBool("JwtValidateIssuerSigningKey", authCfg, true));

        // Per-scheme JwtRefreshPath is independent of root JwtRefreshPath. Empty = no refresh middleware.
        var refreshPath = _config.GetConfigStr("JwtRefreshPath", schemeSection);
        if (!string.IsNullOrEmpty(refreshPath) && !refreshPaths.Add(refreshPath))
        {
            throw new InvalidOperationException(
                $"Auth:Schemes:{schemeName}:JwtRefreshPath='{refreshPath}' collides with another scheme's refresh path. " +
                $"Each refresh path must be unique across the main scheme and every scheme that defines one.");
        }

        var jwtConfig = new JwtTokenConfig
        {
            Scheme = schemeName,
            Secret = secret,
            Issuer = issuer,
            Audience = audience,
            Expire = expire,
            RefreshExpire = refreshExpire,
            ClockSkew = clockSkew,
            ValidateIssuer = validateIssuer,
            ValidateAudience = validateAudience,
            ValidateLifetime = validateLifetime,
            ValidateIssuerSigningKey = validateSigningKey,
            RefreshPath = refreshPath
        };

        auth.AddJwtBearer(schemeName, options =>
        {
            options.TokenValidationParameters = jwtConfig.GetTokenValidationParameters();
            options.SaveToken = true;
        });

        AdditionalJwtTokenConfigs.Add(jwtConfig);

        ClientLogger?.LogDebug(
            "Registered Auth Scheme {SchemeName} (Type=Jwt). Access expires in {Expire}, refresh in {RefreshExpire}. RefreshPath={RefreshPath}.",
            schemeName, expire, refreshExpire, refreshPath ?? "<none>");
    }

    /// <summary>
    /// Resolves a TimeSpan from a scheme section's <paramref name="key"/>, falling back to the root
    /// <paramref name="authCfg"/> section's same key, then to <paramref name="defaultValue"/>. Throws
    /// on syntactically invalid interval values at either level.
    /// </summary>
    private TimeSpan ResolveSchemeIntervalWithRootFallback(
        IConfigurationSection schemeSection,
        IConfigurationSection authCfg,
        string key,
        TimeSpan defaultValue)
    {
        var schemeRaw = _config.GetConfigStr(key, schemeSection);
        if (!string.IsNullOrWhiteSpace(schemeRaw))
        {
            return Parser.ParsePostgresInterval(schemeRaw)
                ?? throw new InvalidOperationException(
                    $"Invalid interval value for Auth:Schemes:{schemeSection.Key}:{key}: '{schemeRaw}'. Expected Postgres interval syntax.");
        }
        return ResolveAuthTimeSpan(key, defaultValue, authCfg);
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
            RateLimiterPolicy = _config.GetConfigStr("RateLimiterPolicy", passkeyCfg),
            ConnectionName = _config.GetConfigStr("ConnectionName", passkeyCfg),
            CommandRetryStrategy = _config.GetConfigStr("CommandRetryStrategy", passkeyCfg) ?? "default",
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
            VerifyChallengeCommand = _config.GetConfigStr("VerifyChallengeCommand", passkeyCfg) ?? "select * from passkey_verify_challenge($1,$2)",
            ValidateSignCount = _config.GetConfigBool("ValidateSignCount", passkeyCfg, true),
            // GROUP 3: Authentication Data Command
            AuthenticateDataCommand = _config.GetConfigStr("AuthenticateDataCommand", passkeyCfg) ?? "select * from passkey_authenticate_data($1)",
            // GROUP 4: Complete Commands
            CompleteAddExistingUserCommand = _config.GetConfigStr("CompleteAddExistingUserCommand", passkeyCfg) ?? "select * from passkey_complete_add_existing($1,$2,$3,$4,$5,$6,$7,$8)",
            CompleteRegistrationCommand = _config.GetConfigStr("CompleteRegistrationCommand", passkeyCfg) ?? "select * from passkey_complete_registration($1,$2,$3,$4,$5,$6,$7,$8)",
            CompleteAuthenticateCommand = _config.GetConfigStr("CompleteAuthenticateCommand", passkeyCfg) ?? "select * from passkey_complete_authenticate($1,$2,$3,$4)",
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

        ClientLogger?.LogDebug(
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
                ClientLogger?.LogDebug("CORS policy allows any origins.");
            }
            else
            {
                builder = builder.WithOrigins(allowedOrigins);
                ClientLogger?.LogDebug("CORS policy allows origins: {allowedOrigins}", allowedOrigins);
            }

            if (allowedMethods.Contains("*"))
            {
                builder = builder.AllowAnyMethod();
                ClientLogger?.LogDebug("CORS policy allows any methods.");
            }
            else
            {
                builder = builder.WithMethods(allowedMethods);
                ClientLogger?.LogDebug("CORS policy allows methods: {allowedMethods}", allowedMethods);
            }

            if (allowedHeaders.Contains("*"))
            {
                builder = builder.AllowAnyHeader();
                ClientLogger?.LogDebug("CORS policy allows any headers.");
            }
            else
            {
                builder = builder.WithHeaders(allowedHeaders);
                ClientLogger?.LogDebug("CORS policy allows headers: {allowedHeaders}", allowedHeaders);
            }

            if (_config.GetConfigBool("AllowCredentials", corsCfg, true) is true)
            {
                ClientLogger?.LogDebug("CORS policy allows credentials.");
                builder.AllowCredentials();
            }
            else
            {
                ClientLogger?.LogDebug("CORS policy does not allow credentials.");
            }
            
            var preflightMaxAge = _config.GetConfigInt("PreflightMaxAgeSeconds", corsCfg) ?? 600;
            if (preflightMaxAge > 0)
            {
                ClientLogger?.LogDebug("CORS policy preflight max age is set to {preflightMaxAge} seconds.", preflightMaxAge);
                builder.SetPreflightMaxAge(TimeSpan.FromSeconds(preflightMaxAge));
            }
            else
            {
                ClientLogger?.LogDebug("CORS policy preflight max age is not set.");
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
                "text/javascript",
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

            ClientLogger?.LogDebug("Using Antiforgery with cookie name {Cookie}, form field name {FormFieldName}, header name {HeaderName}",
                o.Cookie.Name,
                o.FormFieldName,
                o.HeaderName);
        });
        return true;
    }

    public SecurityHeadersConfig? BuildSecurityHeaders()
    {
        var cfg = _config.Cfg.GetSection("SecurityHeaders");
        if (_config.Exists(cfg) is false || _config.GetConfigBool("Enabled", cfg) is false)
        {
            return null;
        }

        var config = new SecurityHeadersConfig
        {
            XContentTypeOptions = _config.GetConfigStr("XContentTypeOptions", cfg),
            XFrameOptions = _config.GetConfigStr("XFrameOptions", cfg),
            ReferrerPolicy = _config.GetConfigStr("ReferrerPolicy", cfg),
            ContentSecurityPolicy = _config.GetConfigStr("ContentSecurityPolicy", cfg),
            PermissionsPolicy = _config.GetConfigStr("PermissionsPolicy", cfg),
            CrossOriginOpenerPolicy = _config.GetConfigStr("CrossOriginOpenerPolicy", cfg),
            CrossOriginEmbedderPolicy = _config.GetConfigStr("CrossOriginEmbedderPolicy", cfg),
            CrossOriginResourcePolicy = _config.GetConfigStr("CrossOriginResourcePolicy", cfg)
        };

        var enabledHeaders = new List<string>();
        if (config.XContentTypeOptions is not null) enabledHeaders.Add("X-Content-Type-Options");
        if (config.XFrameOptions is not null) enabledHeaders.Add("X-Frame-Options");
        if (config.ReferrerPolicy is not null) enabledHeaders.Add("Referrer-Policy");
        if (config.ContentSecurityPolicy is not null) enabledHeaders.Add("Content-Security-Policy");
        if (config.PermissionsPolicy is not null) enabledHeaders.Add("Permissions-Policy");
        if (config.CrossOriginOpenerPolicy is not null) enabledHeaders.Add("Cross-Origin-Opener-Policy");
        if (config.CrossOriginEmbedderPolicy is not null) enabledHeaders.Add("Cross-Origin-Embedder-Policy");
        if (config.CrossOriginResourcePolicy is not null) enabledHeaders.Add("Cross-Origin-Resource-Policy");
        ClientLogger?.LogDebug("Security headers middleware enabled: {Headers}", string.Join(", ", enabledHeaders));
        return config;
    }

    public bool BuildForwardedHeaders()
    {
        var cfg = _config.Cfg.GetSection("ForwardedHeaders");
        if (_config.Exists(cfg) is false || _config.GetConfigBool("Enabled", cfg) is false)
        {
            return false;
        }

        Instance.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

            var forwardLimit = _config.GetConfigInt("ForwardLimit", cfg);
            if (forwardLimit.HasValue)
            {
                options.ForwardLimit = forwardLimit.Value;
            }

            var knownProxies = _config.GetConfigEnumerable("KnownProxies", cfg);
            if (knownProxies is not null)
            {
                foreach (var proxy in knownProxies)
                {
                    if (System.Net.IPAddress.TryParse(proxy, out var ip))
                    {
                        options.KnownProxies.Add(ip);
                    }
                }
            }

            var knownNetworks = _config.GetConfigEnumerable("KnownNetworks", cfg);
            if (knownNetworks is not null)
            {
                foreach (var network in knownNetworks)
                {
                    if (System.Net.IPNetwork.TryParse(network, out var ipNetwork))
                    {
                        options.KnownIPNetworks.Add(ipNetwork);
                    }
                }
            }

            var allowedHosts = _config.GetConfigEnumerable("AllowedHosts", cfg)?.ToList();
            if (allowedHosts is not null && allowedHosts.Count > 0)
            {
                options.AllowedHosts = allowedHosts;
            }
        });

        var forwardLimit = _config.GetConfigInt("ForwardLimit", cfg) ?? 1;
        var knownProxiesCount = _config.GetConfigEnumerable("KnownProxies", cfg)?.Count() ?? 0;
        var knownNetworksCount = _config.GetConfigEnumerable("KnownNetworks", cfg)?.Count() ?? 0;
        ClientLogger?.LogDebug("Forwarded headers middleware enabled: ForwardLimit={ForwardLimit}, KnownProxies={KnownProxies}, KnownNetworks={KnownNetworks}",
            forwardLimit, knownProxiesCount, knownNetworksCount);
        return true;
    }

    public (bool enabled, TimeSpan? cacheDuration) BuildHealthChecks(string? connectionString)
    {
        var cfg = _config.Cfg.GetSection("HealthChecks");
        if (_config.Exists(cfg) is false || _config.GetConfigBool("Enabled", cfg) is false)
        {
            return (false, null);
        }

        var builder = Instance.Services.AddHealthChecks();

        if (_config.GetConfigBool("IncludeDatabaseCheck", cfg, true) && connectionString is not null)
        {
            var dbName = _config.GetConfigStr("DatabaseCheckName", cfg) ?? "postgresql";
            builder.AddNpgSql(connectionString, name: dbName, tags: ["ready"]);
        }

        var path = _config.GetConfigStr("Path", cfg) ?? "/health";
        var readyPath = _config.GetConfigStr("ReadyPath", cfg) ?? "/health/ready";
        var livePath = _config.GetConfigStr("LivePath", cfg) ?? "/health/live";
        var cacheDuration = Parser.ParsePostgresInterval(_config.GetConfigStr("CacheDuration", cfg));
        ClientLogger?.LogDebug("Health checks endpoints configured: {Path}, {ReadyPath}, {LivePath}", path, readyPath, livePath);
        return (true, cacheDuration);
    }

    public (bool enabled, TimeSpan? cacheDuration) BuildStats()
    {
        var cfg = _config.Cfg.GetSection("Stats");
        if (_config.Exists(cfg) is false || _config.GetConfigBool("Enabled", cfg) is false)
        {
            return (false, null);
        }

        var routinesPath = _config.GetConfigStr("RoutinesStatsPath", cfg) ?? "/stats/routines";
        var tablesPath = _config.GetConfigStr("TablesStatsPath", cfg) ?? "/stats/tables";
        var indexesPath = _config.GetConfigStr("IndexesStatsPath", cfg) ?? "/stats/indexes";
        var activityPath = _config.GetConfigStr("ActivityPath", cfg) ?? "/stats/activity";
        var cacheDuration = Parser.ParsePostgresInterval(_config.GetConfigStr("CacheDuration", cfg));
        ClientLogger?.LogDebug("Stats endpoints configured: {RoutinesPath}, {TablesPath}, {IndexesPath}, {ActivityPath}",
            routinesPath, tablesPath, indexesPath, activityPath);
        return (true, cacheDuration);
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
                ClientLogger?.LogDebug("Using main connection string: {ConnectionString}", connectionStringBuilder.ConnectionString);
            }
            else
            {
                ClientLogger?.LogDebug("Using {connectionName} as main connection string: {ConnectionString}", connectionName, connectionStringBuilder.ConnectionString);
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
                ClientLogger?.LogDebug("Using additional connection string: {0}", connectionStringBuilder.ConnectionString);
            }
            else
            {
                ClientLogger?.LogDebug("Using {connectionName} as additional connection string: {ConnectionString}", connectionName, connectionStringBuilder.ConnectionString);
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
                ClientLogger?.LogDebug("Using connection retry options with strategy: RetrySequenceSeconds={RetrySequenceSeconds}, ErrorCodes={ErrorCodes}",
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
                ClientLogger?.LogDebug("Using command retry options with default strategy '{DefaultStrategy}': RetrySequenceSeconds={RetrySequenceSeconds}, ErrorCodes={ErrorCodes}",
                    options.DefaultStrategy,
                    string.Join(",", defStrat.RetrySequenceSeconds),
                    string.Join(",", defStrat.ErrorCodes));
            }
            else
            {
                ClientLogger?.LogWarning("Default command retry strategy '{DefaultStrategy}' not found in defined strategies.", options.DefaultStrategy);
            }
        }
        return options;
    }
    
    public enum CacheType { Memory, Redis, Hybrid }

    /// <summary>
    /// Computes the union of cache types needed by the application: the root <c>Type</c> plus every enabled
    /// cache profile's <c>Type</c>. Used both for DI service registration (HybridCache) in
    /// <see cref="ConfigureCacheServices"/> and for lazy backend instantiation in <see cref="BuildCacheOptions"/>.
    /// </summary>
    private HashSet<CacheType> GetUsedCacheTypes(IConfigurationSection cacheCfg)
    {
        var types = new HashSet<CacheType>();
        var rootType = _config.GetConfigEnum<CacheType?>("Type", cacheCfg) ?? CacheType.Memory;
        types.Add(rootType);

        var profilesSection = cacheCfg.GetSection("Profiles");
        if (!profilesSection.Exists())
        {
            return types;
        }

        foreach (var profileSection in profilesSection.GetChildren())
        {
            // Skip disabled profiles — they're not registered, so their type isn't needed.
            if (_config.GetConfigBool("Enabled", profileSection, false) is false)
            {
                continue;
            }
            var profileType = _config.GetConfigEnum<CacheType?>("Type", profileSection);
            if (profileType.HasValue)
            {
                types.Add(profileType.Value);
            }
        }
        return types;
    }

    public CacheType ConfigureCacheServices()
    {
        var cacheCfg = _config.Cfg.GetSection("CacheOptions");
        if (cacheCfg is null || _config.GetConfigBool("Enabled", cacheCfg) is false)
        {
            return CacheType.Memory;
        }

        var rootType = _config.GetConfigEnum<CacheType?>("Type", cacheCfg) ?? CacheType.Memory;
        var usedTypes = GetUsedCacheTypes(cacheCfg);

        // Register HybridCache services if Hybrid is used by root or any enabled profile.
        // This is the only DI registration step — Memory and Redis backends instantiate later in BuildCacheOptions.
        if (usedTypes.Contains(CacheType.Hybrid))
        {
            var redisConfiguration = _config.GetConfigStr("RedisConfiguration", cacheCfg);
            var useRedisBackend = _config.GetConfigBool("HybridCacheUseRedisBackend", cacheCfg, false);

            if (useRedisBackend && !string.IsNullOrEmpty(redisConfiguration))
            {
                Instance.Services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = redisConfiguration;
                });
                ClientLogger?.LogDebug("HybridCache services configured with Redis L2 at: {RedisConfiguration}", redisConfiguration);
            }
            else
            {
                ClientLogger?.LogDebug("HybridCache services configured with in-memory only (no Redis L2)");
            }

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

        return rootType;
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
            ClientLogger?.LogDebug("Routine caching is disabled.");
            return options;
        }

        options.MaxCacheableRows = _config.GetConfigInt("MaxCacheableRows", cacheCfg);
        options.UseHashedCacheKeys = _config.GetConfigBool("UseHashedCacheKeys", cacheCfg);
        options.HashKeyThreshold = _config.GetConfigInt("HashKeyThreshold", cacheCfg) ?? 256;
        options.InvalidateCacheSuffix = _config.GetConfigStr("InvalidateCacheSuffix", cacheCfg);
        options.MemoryCachePruneIntervalSeconds = _config.GetConfigInt("MemoryCachePruneIntervalSeconds", cacheCfg) ?? 60;

        // Lazy backend instantiation: spin up at most one instance per CacheType, only for types
        // actually referenced by root config or any enabled profile. Profiles that share a Type
        // reuse the same backend instance (one Memory cache, one Redis connection, one HybridCache singleton).
        var usedTypes = GetUsedCacheTypes(cacheCfg);
        var backendsByType = new Dictionary<CacheType, IRoutineCache>();

        if (usedTypes.Contains(CacheType.Memory))
        {
            backendsByType[CacheType.Memory] = new RoutineCache();
            ClientLogger?.LogDebug(
                "Initialized Memory cache backend (MemoryCachePruneIntervalSeconds={Interval}, MaxCacheableRows={MaxRows}, UseHashedCacheKeys={Hash}, HashKeyThreshold={Threshold}).",
                options.MemoryCachePruneIntervalSeconds, options.MaxCacheableRows, options.UseHashedCacheKeys, options.HashKeyThreshold);
        }

        if (usedTypes.Contains(CacheType.Redis))
        {
            var configuration = _config.GetConfigStr("RedisConfiguration", cacheCfg) ??
                                "localhost:6379,abortConnect=false,ssl=false,connectTimeout=10000,syncTimeout=5000,connectRetry=3";
            try
            {
                var redisCache = new RedisCache(configuration, Logger, options);
                backendsByType[CacheType.Redis] = redisCache;
                app.Lifetime.ApplicationStopping.Register(() => redisCache.Dispose());
                ClientLogger?.LogDebug("Initialized Redis cache backend (RedisConfiguration={Configuration}).", configuration);
            }
            catch (Exception ex)
            {
                ClientLogger?.LogError(ex, "Failed to initialize Redis cache backend (RedisConfiguration={Configuration}). Falling back to in-memory.", configuration);
                if (!backendsByType.ContainsKey(CacheType.Memory))
                {
                    backendsByType[CacheType.Memory] = new RoutineCache();
                }
                backendsByType[CacheType.Redis] = backendsByType[CacheType.Memory];
            }
        }

        if (usedTypes.Contains(CacheType.Hybrid))
        {
            try
            {
                var hybridCache = app.Services.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>();
                backendsByType[CacheType.Hybrid] = new HybridCacheWrapper(hybridCache, Logger, options);
                var useRedisBackend = _config.GetConfigBool("HybridCacheUseRedisBackend", cacheCfg, false);
                var redisConfiguration = _config.GetConfigStr("RedisConfiguration", cacheCfg);
                if (useRedisBackend && !string.IsNullOrEmpty(redisConfiguration))
                {
                    ClientLogger?.LogDebug("Initialized Hybrid cache backend (L1: in-memory, L2: Redis at {RedisConfiguration}).", redisConfiguration);
                }
                else
                {
                    ClientLogger?.LogDebug("Initialized Hybrid cache backend (in-memory only, with stampede protection).");
                }
            }
            catch (Exception ex)
            {
                ClientLogger?.LogError(ex, "Failed to initialize Hybrid cache backend. Falling back to in-memory.");
                if (!backendsByType.ContainsKey(CacheType.Memory))
                {
                    backendsByType[CacheType.Memory] = new RoutineCache();
                }
                backendsByType[CacheType.Hybrid] = backendsByType[CacheType.Memory];
            }
        }

        // Wire root cache (the implicit default for endpoints without @cache_profile).
        if (backendsByType.TryGetValue(configuredCacheType, out var rootBackend))
        {
            options.DefaultRoutineCache = rootBackend;
        }
        else
        {
            // Should not happen — usedTypes always contains rootType — but defensively fall back.
            options.DefaultRoutineCache = new RoutineCache();
        }

        // Build named profiles, sharing the per-type backend instances above.
        options.Profiles = BuildCacheProfiles(cacheCfg, backendsByType);

        return options;
    }

    /// <summary>
    /// Parses the <c>CacheOptions:Profiles</c> JSON section into a dictionary of <see cref="CacheProfile"/> POCOs.
    /// Each profile is gated by its own <c>"Enabled"</c> flag (default false). Profiles with missing or invalid
    /// <c>Type</c>, invalid <c>Expiration</c>, or empty/whitespace name are skipped with a Warning. Backends are
    /// looked up from <paramref name="backendsByType"/>; profiles sharing a Type share the backend instance.
    /// </summary>
    private Dictionary<string, CacheProfile>? BuildCacheProfiles(
        IConfigurationSection cacheCfg,
        Dictionary<CacheType, IRoutineCache> backendsByType)
    {
        var profilesSection = cacheCfg.GetSection("Profiles");
        if (!profilesSection.Exists())
        {
            return null;
        }
        var children = profilesSection.GetChildren().ToArray();
        if (children.Length == 0)
        {
            return null;
        }

        var result = new Dictionary<string, CacheProfile>(StringComparer.Ordinal);
        var registeredCount = 0;

        foreach (var profileSection in children)
        {
            var name = profileSection.Key;
            if (string.IsNullOrWhiteSpace(name))
            {
                ClientLogger?.LogWarning("CacheOptions:Profiles contains a profile with an empty or whitespace name. Skipping.");
                continue;
            }

            if (_config.GetConfigBool("Enabled", profileSection, false) is false)
            {
                ClientLogger?.LogInformation(
                    "Skipping cache profile '{Profile}': 'Enabled' is false. Set \"Enabled\": true to activate.",
                    name);
                continue;
            }

            var typeStr = _config.GetConfigStr("Type", profileSection);
            if (string.IsNullOrWhiteSpace(typeStr))
            {
                ClientLogger?.LogWarning(
                    "Cache profile '{Profile}' is enabled but has no 'Type'. Skipping. Valid: Memory, Redis, Hybrid.",
                    name);
                continue;
            }
            if (!Enum.TryParse<CacheType>(typeStr, ignoreCase: true, out var profileType))
            {
                ClientLogger?.LogWarning(
                    "Cache profile '{Profile}' has invalid Type '{Type}'. Skipping. Valid: Memory, Redis, Hybrid.",
                    name, typeStr);
                continue;
            }

            TimeSpan? expiration = null;
            var expirationStr = _config.GetConfigStr("Expiration", profileSection);
            if (!string.IsNullOrWhiteSpace(expirationStr))
            {
                expiration = Parser.ParsePostgresInterval(expirationStr);
                if (expiration is null)
                {
                    ClientLogger?.LogWarning(
                        "Cache profile '{Profile}' has invalid Expiration '{Expiration}'. Skipping. Use PostgreSQL interval syntax (e.g. '5 minutes', '1 hour', '30 seconds').",
                        name, expirationStr);
                    continue;
                }
            }

            var parameters = ReadProfileParameters(profileSection);
            var when = ReadProfileWhenRules(name, profileSection);

            if (!backendsByType.TryGetValue(profileType, out var cacheInstance))
            {
                ClientLogger?.LogWarning(
                    "Cache profile '{Profile}' references Type '{Type}' but no backend was instantiated for that type. Skipping.",
                    name, profileType);
                continue;
            }

            result[name] = new CacheProfile
            {
                Cache = cacheInstance,
                Expiration = expiration,
                Parameters = parameters,
                When = when
            };
            registeredCount++;

            ClientLogger?.LogInformation(
                "Registered cache profile '{Profile}' (Type={Type}, Expiration={Expiration}, Parameters={Parameters}, WhenRules={WhenRuleCount}).",
                name, profileType,
                expiration?.ToString() ?? "(none)",
                parameters is null ? "(all)" : (parameters.Length == 0 ? "(URL-only)" : string.Join(",", parameters)),
                when?.Length ?? 0);
        }

        if (registeredCount > 0)
        {
            ClientLogger?.LogInformation("Total {Count} cache profile(s) registered.", registeredCount);
        }
        return result.Count == 0 ? null : result;
    }

    /// <summary>
    /// Reads <c>Profile.Parameters</c> from config. Returns <c>null</c> if the section is missing
    /// (= use all routine params), an empty array if the section is present but empty (= URL-only cache),
    /// or the explicit list of parameter names otherwise.
    /// </summary>
    private static string[]? ReadProfileParameters(IConfigurationSection profileSection)
    {
        var section = profileSection.GetSection("Parameters");
        if (!section.Exists())
        {
            return null;
        }
        var children = section.GetChildren().ToArray();
        if (children.Length == 0)
        {
            return [];
        }
        var list = new List<string>(children.Length);
        foreach (var child in children)
        {
            if (!string.IsNullOrWhiteSpace(child.Value))
            {
                list.Add(child.Value);
            }
        }
        return [.. list];
    }

    /// <summary>
    /// Reads <c>Profile.When</c> from config into an array of <see cref="CacheWhenRule"/>.
    ///
    /// Each entry is a JSON object with fields:
    ///   - <c>Parameter</c>: required, the routine parameter name to match.
    ///   - <c>Value</c>: required for matching; scalar (single match) or array (OR over entries). JSON null arrives
    ///     as a null <see cref="JsonNode"/>. All non-null values arrive as JSON strings (IConfiguration is type-erasing);
    ///     the runtime matcher compares string-equal case-insensitive.
    ///   - <c>Then</c>: required action — either the literal string <c>"skip"</c> (bypass cache when matched), or a
    ///     PostgreSQL interval string (override the entry's TTL, e.g. <c>"5 seconds"</c>).
    ///
    /// Rules with missing/invalid fields are skipped with a Warning.
    /// </summary>
    private CacheWhenRule[]? ReadProfileWhenRules(string profileName, IConfigurationSection profileSection)
    {
        var section = profileSection.GetSection("When");
        if (!section.Exists())
        {
            return null;
        }
        var children = section.GetChildren().ToArray();
        if (children.Length == 0)
        {
            return null;
        }
        var result = new List<CacheWhenRule>(children.Length);
        for (var i = 0; i < children.Length; i++)
        {
            var entry = children[i];

            var parameter = _config.GetConfigStr("Parameter", entry);
            if (string.IsNullOrWhiteSpace(parameter))
            {
                ClientLogger?.LogWarning(
                    "Cache profile '{Profile}' When[{Index}] is missing 'Parameter'. Skipping rule.",
                    profileName, i);
                continue;
            }

            var thenStr = _config.GetConfigStr("Then", entry);
            if (string.IsNullOrWhiteSpace(thenStr))
            {
                ClientLogger?.LogWarning(
                    "Cache profile '{Profile}' When[{Index}] (Parameter='{Parameter}') is missing 'Then'. " +
                    "Skipping rule. Use \"Then\": \"skip\" to bypass the cache, or a PostgreSQL interval like \"30 seconds\" to override the TTL.",
                    profileName, i, parameter);
                continue;
            }

            bool skip;
            TimeSpan? thenExpiration = null;
            if (string.Equals(thenStr, "skip", StringComparison.OrdinalIgnoreCase))
            {
                skip = true;
            }
            else
            {
                var parsed = Parser.ParsePostgresInterval(thenStr);
                if (parsed is null)
                {
                    ClientLogger?.LogWarning(
                        "Cache profile '{Profile}' When[{Index}] (Parameter='{Parameter}') has invalid 'Then' value '{Then}'. " +
                        "Skipping rule. Use \"skip\" or a PostgreSQL interval (e.g. \"30 seconds\", \"5 minutes\").",
                        profileName, i, parameter, thenStr);
                    continue;
                }
                skip = false;
                thenExpiration = parsed;
            }

            // Read Value section. If missing, treat as null match condition (matches null/DBNull params).
            var valueSection = entry.GetSection("Value");
            JsonNode? value = valueSection.Exists() ? ConfigSectionToJsonNode(valueSection) : null;

            result.Add(new CacheWhenRule
            {
                Parameter = parameter!,
                Value = value,
                Skip = skip,
                ThenExpiration = thenExpiration
            });
        }
        return result.Count == 0 ? null : [.. result];
    }

    /// <summary>
    /// Converts an <see cref="IConfigurationSection"/> leaf (or array) into a <see cref="JsonNode"/>.
    /// Leaf with null Value → null. Leaf with non-null Value → JsonValue (string). Section with children
    /// → JsonArray of children's leaf values.
    /// </summary>
    private static JsonNode? ConfigSectionToJsonNode(IConfigurationSection section)
    {
        var children = section.GetChildren().ToArray();
        if (children.Length == 0)
        {
            return section.Value is null ? null : JsonValue.Create(section.Value);
        }
        var arr = new JsonArray();
        foreach (var child in children)
        {
            arr.Add((JsonNode?)(child.Value is null ? null : JsonValue.Create(child.Value)));
        }
        return arr;
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
                // Detect legacy array form (children keyed by index "0", "1", ...) and fail loudly.
                // 3.13.0 migrated RateLimiterOptions:Policies to an object keyed by policy name (consistent
                // with ValidationOptions:Rules and CacheOptions:Profiles). Continuing silently with a numeric
                // key would register policies under names like "0" and "1", which is impossible for endpoints
                // to reference — turning rate limiting into a silent no-op.
                if (int.TryParse(sectionCfg.Key, out _))
                {
                    throw new InvalidOperationException(
                        "RateLimiterOptions:Policies has been changed from an array to an object keyed by policy name in 3.13.0. " +
                        "Migrate by moving each policy's `Name` value to be the JSON key and dropping the `Name` field. " +
                        "See changelog/v3.13.0.md for details.");
                }

                var type = _config.GetConfigEnum<RateLimiterType?>("Type", sectionCfg);
                if (type is null)
                {
                    continue;
                }
                if (_config.GetConfigBool("Enabled", sectionCfg) is false)
                {
                    continue;
                }
                // Policy name comes from the dict key (e.g. "fixed", "sliding").
                var name = sectionCfg.Key;

                // Read optional partition configuration. When present, every request resolves a partition key
                // and gets its own bucket. When null, all requests share a single global bucket (legacy behavior).
                var partition = ReadPartitionConfig(sectionCfg);

                if (type == RateLimiterType.FixedWindow)
                {
                    var permitLimit = _config.GetConfigInt("PermitLimit", sectionCfg) ?? 100;
                    var window = TimeSpan.FromSeconds(_config.GetConfigInt("WindowSeconds", sectionCfg) ?? 60);
                    var queueLimit = _config.GetConfigInt("QueueLimit", sectionCfg) ?? 10;
                    var autoReplenish = _config.GetConfigBool("AutoReplenishment", sectionCfg, true);
                    if (partition is null)
                    {
                        options.AddFixedWindowLimiter(name, config =>
                        {
                            config.PermitLimit = permitLimit;
                            config.Window = window;
                            config.QueueLimit = queueLimit;
                            config.AutoReplenishment = autoReplenish;
                        });
                    }
                    else
                    {
                        options.AddPolicy(name, httpContext =>
                            partition.BypassAuthenticated && httpContext.User?.Identity?.IsAuthenticated == true
                                ? RateLimitPartition.GetNoLimiter("__authenticated__")
                                : RateLimitPartition.GetFixedWindowLimiter(
                                    ResolvePartitionKey(httpContext, partition),
                                    _ => new FixedWindowRateLimiterOptions
                                    {
                                        PermitLimit = permitLimit,
                                        Window = window,
                                        QueueLimit = queueLimit,
                                        AutoReplenishment = autoReplenish
                                    }));
                    }
                    ClientLogger?.LogDebug("Using Fixed Window rate limiter with name {Name}: PermitLimit={PermitLimit}, WindowSeconds={WindowSeconds}, QueueLimit={QueueLimit}, AutoReplenishment={AutoReplenishment}, Partition={Partition}",
                        name, permitLimit, _config.GetConfigInt("WindowSeconds", sectionCfg) ?? 60, queueLimit, autoReplenish,
                        FormatPartitionForLog(partition));
                }
                else if (type == RateLimiterType.SlidingWindow)
                {
                    var permitLimit = _config.GetConfigInt("PermitLimit", sectionCfg) ?? 100;
                    var window = TimeSpan.FromSeconds(_config.GetConfigInt("WindowSeconds", sectionCfg) ?? 60);
                    var segments = _config.GetConfigInt("SegmentsPerWindow", sectionCfg) ?? 6;
                    var queueLimit = _config.GetConfigInt("QueueLimit", sectionCfg) ?? 10;
                    var autoReplenish = _config.GetConfigBool("AutoReplenishment", sectionCfg, true);
                    if (partition is null)
                    {
                        options.AddSlidingWindowLimiter(name, config =>
                        {
                            config.PermitLimit = permitLimit;
                            config.Window = window;
                            config.SegmentsPerWindow = segments;
                            config.QueueLimit = queueLimit;
                            config.AutoReplenishment = autoReplenish;
                        });
                    }
                    else
                    {
                        options.AddPolicy(name, httpContext =>
                            partition.BypassAuthenticated && httpContext.User?.Identity?.IsAuthenticated == true
                                ? RateLimitPartition.GetNoLimiter("__authenticated__")
                                : RateLimitPartition.GetSlidingWindowLimiter(
                                    ResolvePartitionKey(httpContext, partition),
                                    _ => new SlidingWindowRateLimiterOptions
                                    {
                                        PermitLimit = permitLimit,
                                        Window = window,
                                        SegmentsPerWindow = segments,
                                        QueueLimit = queueLimit,
                                        AutoReplenishment = autoReplenish
                                    }));
                    }
                    ClientLogger?.LogDebug("Using Sliding Window rate limiter with name {Name}: PermitLimit={PermitLimit}, WindowSeconds={WindowSeconds}, SegmentsPerWindow={SegmentsPerWindow}, QueueLimit={QueueLimit}, AutoReplenishment={AutoReplenishment}, Partition={Partition}",
                        name, permitLimit, _config.GetConfigInt("WindowSeconds", sectionCfg) ?? 60, segments, queueLimit, autoReplenish,
                        FormatPartitionForLog(partition));
                }
                else if (type == RateLimiterType.TokenBucket)
                {
                    var tokenLimit = _config.GetConfigInt("TokenLimit", sectionCfg) ?? 100;
                    var tokensPerPeriod = _config.GetConfigInt("TokensPerPeriod", sectionCfg) ?? 10;
                    var replenishSeconds = _config.GetConfigInt("ReplenishmentPeriodSeconds", sectionCfg) ?? 10;
                    var replenishPeriod = TimeSpan.FromSeconds(replenishSeconds);
                    var queueLimit = _config.GetConfigInt("QueueLimit", sectionCfg) ?? 10;
                    var autoReplenish = _config.GetConfigBool("AutoReplenishment", sectionCfg, true);
                    if (partition is null)
                    {
                        options.AddTokenBucketLimiter(name, config =>
                        {
                            config.TokenLimit = tokenLimit;
                            config.TokensPerPeriod = tokensPerPeriod;
                            config.ReplenishmentPeriod = replenishPeriod;
                            config.QueueLimit = queueLimit;
                            config.AutoReplenishment = autoReplenish;
                        });
                    }
                    else
                    {
                        options.AddPolicy(name, httpContext =>
                            partition.BypassAuthenticated && httpContext.User?.Identity?.IsAuthenticated == true
                                ? RateLimitPartition.GetNoLimiter("__authenticated__")
                                : RateLimitPartition.GetTokenBucketLimiter(
                                    ResolvePartitionKey(httpContext, partition),
                                    _ => new TokenBucketRateLimiterOptions
                                    {
                                        TokenLimit = tokenLimit,
                                        TokensPerPeriod = tokensPerPeriod,
                                        ReplenishmentPeriod = replenishPeriod,
                                        QueueLimit = queueLimit,
                                        AutoReplenishment = autoReplenish
                                    }));
                    }
                    ClientLogger?.LogDebug("Using Token Bucket rate limiter with name {Name}: TokenLimit={TokenLimit}, TokensPerPeriod={TokensPerPeriod}, ReplenishmentPeriodSeconds={ReplenishmentPeriodSeconds}, QueueLimit={QueueLimit}, AutoReplenishment={AutoReplenishment}, Partition={Partition}",
                        name, tokenLimit, tokensPerPeriod, replenishSeconds, queueLimit, autoReplenish,
                        FormatPartitionForLog(partition));
                }
                else if (type == RateLimiterType.Concurrency)
                {
                    var permitLimit = _config.GetConfigInt("PermitLimit", sectionCfg) ?? 10;
                    var queueLimit = _config.GetConfigInt("QueueLimit", sectionCfg) ?? 5;
                    var oldestFirst = _config.GetConfigBool("OldestFirst", sectionCfg, true);
                    var queueOrder = oldestFirst ? QueueProcessingOrder.OldestFirst : QueueProcessingOrder.NewestFirst;
                    if (partition is null)
                    {
                        options.AddConcurrencyLimiter(name, config =>
                        {
                            config.PermitLimit = permitLimit;
                            config.QueueLimit = queueLimit;
                            config.QueueProcessingOrder = queueOrder;
                        });
                    }
                    else
                    {
                        options.AddPolicy(name, httpContext =>
                            partition.BypassAuthenticated && httpContext.User?.Identity?.IsAuthenticated == true
                                ? RateLimitPartition.GetNoLimiter("__authenticated__")
                                : RateLimitPartition.GetConcurrencyLimiter(
                                    ResolvePartitionKey(httpContext, partition),
                                    _ => new ConcurrencyLimiterOptions
                                    {
                                        PermitLimit = permitLimit,
                                        QueueLimit = queueLimit,
                                        QueueProcessingOrder = queueOrder
                                    }));
                    }
                    ClientLogger?.LogDebug("Using Concurrency rate limiter with name {Name}: PermitLimit={PermitLimit}, QueueLimit={QueueLimit}, OldestFirst={OldestFirst}, Partition={Partition}",
                        name, permitLimit, queueLimit, oldestFirst,
                        FormatPartitionForLog(partition));
                }
            }
        });

        return (defaultPolicy, true);
    }

    /// <summary>
    /// Parses an optional <c>Partition</c> sub-section under a rate-limiter policy. Returns null if the
    /// section is absent or contains no usable sources/flags. Logs Warning for individual invalid sources.
    /// </summary>
    private RateLimitPartitionConfig? ReadPartitionConfig(IConfigurationSection policyCfg)
    {
        var sec = policyCfg.GetSection("Partition");
        if (!sec.Exists())
        {
            return null;
        }

        var bypassAuthenticated = _config.GetConfigBool("BypassAuthenticated", sec, false);
        var sources = new List<RateLimitPartitionSource>();
        var sourcesSec = sec.GetSection("Sources");
        if (sourcesSec.Exists())
        {
            var idx = 0;
            foreach (var srcSec in sourcesSec.GetChildren())
            {
                var typeStr = _config.GetConfigStr("Type", srcSec);
                if (string.IsNullOrWhiteSpace(typeStr))
                {
                    ClientLogger?.LogWarning(
                        "RateLimiterOptions:Policies:{Policy}:Partition:Sources[{Index}] is missing 'Type'. Skipping. Valid: Claim, IpAddress, Header, Static.",
                        policyCfg.Key, idx);
                    idx++;
                    continue;
                }
                if (!Enum.TryParse<RateLimitPartitionSourceType>(typeStr, ignoreCase: true, out var srcType))
                {
                    ClientLogger?.LogWarning(
                        "RateLimiterOptions:Policies:{Policy}:Partition:Sources[{Index}] has invalid Type '{Type}'. Skipping. Valid: Claim, IpAddress, Header, Static.",
                        policyCfg.Key, idx, typeStr);
                    idx++;
                    continue;
                }
                var src = new RateLimitPartitionSource
                {
                    Type = srcType,
                    Name = _config.GetConfigStr("Name", srcSec),
                    Value = _config.GetConfigStr("Value", srcSec)
                };
                if (srcType == RateLimitPartitionSourceType.Claim && string.IsNullOrWhiteSpace(src.Name))
                {
                    ClientLogger?.LogWarning(
                        "RateLimiterOptions:Policies:{Policy}:Partition:Sources[{Index}] (Type=Claim) requires 'Name' (claim type). Skipping.",
                        policyCfg.Key, idx);
                    idx++;
                    continue;
                }
                if (srcType == RateLimitPartitionSourceType.Header && string.IsNullOrWhiteSpace(src.Name))
                {
                    ClientLogger?.LogWarning(
                        "RateLimiterOptions:Policies:{Policy}:Partition:Sources[{Index}] (Type=Header) requires 'Name' (header name). Skipping.",
                        policyCfg.Key, idx);
                    idx++;
                    continue;
                }
                if (srcType == RateLimitPartitionSourceType.Static && string.IsNullOrWhiteSpace(src.Value))
                {
                    ClientLogger?.LogWarning(
                        "RateLimiterOptions:Policies:{Policy}:Partition:Sources[{Index}] (Type=Static) requires 'Value' (the literal partition key). Skipping.",
                        policyCfg.Key, idx);
                    idx++;
                    continue;
                }
                sources.Add(src);
                idx++;
            }
        }

        if (sources.Count == 0 && !bypassAuthenticated)
        {
            ClientLogger?.LogWarning(
                "RateLimiterOptions:Policies:{Policy}:Partition has no valid Sources and BypassAuthenticated is false. Partition is non-functional and will be ignored (policy reverts to a single global bucket).",
                policyCfg.Key);
            return null;
        }

        return new RateLimitPartitionConfig
        {
            Sources = [.. sources],
            BypassAuthenticated = bypassAuthenticated
        };
    }

    /// <summary>
    /// Walks <see cref="RateLimitPartitionConfig.Sources"/> top-to-bottom; first source returning a non-empty
    /// key wins. Falls back to <c>"unpartitioned"</c> if no source matches (so the policy still has a coherent
    /// bucket). Caller has already short-circuited on <c>BypassAuthenticated</c>.
    /// </summary>
    public static string ResolvePartitionKey(Microsoft.AspNetCore.Http.HttpContext ctx, RateLimitPartitionConfig partition)
    {
        foreach (var src in partition.Sources)
        {
            string? key = src.Type switch
            {
                RateLimitPartitionSourceType.Claim =>
                    src.Name is null ? null : ctx.User?.FindFirst(src.Name)?.Value,
                RateLimitPartitionSourceType.IpAddress =>
                    ctx.Request.GetClientIpAddress(),
                RateLimitPartitionSourceType.Header =>
                    src.Name is not null && ctx.Request.Headers.TryGetValue(src.Name, out var v) ? v.ToString() : null,
                RateLimitPartitionSourceType.Static => src.Value,
                _ => null
            };
            if (!string.IsNullOrEmpty(key))
            {
                return key;
            }
        }
        return "unpartitioned";
    }

    private static string FormatPartitionForLog(RateLimitPartitionConfig? partition)
    {
        if (partition is null)
        {
            return "(none — single global bucket)";
        }
        var parts = new List<string>();
        if (partition.BypassAuthenticated)
        {
            parts.Add("BypassAuthenticated=true");
        }
        if (partition.Sources.Length > 0)
        {
            parts.Add("Sources=[" + string.Join(",", partition.Sources.Select(s =>
                s.Type switch
                {
                    RateLimitPartitionSourceType.Claim => $"Claim:{s.Name}",
                    RateLimitPartitionSourceType.Header => $"Header:{s.Name}",
                    RateLimitPartitionSourceType.IpAddress => "IpAddress",
                    RateLimitPartitionSourceType.Static => $"Static:{s.Value}",
                    _ => s.Type.ToString()
                })) + "]");
        }
        return string.Join(", ", parts);
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
            ClientLogger?.LogDebug("HTTP client options enabled: ResponseBodyField={ResponseBodyField}, ResponseStatusCodeField={ResponseStatusCodeField}, ResponseHeadersField={ResponseHeadersField}, ResponseContentTypeField={ResponseContentTypeField}, ResponseSuccessField={ResponseSuccessField}, ResponseErrorMessageField={ResponseErrorMessageField}",
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
            ClientLogger?.LogDebug("Proxy options enabled: Host={Host}, DefaultTimeout={DefaultTimeout}, ForwardHeaders={ForwardHeaders}, ForwardResponseHeaders={ForwardResponseHeaders}",
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
                ClientLogger?.LogDebug("Using target session attribute override '{TargetSession}' for connection '{ConnectionName}'", result, connectionName);
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
            ClientLogger?.LogDebug("Built multi-host data source for main connection with target session '{TargetSession}'", target);
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
                ClientLogger?.LogDebug("Built multi-host data source for connection '{ConnectionName}' with target session '{TargetSession}'", section.Key, target);
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
            ClientLogger?.LogDebug("Using multi-host data source with target session '{TargetSession}'", target);
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
            ClientLogger?.LogDebug("Validation options disabled or not configured. Using defaults.");
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
                ClientLogger?.LogWarning("Validation rule '{RuleName}' has no valid Type specified, skipping.", ruleName);
                continue;
            }

            var pattern = _config.GetConfigStr("Pattern", ruleSection);
            var minLength = _config.GetConfigInt("MinLength", ruleSection);
            var maxLength = _config.GetConfigInt("MaxLength", ruleSection);

            // Skip rules with missing required properties for specific types
            if (type == ValidationType.Regex && string.IsNullOrEmpty(pattern))
            {
                ClientLogger?.LogWarning("Validation rule '{RuleName}' has Type 'Regex' but no Pattern specified, skipping.", ruleName);
                continue;
            }
            if (type == ValidationType.MinLength && minLength is null)
            {
                ClientLogger?.LogWarning("Validation rule '{RuleName}' has Type 'MinLength' but no MinLength specified, skipping.", ruleName);
                continue;
            }
            if (type == ValidationType.MaxLength && maxLength is null)
            {
                ClientLogger?.LogWarning("Validation rule '{RuleName}' has Type 'MaxLength' but no MaxLength specified, skipping.", ruleName);
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
            ClientLogger?.LogDebug("Registered validation rule '{RuleName}': {Rule}", ruleName, rule);
        }

        if (result.Rules.Count == 0)
        {
            ClientLogger?.LogDebug("No validation rules configured, using defaults.");
            return defaults;
        }

        ClientLogger?.LogDebug("Using {Count} validation rules: {Rules}",
            result.Rules.Count,
            string.Join(", ", result.Rules.Keys));

        return result;
    }

    public BeforeRoutineCommand[] BuildBeforeRoutineCommands()
    {
        var section = _config.NpgsqlRestCfg.GetSection("BeforeRoutineCommands");
        if (section.Exists() is false)
        {
            return [];
        }

        var children = section.GetChildren().ToArray();
        if (children.Length == 0)
        {
            return [];
        }

        var result = new List<BeforeRoutineCommand>(children.Length);
        for (var i = 0; i < children.Length; i++)
        {
            var child = children[i];

            // Shorthand: array entry is a plain string → raw SQL, no parameters. Always enabled (the user wrote it inline).
            if (child.Value is not null)
            {
                if (string.IsNullOrWhiteSpace(child.Value))
                {
                    ClientLogger?.LogWarning(
                        "NpgsqlRest:BeforeRoutineCommands[{Index}] is an empty string. Skipping. " +
                        "Provide either a non-empty SQL string or an object with a non-empty 'Sql' property.",
                        i);
                    continue;
                }
                result.Add(new BeforeRoutineCommand { Sql = child.Value, Parameters = [] });
                ClientLogger?.LogInformation(
                    "Added BeforeRoutineCommand[{Index}] (shorthand): {Sql}",
                    i, child.Value.Trim());
                continue;
            }

            // Full form: object with Enabled + Sql + Parameters.
            // Enabled gates whether the command is registered at runtime. Default false (must explicitly opt in).
            var enabled = _config.GetConfigBool("Enabled", child, false);
            if (enabled is false)
            {
                ClientLogger?.LogInformation(
                    "Skipping NpgsqlRest:BeforeRoutineCommands[{Index}]: 'Enabled' is false. " +
                    "Set \"Enabled\": true to activate this command.",
                    i);
                continue;
            }

            var sql = _config.GetConfigStr("Sql", child);
            if (string.IsNullOrWhiteSpace(sql))
            {
                ClientLogger?.LogWarning(
                    "NpgsqlRest:BeforeRoutineCommands[{Index}] is enabled but has no 'Sql' property (or it is empty). Skipping. " +
                    "Provide a non-empty 'Sql' string, e.g. \"select set_config('app.x', $1, true)\".",
                    i);
                continue;
            }

            var paramsSection = child.GetSection("Parameters");
            var paramsList = new List<BeforeRoutineCommandParameter>();
            var paramsValid = true;
            if (paramsSection.Exists())
            {
                var paramChildren = paramsSection.GetChildren().ToArray();
                for (var pi = 0; pi < paramChildren.Length; pi++)
                {
                    var paramChild = paramChildren[pi];
                    var sourceStr = _config.GetConfigStr("Source", paramChild);
                    if (string.IsNullOrWhiteSpace(sourceStr))
                    {
                        ClientLogger?.LogWarning(
                            "NpgsqlRest:BeforeRoutineCommands[{Index}].Parameters[{ParamIndex}] is missing 'Source'. " +
                            "Skipping the entire command. " +
                            "Set Source to one of: Claim, RequestHeader, IpAddress.",
                            i, pi);
                        paramsValid = false;
                        break;
                    }
                    if (Enum.TryParse<BeforeRoutineCommandParameterSource>(sourceStr, ignoreCase: true, out var source) is false)
                    {
                        ClientLogger?.LogWarning(
                            "NpgsqlRest:BeforeRoutineCommands[{Index}].Parameters[{ParamIndex}] has invalid Source '{Source}'. " +
                            "Skipping the entire command. " +
                            "Valid sources are: Claim, RequestHeader, IpAddress.",
                            i, pi, sourceStr);
                        paramsValid = false;
                        break;
                    }
                    var name = _config.GetConfigStr("Name", paramChild);
                    if (source != BeforeRoutineCommandParameterSource.IpAddress && string.IsNullOrWhiteSpace(name))
                    {
                        ClientLogger?.LogWarning(
                            "NpgsqlRest:BeforeRoutineCommands[{Index}].Parameters[{ParamIndex}] has Source '{Source}' but no 'Name'. " +
                            "Skipping the entire command. " +
                            "For Source={Source}, set 'Name' to the {Hint}.",
                            i, pi, source, source,
                            source == BeforeRoutineCommandParameterSource.Claim ? "claim type" : "request header name");
                        paramsValid = false;
                        break;
                    }
                    paramsList.Add(new BeforeRoutineCommandParameter { Source = source, Name = name });
                }
            }

            if (paramsValid is false)
            {
                continue;
            }

            result.Add(new BeforeRoutineCommand { Sql = sql!, Parameters = [.. paramsList] });
            if (paramsList.Count == 0)
            {
                ClientLogger?.LogInformation(
                    "Added BeforeRoutineCommand[{Index}]: {Sql}",
                    i, sql!.Trim());
            }
            else
            {
                ClientLogger?.LogInformation(
                    "Added BeforeRoutineCommand[{Index}]: {Sql} | Parameters: [{Parameters}]",
                    i,
                    sql!.Trim(),
                    string.Join(", ", paramsList.Select(p => p.Source == BeforeRoutineCommandParameterSource.IpAddress
                        ? "IpAddress"
                        : $"{p.Source}:{p.Name}")));
            }
        }

        if (result.Count > 0)
        {
            ClientLogger?.LogInformation(
                "Total {Count} BeforeRoutineCommand(s) registered. They will run after context is set, before each routine call.",
                result.Count);
        }

        return [.. result];
    }
}

public class SecurityHeadersConfig
{
    public string? XContentTypeOptions { get; set; }
    public string? XFrameOptions { get; set; }
    public string? ReferrerPolicy { get; set; }
    public string? ContentSecurityPolicy { get; set; }
    public string? PermissionsPolicy { get; set; }
    public string? CrossOriginOpenerPolicy { get; set; }
    public string? CrossOriginEmbedderPolicy { get; set; }
    public string? CrossOriginResourcePolicy { get; set; }
}