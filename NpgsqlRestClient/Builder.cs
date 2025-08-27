using System.Collections.Frozen;
using System.IO.Compression;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Npgsql;
using NpgsqlRest;
using Serilog;
using Serilog.Extensions.Logging;


namespace NpgsqlRestClient;

public class Builder
{
    private readonly Config _config;
    
    public Builder(Config config)
    {
        _config = config;
    }
    
    public WebApplicationBuilder Instance { get; private set; }  = default!;

    public bool LogToConsole { get; private set; } = false;
    public bool LogToFile { get; private set; } = false;
    public bool LogToPostgres { get; private set; } = false;
    public Microsoft.Extensions.Logging.ILogger? Logger { get; private set; } = null;
    public bool UseHttpsRedirection { get; private set; } = false;
    public bool UseHsts { get; private set; } = false;
    public BearerTokenConfig? BearerTokenConfig { get; private set; } = null;
    public string? ConnectionString { get; private set; } = null;
    public string? ConnectionName { get; private set; } = null;
    public ExternalAuthConfig? ExternalAuthConfig { get; private set; } = null;

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

    public void BuildLogger()
    {
        var logCfg = _config.Cfg.GetSection("Log");
        Logger = null;
        LogToConsole = _config.GetConfigBool("ToConsole", logCfg, true);
        LogToFile = _config.GetConfigBool("ToFile", logCfg);
        var filePath = _config.GetConfigStr("FilePath", logCfg) ?? "logs/log.txt";
        LogToPostgres = _config.GetConfigBool("ToPostgres", logCfg);
        var postgresCommand = _config.GetConfigStr("PostgresCommand", logCfg);

        if (LogToConsole is true || (LogToFile is true)  || (LogToPostgres is true && postgresCommand is not null))
        {
            var loggerConfig = new LoggerConfiguration().MinimumLevel.Verbose();
            bool npgsqlRestAdded = false;
            bool systemAdded = false;
            bool microsoftAdded = false;
            foreach (var level in logCfg?.GetSection("MinimalLevels")?.GetChildren() ?? [])
            {
                var key = level.Key;
                var value = _config.GetEnum<Serilog.Events.LogEventLevel?>(level.Value);
                if (value is not null && key is not null)
                {
                    loggerConfig.MinimumLevel.Override(key, value.Value);
                    if (string.Equals(key, "NpgsqlRest", StringComparison.OrdinalIgnoreCase))
                    {
                        npgsqlRestAdded = true;
                    }
                    if (string.Equals(key, "System", StringComparison.OrdinalIgnoreCase))
                    {
                        systemAdded = true;
                    }
                    if (string.Equals(key, "Microsoft", StringComparison.OrdinalIgnoreCase))
                    {
                        microsoftAdded = true;
                    }
                }
            }
            if (npgsqlRestAdded is false)
            {
                loggerConfig.MinimumLevel.Override("NpgsqlRest", Serilog.Events.LogEventLevel.Debug);
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
            if (LogToConsole is true)
            {
                loggerConfig = loggerConfig.WriteTo.Console(
                    restrictedToMinimumLevel: 
                        _config.GetConfigEnum<Serilog.Events.LogEventLevel?>("ConsoleMinimumLevel", logCfg) ?? Serilog.Events.LogEventLevel.Verbose,
                    outputTemplate: outputTemplate,
                    theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code);
            }
            if (LogToFile is true)
            {
                loggerConfig = loggerConfig.WriteTo.File(
                    restrictedToMinimumLevel:
                        _config.GetConfigEnum<Serilog.Events.LogEventLevel?>("FileMinimumLevel", logCfg) ?? Serilog.Events.LogEventLevel.Verbose,
                    path: filePath ?? "logs/log.txt",
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: _config.GetConfigInt("FileSizeLimitBytes", logCfg) ?? 30000000,
                    retainedFileCountLimit: _config.GetConfigInt("RetainedFileCountLimit", logCfg) ?? 30,
                    rollOnFileSizeLimit: _config.GetConfigBool("RollOnFileSizeLimit", logCfg, defaultVal: true),
                    outputTemplate: outputTemplate);
            }
            if (LogToPostgres is true && postgresCommand is not null)
            {
                loggerConfig = loggerConfig.WriteTo.Postgres(
                    postgresCommand, 
                    restrictedToMinimumLevel:
                        _config.GetConfigEnum<Serilog.Events.LogEventLevel?>("PostgresMinimumLevel", logCfg) ?? Serilog.Events.LogEventLevel.Verbose,
                    connectionString: ConnectionString);
            }
            var serilog = loggerConfig.CreateLogger();
            var appName = _config.GetConfigStr("ApplicationName", _config.Cfg);
            
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
        }
    }

    public enum DataProtectionStorage
    {
        Default,
        FileSystem,
        Database
    }

    public string? BuildDataProtection()
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
                    storeElementCommand);
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

    private readonly string[] _connectionNames = ["Host", "Port", "Database", "Username", "Password", "Passfile", "SSL Mode", "Trust Server Certificate", "SSL Certificate", "SSL Key", "SSL Password", "Root Certificate", "Check Certificate Revocation", "SSL Negotiation", "Channel Binding", "Persist Security Info", "Kerberos Service Name", "Include Realm", "Include Error Detail", "Log Parameters", "Pooling", "Minimum Pool Size", "Maximum Pool Size", "Connection Idle Lifetime", "Connection Pruning Interval", "Connection Lifetime", "Timeout", "Command Timeout", "Cancellation Timeout", "Keepalive", "Tcp Keepalive", "Tcp Keepalive Time", "Tcp Keepalive Interval", "Max Auto Prepare", "Auto Prepare Min Usages", "Read Buffer Size", "Write Buffer Size", "Socket Receive Buffer Size", "Socket Send Buffer Size", "No Reset On Close", "Target Session Attributes", "Load Balance Hosts", "Host Recheck Seconds", "Options", "Application Name", "Enlist", "Search Path", "Client Encoding", "Encoding", "Timezone", "Array Nullability Mode"];

    private (string?, ConnectionRetryOptions) BuildConnection(
        string? connectionName, 
        string connectionString, 
        bool isMain,
        bool skipRetryOpts)
    {
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
                retryOptions.MaxRetryCount = _config.GetConfigInt("MaxRetryCount", retryCfg) ?? 6;
                retryOptions.MaxRetryDelay = TimeSpan.FromSeconds(_config.GetConfigInt("MaxRetryDelaySeconds", retryCfg) ?? 30);
                retryOptions.ExponentialBase = _config.GetConfigDouble("ExponentialBase", retryCfg) ?? 2.0;
                retryOptions.RandomFactor = _config.GetConfigDouble("RandomFactor", retryCfg) ?? 1.1;
                retryOptions.DelayCoefficient = TimeSpan.FromSeconds(_config.GetConfigInt("DelayCoefficientSeconds", retryCfg) ?? 1);
                var additionalErrorCodes = _config.GetConfigEnumerable("AdditionalErrorCodes", retryCfg)?.ToHashSet();
                if (additionalErrorCodes is not null && additionalErrorCodes.Count > 0)
                {
                    retryOptions.AdditionalErrorCodes = additionalErrorCodes;
                }
            }
        }
        if (_config.GetConfigBool("TestConnectionStrings", _config.ConnectionSettingsCfg) is true)
        {
            using var conn = new NpgsqlConnection(connectionString);
            try
            {
                NpgsqlConnectionRetryOpener.Open(conn, retryOptions, Logger);
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

    public Dictionary<string, StringValues> GetCustomServerSentEventsResponseHeaders()
    {
        var result = new Dictionary<string, StringValues>();
        foreach (var section in _config.NpgsqlRestCfg.GetSection("CustomServerSentEventsResponseHeaders").GetChildren())
        {
            result.Add(section.Key, section.Value);
        }
        return result;
    }
}
