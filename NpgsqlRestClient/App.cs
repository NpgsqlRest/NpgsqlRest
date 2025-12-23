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
using NpgsqlRest.OpenAPI;

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
        var availableClaims = _config.GetConfigEnumerable("AvailableClaims", parseCfg)?.ToArray();
        
        var antiforgery = app.Services.GetService<IAntiforgery>();

        AppStaticFileMiddleware.ConfigureStaticFileMiddleware(
            parse,
            filePaths,
            options,
            _config.GetConfigBool("CacheParsedFile", parseCfg, true),
            antiforgeryFieldNameTag,
            antiforgeryTokenTag,
            antiforgery,
            _config.GetConfigEnumerable("Headers", parseCfg)?.ToArray() ?? ["Cache-Control: no-store, no-cache, must-revalidate", "Pragma: no-cache", "Expires: 0"],
            authorizePaths,
            unauthorizedRedirectPath,
            unauthorizedReturnToQueryParameter,
            availableClaims,
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
            UseDefaultPasswordHasher = _config.GetConfigBool("UseDefaultPasswordHasher", basicAuthCfg, true),
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
            BodyColumnName = _config.GetConfigStr("BodyColumnName", authCfg) ?? "body",
            ResponseTypeColumnName = _config.GetConfigStr("ResponseTypeColumnName", authCfg) ?? "application/json",

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
            DefaultDataProtector = protector,
            CustomLoginHandler = CreateJwtLoginHandler()
        }, authCfg);
    }

    private Func<HttpContext, ClaimsPrincipal, string?, Task<bool>>? CreateJwtLoginHandler()
    {
        if (_builder.JwtTokenConfig is null)
        {
            return null;
        }

        // Initialize the static JWT login handler
        JwtLoginHandler.Initialize(_builder.JwtTokenConfig);
        var jwtScheme = _builder.JwtTokenConfig.Scheme;

        return async (context, principal, scheme) =>
        {
            // Only handle if the scheme matches JWT scheme or if no scheme specified and JWT is the default
            if (scheme is not null && !string.Equals(scheme, jwtScheme, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return await JwtLoginHandler.HandleLoginAsync(context, principal);
        };
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
            if (loginPath is not null && string.Equals(endpoint.Path, loginPath, StringComparison.OrdinalIgnoreCase))
            {
                endpoint.Login = true;
            }
            if (logoutPath is not null && string.Equals(endpoint.Routine.Name, logoutPath, StringComparison.OrdinalIgnoreCase))
            {
                endpoint.Login = true;
            }
        };
    }
    
    public Action<NpgsqlConnection, RoutineEndpoint, HttpContext>? BeforeConnectionOpen(string connectionString, NpgsqlRestAuthenticationOptions options)
    {
        if (_config.UseJsonApplicationName is false)
        {
            return null;
        }

        // Extract the application name to avoid capturing _builder reference
        var applicationName = _builder.Instance.Environment.ApplicationName;
        var headerName = _config.GetConfigStr("ExecutionIdHeaderName", _config.NpgsqlRestCfg) ?? "X-Execution-Id";

        _builder.Logger?.LogDebug("Using JsonApplicationName {{\"app\":\"{applicationName}\",\"uid\":\"<{UserIdClaimType}>\",\"id\":\"<{headerName}>\"}}",
            applicationName,
            options.DefaultUserIdClaimType,
            headerName);

        return (connection, endpoint, context) =>
        {
            var uid = context.User.FindFirstValue(options.DefaultUserIdClaimType);
            var executionId = context.Request.Headers[headerName].FirstOrDefault();
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
                NamePattern = _config.GetConfigStr("NamePattern", httpFilecfg) ?? "{0}_{1}",
                CommentHeader = _config.GetConfigEnum<CommentHeader?>("CommentHeader", httpFilecfg) ?? CommentHeader.Simple,
                CommentHeaderIncludeComments = _config.GetConfigBool("CommentHeaderIncludeComments", httpFilecfg, true),
                FileMode = _config.GetConfigEnum<HttpFileMode?>("FileMode", httpFilecfg) ?? HttpFileMode.Schema,
                FileOverwrite = _config.GetConfigBool("FileOverwrite", httpFilecfg, true),
                ConnectionString = connectionString
            }));
            _builder.Logger?.LogDebug("HTTP file generation enabled. Name={Name}",
                _config.GetConfigStr("Name", httpFilecfg) ?? "generated from connection string");
        }
        
        var openApiCfg = _config.NpgsqlRestCfg.GetSection("OpenApiOptions");
        if (openApiCfg is not null && _config.GetConfigBool("Enabled", openApiCfg) is true)
        {
            var openApi = new OpenApiOptions
            {
                FileName = _config.GetConfigStr("FileName", openApiCfg),
                UrlPath = _config.GetConfigStr("UrlPath", openApiCfg),
                FileOverwrite = _config.GetConfigBool("FileOverwrite", openApiCfg, true),
                DocumentTitle = _config.GetConfigStr("DocumentTitle", openApiCfg),
                DocumentVersion = _config.GetConfigStr("DocumentVersion", openApiCfg) ?? "1.0.0",
                DocumentDescription = _config.GetConfigStr("DocumentDescription", openApiCfg),
                ConnectionString = connectionString,
                AddCurrentServer = _config.GetConfigBool("AddCurrentServer", openApiCfg, true),
            };
            if (openApi.UrlPath is null && openApi.FileName is null)
            {
                _builder.Logger?.LogWarning("OpenAPI generation is disabled because both FileName and UrlPath are not set.");
            }
            else
            {
                var serversCfg = openApiCfg.GetSection("Servers");
                var servers = new List<OpenApiServer>(3);
                foreach (var serverSection in serversCfg.GetChildren())
                {
                    var url = _config.GetConfigStr("Url", serverSection);
                    if (url is null)
                    {
                        continue;
                    }
                    servers.Add(new OpenApiServer
                    {
                        Url = _config.GetConfigStr("Url", serverSection)!,
                        Description = _config.GetConfigStr("Description", serverSection)
                    });
                }
                if (servers.Count > 0)
                {
                    openApi.Servers = servers.ToArray();
                }

                var securitySchemesCfg = openApiCfg.GetSection("SecuritySchemes");
                var securitySchemes = new List<OpenApiSecurityScheme>(2);
                foreach (var schemeSection in securitySchemesCfg.GetChildren())
                {
                    var name = _config.GetConfigStr("Name", schemeSection);
                    var type = _config.GetConfigEnum<OpenApiSecuritySchemeType?>("Type", schemeSection);
                    if (name is null || type is null)
                    {
                        continue;
                    }

                    var scheme = new OpenApiSecurityScheme
                    {
                        Name = name,
                        Type = type.Value,
                        Description = _config.GetConfigStr("Description", schemeSection)
                    };

                    // HTTP scheme configuration (Bearer/Basic)
                    if (type == OpenApiSecuritySchemeType.Http)
                    {
                        scheme.Scheme = _config.GetConfigEnum<HttpAuthScheme?>("Scheme", schemeSection);
                        scheme.BearerFormat = _config.GetConfigStr("BearerFormat", schemeSection);
                    }

                    // API Key configuration (Cookie/Header/Query)
                    if (type == OpenApiSecuritySchemeType.ApiKey)
                    {
                        scheme.In = _config.GetConfigStr("In", schemeSection);
                        scheme.ApiKeyLocation = _config.GetConfigEnum<ApiKeyLocation?>("ApiKeyLocation", schemeSection);
                    }

                    securitySchemes.Add(scheme);
                }

                if (securitySchemes.Count > 0)
                {
                    openApi.SecuritySchemes = securitySchemes.ToArray();
                }

                handlers.Add(new OpenApi(openApi));
                _builder.Logger?.LogDebug("OpenAPI generation enabled. FileName={FileName}, UrlPath={UrlPath}",
                    openApi.FileName ?? "not set", openApi.UrlPath ?? "not set");
            }
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
                IncludeSchemaInNames = _config.GetConfigBool("IncludeSchemaInNames", tsClientCfg, true),
                ErrorExpression = _config.GetConfigStr("ErrorExpression", tsClientCfg) ?? "await response.json()",
                ErrorType = _config.GetConfigStr("ErrorType", tsClientCfg) ?? "{status: number; title: string; detail?: string | null} | undefined",
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
            _builder.Logger?.LogDebug("TypeScript client code generation enabled. FilePath={FilePath}", ts.FilePath);
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
        if (crudSourceCfg.Exists() is false || _config.GetConfigBool("Enabled", crudSourceCfg, true) is false)
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
            Enabled = _config.GetConfigBool("Enabled", uploadCfg),
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
                CsvUploadKey = _config.GetConfigStr("CsvUploadKey", uploadHandlersCfg) ?? "csv",
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
                strategy => new ExcelUploadHandler(result, strategy));
        }

        if (result?.UploadHandlers is not null && result.UploadHandlers.Count > 1)
        {
            _builder.Logger?.LogDebug("Using {Keys} upload handlers where {DefaultUploadHandler} is default.", result.UploadHandlers.Keys, result.DefaultUploadHandler);
            foreach (var uploadHandler in result.UploadHandlers)
            {
                _builder.Logger?.LogDebug("Upload handler {Key} has following parameters: {Parameters}", 
                    uploadHandler.Key, uploadHandler.Value(null!).SetType(uploadHandler.Key).Parameters);
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
            _builder.Logger?.LogDebug("ThreadPool minimum worker threads to {MinWorkerThreads} and minimum completion port threads to {MinCompletionPortThreads}",
                minWorkerThreads, minCompletionPortThreads);
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
            _builder.Logger?.LogDebug("ThreadPool maximum worker threads to {MaxWorkerThreads} and maximum completion port threads to {MaxCompletionPortThreads}",
                maxWorkerThreads, maxCompletionPortThreads);
        }
    }
}