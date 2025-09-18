using System.Net;
using System.Security.Claims;
using Npgsql;
using NpgsqlRest;
using NpgsqlRest.HttpFiles;
using NpgsqlRest.TsClient;
using Serilog;

using Microsoft.Extensions.Primitives;
using NpgsqlRest.CrudSource;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using NpgsqlRest.UploadHandlers;
using NpgsqlRest.Auth;

namespace NpgsqlRestClient;

public class App
{
    private readonly Config _config;
    private readonly Builder _builder;
    
    public App(Config config, Builder builder)
    {
        _config = config;
        _builder = builder;
    }
    
    public void Configure(WebApplication app, Action started)
    {
        app.Lifetime.ApplicationStarted.Register(started);
        if (_builder.UseHttpsRedirection)
        {
            app.UseHttpsRedirection();
        }
        if (_builder.UseHsts)
        {
            app.UseHsts();
        }

        if (_builder.LoggingEnabled is true)
        {
            app.UseSerilogRequestLogging();
        }

        var cfgCfg = _config.Cfg.GetSection("Config");
        var configEndpoint = _config.GetConfigStr("ExposeAsEndpoint", cfgCfg);
        if (configEndpoint is not null)
        {
            app.Use(async (context, next) =>
            {
                if (
                    string.Equals(context.Request.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.Path, configEndpoint, StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                    context.Response.ContentType = System.Net.Mime.MediaTypeNames.Application.Json;
                    await context.Response.WriteAsync(_config.Serialize());
                    await context.Response.CompleteAsync();
                    return;
                }
                await next(context);
            });
        }
    }

    public void ConfigureStaticFiles(WebApplication app, NpgsqlRestAuthenticationOptions options)
    {
        var staticFilesCfg = _config.Cfg.GetSection("StaticFiles");
        if (_config.Exists(staticFilesCfg) is false || _config.GetConfigBool("Enabled", staticFilesCfg) is false)
        {
            return;
        }

        app.UseDefaultFiles();

        string[]? authorizePaths = _config.GetConfigEnumerable("AuthorizePaths", staticFilesCfg)?.ToArray();
        string? unauthorizedRedirectPath = _config.GetConfigStr("UnauthorizedRedirectPath", staticFilesCfg);
        string? unauthorizedReturnToQueryParameter = _config.GetConfigStr("UnauthorizedReturnToQueryParameter", staticFilesCfg);

        var parseCfg = staticFilesCfg.GetSection("ParseContentOptions");
        
        bool parse = true;
        if (_config.Exists(parseCfg) is false || _config.GetConfigBool("Enabled", parseCfg) is false)
        {
            parse = false;
        }

        var filePaths = _config.GetConfigEnumerable("FilePaths", parseCfg)?.ToArray();
        var antiforgeryFieldNameTag = _config.GetConfigStr("AntiforgeryFieldName", parseCfg);
        var antiforgeryTokenTag = _config.GetConfigStr("AntiforgeryToken", parseCfg);
        var antiforgery = app.Services.GetService<IAntiforgery>();

        AppStaticFileMiddleware.ConfigureStaticFileMiddleware(
            parse,
            filePaths,
            options,
            _config.GetConfigBool("CacheParsedFile", parseCfg, true),
            antiforgeryFieldNameTag,
            antiforgeryTokenTag,
            antiforgery,
            _config.GetConfigEnumerable("Headers", parseCfg)?.ToArray(),
            authorizePaths,
            unauthorizedRedirectPath,
            unauthorizedReturnToQueryParameter,
            _builder.Logger);

        app.UseMiddleware<AppStaticFileMiddleware>();
        _builder.Logger?.LogDebug("Serving static files from {WebRootPath}. Parsing following file path patterns: {filePaths}", app.Environment.WebRootPath, filePaths);
    }

    public string CreateUrl(Routine routine, NpgsqlRestOptions options) =>
        string.Concat(
            string.IsNullOrEmpty(options.UrlPathPrefix) ? "/" : string.Concat("/", options.UrlPathPrefix.Trim('/')),
            routine.Schema == "public" ? "" : routine.Schema.Trim(Consts.DoubleQuote).Trim('/'),
            "/",
            routine.Name.Trim(Consts.DoubleQuote).Trim('/'),
            "/");

    public (NpgsqlRestAuthenticationOptions options, IConfigurationSection authCfg) CreateNpgsqlRestAuthenticationOptions(
        WebApplication app,
        string? dataProtectionName)
    {
        var authCfg = _config.NpgsqlRestCfg.GetSection("AuthenticationOptions");
        if (_config.Exists(authCfg) is false)
        {
            return (new NpgsqlRestAuthenticationOptions(), authCfg);
        }
        
        var basicAuthCfg = authCfg.GetSection("BasicAuth");
        BasicAuthOptions basicAuth = new()
        {
            Enabled = _config.GetConfigBool("Enabled", basicAuthCfg, false),
            Realm = _config.GetConfigStr("Realm", basicAuthCfg) ?? BasicAuthOptions.DefaultRealm,
            Users = _config.GetConfigDict(basicAuthCfg.GetSection("Users")) ?? new Dictionary<string, string>(),
            ChallengeCommand = _config.GetConfigStr("ChallengeCommand", basicAuthCfg),
            UseDefaultPasswordHasher = _config.GetConfigBool("UseDefaultPasswordHasher", basicAuthCfg, false),
            SslRequirement = _config.GetConfigEnum<SslRequirement?>("SslRequirement", basicAuthCfg) ?? SslRequirement.Required
        };

        if (basicAuth.Enabled is true && _builder.SslEnabled is false)
        {
            if (basicAuth.SslRequirement == SslRequirement.Required)
            {
                throw new InvalidOperationException("Basic authentication with SslRequirement 'Required' cannot be used when SSL is disabled.");
            }
            if (basicAuth.SslRequirement == SslRequirement.Warning)
            {
                _builder.Logger?.LogWarning("Using Basic Authentication when SSL is disabled.");
            }
            else if (basicAuth.SslRequirement == SslRequirement.Ignore)
            {
                _builder.Logger?.LogDebug("WARNING: Using Basic Authentication when SSL is disabled.");
            }
        }

        if (basicAuth.Enabled is true)
        {
            _builder.Logger?.LogDebug("Basic Authentication enabled with realm {Realm}", basicAuth.Realm);
        }
        
        var provider = app.Services.GetService<IDataProtectionProvider>();
        IDataProtector? protector = dataProtectionName is null ? null : provider?.CreateProtector(dataProtectionName);

        return (new()
        {
            DefaultAuthenticationType = _config.GetConfigStr("DefaultAuthenticationType", authCfg),

            StatusColumnName = _config.GetConfigStr("StatusColumnName", authCfg) ?? "status",
            SchemeColumnName = _config.GetConfigStr("SchemeColumnName", authCfg) ?? "scheme",
            MessageColumnName = _config.GetConfigStr("MessageColumnName", authCfg) ?? "message",

            DefaultUserIdClaimType = _config.GetConfigStr("DefaultUserIdClaimType", authCfg) ?? "user_id",
            DefaultNameClaimType = _config.GetConfigStr("DefaultNameClaimType", authCfg) ?? "user_name",
            DefaultRoleClaimType = _config.GetConfigStr("DefaultRoleClaimType", authCfg) ?? "user_roles",

            SerializeAuthEndpointsResponse = _config.GetConfigBool("SerializeAuthEndpointsResponse", authCfg, false),
            ObfuscateAuthParameterLogValues = _config.GetConfigBool("ObfuscateAuthParameterLogValues", authCfg, true),
            HashColumnName = _config.GetConfigStr("HashColumnName", authCfg) ?? "hash",
            PasswordParameterNameContains = _config.GetConfigStr("PasswordParameterNameContains", authCfg) ?? "pass",

            PasswordVerificationFailedCommand = _config.GetConfigStr("PasswordVerificationFailedCommand", authCfg),
            PasswordVerificationSucceededCommand = _config.GetConfigStr("PasswordVerificationSucceededCommand", authCfg),
            UseUserContext = _config.GetConfigBool("UseUserContext", authCfg, false),
            ContextKeyClaimsMapping = _config.GetConfigDict(authCfg.GetSection("ContextKeyClaimsMapping")) ?? new()
            {
                { "request.user_id", "user_id" },
                { "request.user_name", "user_name" },
                { "request.user_roles" , "user_roles" },
            },
            ClaimsJsonContextKey = _config.GetConfigStr("ClaimsJsonContextKey", authCfg),
            IpAddressContextKey = _config.GetConfigStr("IpAddressContextKey", authCfg) ?? "request.ip_address",
            UseUserParameters = _config.GetConfigBool("UseUserParameters", authCfg, false),
            ParameterNameClaimsMapping = _config.GetConfigDict(authCfg.GetSection("ParameterNameClaimsMapping")) ?? new()
            {
                { "_user_id" , "user_id" },
                { "_user_name" , "user_name" },
                { "_user_roles" , "user_roles" },
            },
            ClaimsJsonParameterName = _config.GetConfigStr("ClaimsJsonParameterName", authCfg) ?? "_user_claims",
            IpAddressParameterName = _config.GetConfigStr("IpAddressParameterName", authCfg) ?? "_ip_address",
            
            BasicAuth = basicAuth,
            DefaultDataProtector = protector
        }, authCfg);
    }

    public Action<RoutineEndpoint?>? CreateEndpointCreatedHandler(IConfigurationSection authCfg)
    {
        if (_config.Exists(authCfg) is false)
        {
            return null;
        }
        var loginPath = _config.GetConfigStr("LoginPath", authCfg);
        var logoutPath = _config.GetConfigStr("LogoutPath", authCfg);
        if (loginPath is null && logoutPath is null)
        {
            return null;
        }
        return endpoint =>
        {
            if (endpoint is null)
            {
                return;
            }
            if (loginPath is not null && string.Equals(endpoint.Url, loginPath, StringComparison.OrdinalIgnoreCase))
            {
                endpoint.Login = true;
            }
            if (logoutPath is not null && string.Equals(endpoint.Routine.Name, logoutPath, StringComparison.OrdinalIgnoreCase))
            {
                endpoint.Login = true;
            }
        };
    }

    public Dictionary<string, int> CreatePostgreSqlErrorCodeToHttpStatusCodeMapping()
    {
        if (_config.Exists(_config.NpgsqlRestCfg) is false)
        {
            return new()
            {
                { "57014", 205 }, //query_canceled -> 205 Reset Content
                { "P0001", 400 }, // raise_exception -> 400 Bad Request
                { "P0004", 400 }, // assert_failure -> 400 Bad Request
            };
        }
        var config = _config.NpgsqlRestCfg.GetSection("PostgreSqlErrorCodeToHttpStatusCodeMapping");
        if (config.Exists() is false)
        {
            return new()
            {
                { "57014", 205 }, //query_canceled -> 205 Reset Content
                { "P0001", 400 }, // raise_exception -> 400 Bad Request
                { "P0004", 400 }, // assert_failure -> 400 Bad Request
            };
        }
        var result = new Dictionary<string, int>();
        foreach (var section in config.GetChildren())
        {
            if (int.TryParse(section.Value, out var value))
            {
                result.TryAdd(section.Key, value);
            }
        }
        return result;
    }

    public Action<NpgsqlConnection, RoutineEndpoint, HttpContext>? BeforeConnectionOpen(string connectionString, NpgsqlRestAuthenticationOptions options)
    {
        if (_config.UseConnectionApplicationNameWithUsername is false)
        {
            return null;
        }

        // Extract the application name to avoid capturing _builder reference
        var applicationName = _builder.Instance.Environment.ApplicationName;
        
        return (connection, endpoint, context) =>
        {
            var uid = context.User.FindFirstValue(options.DefaultUserIdClaimType);
            var executionId = context.Request.Headers["X-Execution-Id"].FirstOrDefault();
            connection.ConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
            {
                ApplicationName = string.Concat("{\"app\":\"", applicationName,
                        "\",\"uid\":", uid is null ? "null" : string.Concat("\"", uid, "\""),
                        ",\"id\":", executionId is null ? "null" : string.Concat("\"", executionId, "\""),
                        "}")
            }.ConnectionString;
        };
    }

    public List<IEndpointCreateHandler> CreateCodeGenHandlers(string connectionString)
    {
        List<IEndpointCreateHandler> handlers = new(2);
        var httpFilecfg = _config.NpgsqlRestCfg.GetSection("HttpFileOptions");
        if (httpFilecfg is not null && _config.GetConfigBool("Enabled", httpFilecfg) is true)
        {
            handlers.Add(new HttpFile(new HttpFileOptions
            {
                Name = _config.GetConfigStr("Name", httpFilecfg),
                Option = _config.GetConfigEnum<HttpFileOption?>("Option", httpFilecfg) ?? HttpFileOption.File,
                NamePattern = _config.GetConfigStr("NamePattern", httpFilecfg) ?? "{0}{1}",
                CommentHeader = _config.GetConfigEnum<CommentHeader?>("CommentHeader", httpFilecfg) ?? CommentHeader.Simple,
                CommentHeaderIncludeComments = _config.GetConfigBool("CommentHeaderIncludeComments", httpFilecfg, true),
                FileMode = _config.GetConfigEnum<HttpFileMode?>("FileMode", httpFilecfg) ?? HttpFileMode.Schema,
                FileOverwrite = _config.GetConfigBool("FileOverwrite", httpFilecfg, true),
                ConnectionString = connectionString
            }));
        }

        var tsClientCfg = _config.NpgsqlRestCfg.GetSection("ClientCodeGen");
        if (tsClientCfg is not null && _config.GetConfigBool("Enabled", tsClientCfg) is true)
        {
            var ts = new TsClientOptions
            {
                FilePath = _config.GetConfigStr("FilePath", tsClientCfg),
                FileOverwrite = _config.GetConfigBool("FileOverwrite", tsClientCfg, true),
                IncludeHost = _config.GetConfigBool("IncludeHost", tsClientCfg, true),
                CustomHost = _config.GetConfigStr("CustomHost", tsClientCfg),
                CommentHeader = _config.GetConfigEnum<CommentHeader?>("CommentHeader", tsClientCfg) ?? CommentHeader.Simple,
                CommentHeaderIncludeComments = _config.GetConfigBool("CommentHeaderIncludeComments", tsClientCfg, true),
                BySchema = _config.GetConfigBool("BySchema", tsClientCfg, true),
                IncludeStatusCode = _config.GetConfigBool("IncludeStatusCode", tsClientCfg, true),
                CreateSeparateTypeFile = _config.GetConfigBool("CreateSeparateTypeFile", tsClientCfg, true),
                ImportBaseUrlFrom = _config.GetConfigStr("ImportBaseUrlFrom", tsClientCfg),
                ImportParseQueryFrom = _config.GetConfigStr("ImportParseQueryFrom", tsClientCfg),
                IncludeParseUrlParam = _config.GetConfigBool("IncludeParseUrlParam", tsClientCfg),
                IncludeParseRequestParam = _config.GetConfigBool("IncludeParseRequestParam", tsClientCfg),
                UseRoutineNameInsteadOfEndpoint = _config.GetConfigBool("UseRoutineNameInsteadOfEndpoint", tsClientCfg),
                DefaultJsonType = _config.GetConfigStr("DefaultJsonType", tsClientCfg) ?? "string",
                ExportUrls = _config.GetConfigBool("ExportUrls", tsClientCfg),
                SkipTypes = _config.GetConfigBool("SkipTypes", tsClientCfg),
                UniqueModels = _config.GetConfigBool("UniqueModels", tsClientCfg),
                XsrfTokenHeaderName = _config.GetConfigStr("XsrfTokenHeaderName", tsClientCfg),
                ExportEventSources = _config.GetConfigBool("ExportEventSources", tsClientCfg, true),
                CustomImports = _config.GetConfigEnumerable("CustomImports", tsClientCfg)?.ToArray() ?? [],
            };

            Dictionary<string, string> customHeaders = [];
            foreach (var section in tsClientCfg.GetSection("CustomHeaders").GetChildren())
            {
                if (section?.Value is null)
                {
                    continue;
                }
                customHeaders.Add(section.Key, section.Value!);
            }
            ts.CustomHeaders = customHeaders;

            var headerLines = _config.GetConfigEnumerable("HeaderLines", tsClientCfg);
            if (headerLines is not null)
            {
                ts.HeaderLines = [.. headerLines];
            }

            var skipRoutineNames = _config.GetConfigEnumerable("SkipRoutineNames", tsClientCfg);
            if (skipRoutineNames is not null)
            {
                ts.SkipRoutineNames = [.. skipRoutineNames];
            }

            var skipFunctionNames = _config.GetConfigEnumerable("SkipFunctionNames", tsClientCfg);
            if (skipFunctionNames is not null)
            {
                ts.SkipFunctionNames = [.. skipFunctionNames];
            }

            var skipPaths = _config.GetConfigEnumerable("SkipPaths", tsClientCfg);
            if (skipPaths is not null)
            {
                ts.SkipPaths = [.. skipPaths];
            }

            var skipSchemas = _config.GetConfigEnumerable("SkipSchemas", tsClientCfg);
            if (skipSchemas is not null)
            {
                ts.SkipSchemas = [.. skipSchemas];
            }

            handlers.Add(new TsClient(ts));
        }

        return handlers;
    }

    public List<IRoutineSource> CreateRoutineSources()
    {
        var sources = new List<IRoutineSource>(2);

        var source = new RoutineSource();
        var routineOptionsCfg = _config.NpgsqlRestCfg.GetSection("RoutineOptions");
        if (routineOptionsCfg.Exists() is true)
        {
            var customTypeParameterSeparator = _config.GetConfigStr("CustomTypeParameterSeparator", routineOptionsCfg);
            if (customTypeParameterSeparator is not null)
            {
                source.CustomTypeParameterSeparator = customTypeParameterSeparator;
            }
            var includeLanguages = _config.GetConfigEnumerable("IncludeLanguages", routineOptionsCfg);
            if (includeLanguages is not null)
            {
                source.IncludeLanguages = [.. includeLanguages];
            }
            var excludeLanguages = _config.GetConfigEnumerable("ExcludeLanguages", routineOptionsCfg);
            if (excludeLanguages is not null)
            {
                source.ExcludeLanguages = [.. excludeLanguages];
            }
        }
        sources.Add(source);
        _builder.Logger?.LogDebug("Using {name} PostrgeSQL Source", nameof(RoutineSource));

        var crudSourceCfg = _config.NpgsqlRestCfg.GetSection("CrudSource");
        if (crudSourceCfg.Exists() is false || _config.GetConfigBool("Enabled", crudSourceCfg) is false)
        {
            return sources;
        }
        sources.Add(new CrudSource()
        {
            SchemaSimilarTo = _config.GetConfigStr("SchemaSimilarTo", crudSourceCfg),
            SchemaNotSimilarTo = _config.GetConfigStr("SchemaNotSimilarTo", crudSourceCfg),
            IncludeSchemas = _config.GetConfigEnumerable("IncludeSchemas", crudSourceCfg)?.ToArray(),
            ExcludeSchemas = _config.GetConfigEnumerable("ExcludeSchemas", crudSourceCfg)?.ToArray(),
            NameSimilarTo = _config.GetConfigStr("NameSimilarTo", crudSourceCfg),
            NameNotSimilarTo = _config.GetConfigStr("NameNotSimilarTo", crudSourceCfg),
            IncludeNames = _config.GetConfigEnumerable("IncludeNames", crudSourceCfg)?.ToArray(),
            ExcludeNames = _config.GetConfigEnumerable("ExcludeNames", crudSourceCfg)?.ToArray(),
            CommentsMode = _config.GetConfigEnum<CommentsMode?>("CommentsMode", crudSourceCfg),
            CrudTypes = _config.GetConfigFlag<CrudCommandType>("CrudTypes", crudSourceCfg),

            ReturningUrlPattern = _config.GetConfigStr("ReturningUrlPattern", crudSourceCfg) ?? "{0}/returning",
            OnConflictDoNothingUrlPattern = _config.GetConfigStr("OnConflictDoNothingUrlPattern", crudSourceCfg) ?? "{0}/on-conflict-do-nothing",
            OnConflictDoNothingReturningUrlPattern = _config.GetConfigStr("OnConflictDoNothingReturningUrlPattern", crudSourceCfg) ?? "{0}/on-conflict-do-nothing/returning",
            OnConflictDoUpdateUrlPattern = _config.GetConfigStr("OnConflictDoUpdateUrlPattern", crudSourceCfg) ?? "{0}/on-conflict-do-update",
            OnConflictDoUpdateReturningUrlPattern = _config.GetConfigStr("OnConflictDoUpdateReturningUrlPattern", crudSourceCfg) ?? "{0}/on-conflict-do-update/returning",
        });
        _builder.Logger?.LogDebug("Using {name} PostrgeSQL Source", nameof(CrudSource));
        return sources;
    }

    public NpgsqlRestUploadOptions CreateUploadOptions()
    {
        var uploadCfg = _config.NpgsqlRestCfg.GetSection("UploadOptions");
        if (uploadCfg.Exists() is false)
        {
            return new NpgsqlRestUploadOptions();
        }

        var result = new NpgsqlRestUploadOptions
        {
            Enabled = _config.GetConfigBool("Enabled", uploadCfg, true),
            LogUploadEvent = _config.GetConfigBool("LogUploadEvent", uploadCfg, true),
            LogUploadParameters = _config.GetConfigBool("LogUploadParameters", uploadCfg, false),
            DefaultUploadHandler = _config.GetConfigStr("DefaultUploadHandler", uploadCfg) ?? "large_object",
            UseDefaultUploadMetadataParameter = _config.GetConfigBool("UseDefaultUploadMetadataParameter", uploadCfg, false),
            DefaultUploadMetadataParameterName = _config.GetConfigStr("DefaultUploadMetadataParameterName", uploadCfg) ?? "_upload_metadata",
            UseDefaultUploadMetadataContextKey = _config.GetConfigBool("UseDefaultUploadMetadataContextKey", uploadCfg, false),
            DefaultUploadMetadataContextKey = _config.GetConfigStr("DefaultUploadMetadataContextKey", uploadCfg) ?? "request.upload_metadata",
        };

        var uploadHandlersCfg = uploadCfg.GetSection("UploadHandlers");
        UploadHandlerOptions uploadHandlerOptions;
        if (uploadHandlersCfg.Exists() is false)
        {
            uploadHandlerOptions = new();
        }
        else
        {
            uploadHandlerOptions = new()
            {
                StopAfterFirstSuccess = _config.GetConfigBool("StopAfterFirstSuccess", uploadHandlersCfg, false),
                IncludedMimeTypePatterns = _config.GetConfigStr("IncludedMimeTypePatterns", uploadHandlersCfg).SplitParameter(),
                ExcludedMimeTypePatterns = _config.GetConfigStr("ExcludedMimeTypePatterns", uploadHandlersCfg).SplitParameter(),
                BufferSize = _config.GetConfigInt("BufferSize", uploadHandlersCfg) ?? 8192,
                TextTestBufferSize = _config.GetConfigInt("TextTestBufferSize", uploadHandlersCfg) ?? 4096,
                TextNonPrintableThreshold = _config.GetConfigInt("TextNonPrintableThreshold", uploadHandlersCfg) ?? 5,

                LargeObjectEnabled = _config.GetConfigBool("LargeObjectEnabled", uploadHandlersCfg, true),
                LargeObjectKey = _config.GetConfigStr("LargeObjectKey", uploadHandlersCfg) ?? "large_object",
                LargeObjectCheckText = _config.GetConfigBool("LargeObjectCheckText", uploadHandlersCfg, false),
                LargeObjectCheckImage = _config.GetConfigBool("LargeObjectCheckImage", uploadHandlersCfg, false),

                FileSystemEnabled = _config.GetConfigBool("FileSystemEnabled", uploadHandlersCfg, true),
                FileSystemKey = _config.GetConfigStr("FileSystemKey", uploadHandlersCfg) ?? "file_system",
                FileSystemPath = _config.GetConfigStr("FileSystemPath", uploadHandlersCfg) ?? "/tmp/uploads",
                FileSystemUseUniqueFileName = _config.GetConfigBool("FileSystemUseUniqueFileName", uploadHandlersCfg, true),
                FileSystemCreatePathIfNotExists = _config.GetConfigBool("FileSystemCreatePathIfNotExists", uploadHandlersCfg, true),
                FileSystemCheckText = _config.GetConfigBool("FileSystemCheckText", uploadHandlersCfg, false),
                FileSystemCheckImage = _config.GetConfigBool("FileSystemCheckImage", uploadHandlersCfg, false),

                CsvUploadEnabled = _config.GetConfigBool("CsvUploadEnabled", uploadHandlersCfg, true),
                CsvUploadCheckFileStatus = _config.GetConfigBool("CsvUploadCheckFileStatus", uploadHandlersCfg, true),
                CsvUploadDelimiterChars = _config.GetConfigStr("CsvUploadDelimiterChars", uploadHandlersCfg) ?? ",",
                CsvUploadHasFieldsEnclosedInQuotes = _config.GetConfigBool("CsvUploadHasFieldsEnclosedInQuotes", uploadHandlersCfg, true),
                CsvUploadSetWhiteSpaceToNull = _config.GetConfigBool("CsvUploadSetWhiteSpaceToNull", uploadHandlersCfg, true),
                CsvUploadRowCommand = _config.GetConfigStr("CsvUploadRowCommand", uploadHandlersCfg) ?? "call process_csv_row($1,$2,$3,$4)",
            };
            var imageTypes = _config.GetConfigStr("AllowedImageTypes", uploadHandlersCfg)?.ParseImageTypes(null);
            if (imageTypes is not null)
            {
                uploadHandlerOptions.AllowedImageTypes = imageTypes.Value;
            }
        }
        result.DefaultUploadHandlerOptions = uploadHandlerOptions;

        result.UploadHandlers = result.CreateUploadHandlers();

        if (_config.GetConfigBool("ExcelUploadEnabled", uploadHandlersCfg, true))
        {
            // Initialize ExcelDataReader encoding provider
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            ExcelUploadOptions.Instance.ExcelSheetName = _config.GetConfigStr("ExcelSheetName", uploadHandlersCfg) ?? null;
            ExcelUploadOptions.Instance.ExcelAllSheets = _config.GetConfigBool("ExcelAllSheets", uploadHandlersCfg, false);
            ExcelUploadOptions.Instance.ExcelTimeFormat = _config.GetConfigStr("ExcelTimeFormat", uploadHandlersCfg) ?? "HH:mm:ss";
            ExcelUploadOptions.Instance.ExcelDateFormat = _config.GetConfigStr("ExcelDateFormat", uploadHandlersCfg) ?? "yyyy-MM-dd";
            ExcelUploadOptions.Instance.ExcelDateTimeFormat = _config.GetConfigStr("ExcelDateTimeFormat", uploadHandlersCfg) ?? "yyyy-MM-dd HH:mm:ss";
            ExcelUploadOptions.Instance.ExcelRowDataAsJson = _config.GetConfigBool("ExcelRowDataAsJson", uploadHandlersCfg, false);
            ExcelUploadOptions.Instance.ExcelUploadRowCommand = _config.GetConfigStr("ExcelUploadRowCommand", uploadHandlersCfg) ?? "call process_excel_row($1,$2,$3,$4)";

            result?.UploadHandlers?.Add(
                _config.GetConfigStr("ExcelKey", uploadHandlersCfg) ?? "excel", 
                (strategy, logger) => new ExcelUploadHandler(result, strategy, logger));
        }

        if (result?.UploadHandlers is not null && result.UploadHandlers.Count > 1)
        {
            _builder.Logger?.LogDebug("Using {Keys} upload handlers where {DefaultUploadHandler} is default.", result.UploadHandlers.Keys, result.DefaultUploadHandler);
            foreach (var uploadHandler in result.UploadHandlers)
            {
                _builder.Logger?.LogDebug("Upload handler {Key} has following parameters: {Parameters}", 
                    uploadHandler.Key, uploadHandler.Value(null!, null!).SetType(uploadHandler.Key).Parameters);
            }
        }
        return result!;
    }

    public void ConfigureThreadPool()
    {
        var threadPoolCfg = _config.Cfg.GetSection("ThreadPool");
        if (threadPoolCfg.Exists() is false)
        {
            return;
        }

        var minWorkerThreads = _config.GetConfigInt("MinWorkerThreads", threadPoolCfg);
        var minCompletionPortThreads = _config.GetConfigInt("MinCompletionPortThreads", threadPoolCfg);
        if (minWorkerThreads is not null || minCompletionPortThreads is not null)
        {
            if (minWorkerThreads is null || minCompletionPortThreads is null)
            {
                ThreadPool.GetMinThreads(out var minWorkerThreadsTmp, out var minCompletionPortThreadsTmp);
                minWorkerThreads ??= minWorkerThreadsTmp;
                minCompletionPortThreads ??= minCompletionPortThreadsTmp;
            }
            ThreadPool.SetMinThreads(workerThreads: minWorkerThreads.Value, completionPortThreads: minCompletionPortThreads.Value);
        }

        var maxWorkerThreads = _config.GetConfigInt("MaxWorkerThreads", threadPoolCfg);
        var maxCompletionPortThreads = _config.GetConfigInt("MaxCompletionPortThreads", threadPoolCfg);
        if (maxWorkerThreads is not null || maxCompletionPortThreads is not null)
        {
            if (maxWorkerThreads is null || maxCompletionPortThreads is null)
            {
                ThreadPool.GetMaxThreads(out var maxWorkerThreadsTmp, out var maxCompletionPortThreadsTmp);
                maxWorkerThreads ??= maxWorkerThreadsTmp;
                maxCompletionPortThreads ??= maxCompletionPortThreadsTmp;
            }
            ThreadPool.SetMaxThreads(workerThreads: maxWorkerThreads.Value, completionPortThreads: maxCompletionPortThreads.Value);
        }
    }
}