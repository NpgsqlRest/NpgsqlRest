using System.Text.Json.Nodes;

namespace NpgsqlRestClient;

/// <summary>
/// Centralized configuration defaults for NpgsqlRestClient.
/// All default values are defined here to ensure consistency between
/// runtime behavior and --config output.
/// </summary>
public static class ConfigDefaults
{
    // Helper methods to create JsonArray from primitives (AOT-compatible)
    // Using JsonNode cast to avoid generic Add<T> which requires runtime code generation
    private static JsonArray CreateIntArray(params int[] values)
    {
        var arr = new JsonArray();
        foreach (var v in values) arr.Add((JsonNode)v);
        return arr;
    }

    private static JsonArray CreateStringArray(params string[] values)
    {
        var arr = new JsonArray();
        foreach (var v in values) arr.Add((JsonNode)v);
        return arr;
    }

    /// <summary>
    /// Returns a JsonObject containing all default configuration values.
    /// </summary>
    public static JsonObject GetDefaults()
    {
        return new JsonObject
        {
            ["ApplicationName"] = null,
            ["EnvironmentName"] = "Production",
            ["Urls"] = "http://localhost:8080",
            ["StartupMessage"] = "Started in {time}, listening on {urls}, version {version}",

            ["Config"] = new JsonObject
            {
                ["AddEnvironmentVariables"] = false,
                ["ParseEnvironmentVariables"] = true,
                ["EnvFile"] = null,
                ["ValidateConfigKeys"] = "Warning"
            },

            ["ConnectionStrings"] = new JsonObject
            {
                ["Default"] = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres"
            },

            ["ConnectionSettings"] = new JsonObject
            {
                ["SetApplicationNameInConnection"] = true,
                ["UseJsonApplicationName"] = false,
                ["TestConnectionStrings"] = false,
                ["RetryOptions"] = new JsonObject
                {
                    ["Enabled"] = true,
                    ["RetrySequenceSeconds"] = CreateIntArray(1, 3, 6, 12),
                    ["ErrorCodes"] = CreateStringArray("08000", "08003", "08006", "08001", "08004", "55P03", "55006", "53300", "57P03", "40001")
                },
                ["MetadataQueryConnectionName"] = null,
                ["MetadataQuerySchema"] = "public",
                ["MultiHostConnectionTargets"] = new JsonObject
                {
                    ["Default"] = "Any",
                    ["ByConnectionName"] = new JsonObject()
                }
            },

            ["Ssl"] = new JsonObject
            {
                ["Enabled"] = false,
                ["UseHttpsRedirection"] = true,
                ["UseHsts"] = true
            },

            ["DataProtection"] = new JsonObject
            {
                ["Enabled"] = true,
                ["CustomApplicationName"] = null,
                ["DefaultKeyLifetimeDays"] = 90,
                ["Storage"] = "Default",
                ["FileSystemPath"] = "./data-protection-keys",
                ["GetAllElementsCommand"] = "select get_data_protection_keys()",
                ["StoreElementCommand"] = "call store_data_protection_keys($1,$2)",
                ["EncryptionAlgorithm"] = null,
                ["ValidationAlgorithm"] = null,
                ["KeyEncryption"] = "None",
                ["CertificatePath"] = null,
                ["CertificatePassword"] = null,
                ["DpapiLocalMachine"] = false
            },

            ["Kestrel"] = new JsonObject
            {
                ["Endpoints"] = new JsonObject(),
                ["Certificates"] = new JsonObject(),
                ["Limits"] = new JsonObject
                {
                    ["MaxConcurrentConnections"] = null,
                    ["MaxConcurrentUpgradedConnections"] = null,
                    ["MaxRequestBodySize"] = null,
                    ["MaxRequestBufferSize"] = null,
                    ["MaxRequestHeaderCount"] = null,
                    ["MaxRequestHeadersTotalSize"] = null,
                    ["MaxRequestLineSize"] = null,
                    ["MaxResponseBufferSize"] = null,
                    ["KeepAliveTimeout"] = null,
                    ["RequestHeadersTimeout"] = null,
                    ["Http2"] = new JsonObject
                    {
                        ["MaxStreamsPerConnection"] = null,
                        ["HeaderTableSize"] = null,
                        ["MaxFrameSize"] = null,
                        ["MaxRequestHeaderFieldSize"] = null,
                        ["InitialConnectionWindowSize"] = null,
                        ["InitialStreamWindowSize"] = null,
                        ["MaxReadFrameSize"] = null,
                        ["KeepAlivePingDelay"] = null,
                        ["KeepAlivePingTimeout"] = null,
                        ["KeepAlivePingPolicy"] = null
                    },
                    ["Http3"] = new JsonObject
                    {
                        ["MaxRequestHeaderFieldSize"] = null
                    }
                },
                ["DisableStringReuse"] = false,
                ["AllowAlternateSchemes"] = false,
                ["AllowSynchronousIO"] = false,
                ["AllowResponseHeaderCompression"] = true,
                ["AddServerHeader"] = true,
                ["AllowHostHeaderOverride"] = false
            },

            ["ThreadPool"] = new JsonObject
            {
                ["MinWorkerThreads"] = null,
                ["MinCompletionPortThreads"] = null,
                ["MaxWorkerThreads"] = null,
                ["MaxCompletionPortThreads"] = null
            },

            ["Auth"] = GetAuthDefaults(),
            ["Log"] = GetLogDefaults(),
            ["ResponseCompression"] = GetResponseCompressionDefaults(),
            ["Antiforgery"] = GetAntiforgeryDefaults(),
            ["StaticFiles"] = GetStaticFilesDefaults(),
            ["Cors"] = GetCorsDefaults(),
            ["SecurityHeaders"] = GetSecurityHeadersDefaults(),
            ["ForwardedHeaders"] = GetForwardedHeadersDefaults(),
            ["HealthChecks"] = GetHealthChecksDefaults(),
            ["Stats"] = GetStatsDefaults(),
            ["CommandRetryOptions"] = GetCommandRetryOptionsDefaults(),
            ["CacheOptions"] = GetCacheOptionsDefaults(),
            ["ValidationOptions"] = GetValidationOptionsDefaults(),
            ["RateLimiterOptions"] = GetRateLimiterOptionsDefaults(),
            ["ErrorHandlingOptions"] = GetErrorHandlingOptionsDefaults(),
            ["NpgsqlRest"] = GetNpgsqlRestDefaults()
        };
    }

    private static JsonObject GetAuthDefaults()
    {
        return new JsonObject
        {
            ["CookieAuth"] = false,
            ["CookieAuthScheme"] = null,
            ["CookieValidDays"] = 14,
            ["CookieName"] = null,
            ["CookiePath"] = null,
            ["CookieDomain"] = null,
            ["CookieMultiSessions"] = true,
            ["CookieHttpOnly"] = true,
            ["BearerTokenAuth"] = false,
            ["BearerTokenAuthScheme"] = null,
            ["BearerTokenExpireHours"] = 1,
            ["BearerTokenRefreshPath"] = "/api/token/refresh",
            ["JwtAuth"] = false,
            ["JwtAuthScheme"] = null,
            ["JwtSecret"] = null,
            ["JwtIssuer"] = null,
            ["JwtAudience"] = null,
            ["JwtExpireMinutes"] = 60,
            ["JwtRefreshExpireDays"] = 7,
            ["JwtValidateIssuer"] = false,
            ["JwtValidateAudience"] = false,
            ["JwtValidateLifetime"] = true,
            ["JwtValidateIssuerSigningKey"] = true,
            ["JwtClockSkew"] = "5 minutes",
            ["JwtRefreshPath"] = "/api/jwt/refresh",
            ["External"] = new JsonObject
            {
                ["Enabled"] = false,
                ["BrowserSessionStatusKey"] = "__external_status",
                ["BrowserSessionMessageKey"] = "__external_message",
                ["SigninUrl"] = "/signin-{0}",
                ["SignInHtmlTemplate"] = "<!DOCTYPE html><html><head><meta charset=\"utf-8\" /><title>Talking To {0}</title></head><body>Loading...{1}</body></html>",
                ["RedirectUrl"] = null,
                ["ReturnToPath"] = "/",
                ["ReturnToPathQueryStringKey"] = "return_to",
                ["LoginCommand"] = "select * from external_login($1,$2,$3,$4,$5)",
                ["ClientAnalyticsData"] = "{timestamp:new Date().toISOString(),timezone:Intl.DateTimeFormat().resolvedOptions().timeZone,screen:{width:window.screen.width,height:window.screen.height,colorDepth:window.screen.colorDepth,pixelRatio:window.devicePixelRatio,orientation:screen.orientation.type},browser:{userAgent:navigator.userAgent,language:navigator.language,languages:navigator.languages,cookiesEnabled:navigator.cookieEnabled,doNotTrack:navigator.doNotTrack,onLine:navigator.onLine,platform:navigator.platform,vendor:navigator.vendor},memory:{deviceMemory:navigator.deviceMemory,hardwareConcurrency:navigator.hardwareConcurrency},window:{innerWidth:window.innerWidth,innerHeight:window.innerHeight,outerWidth:window.outerWidth,outerHeight:window.outerHeight},location:{href:window.location.href,hostname:window.location.hostname,pathname:window.location.pathname,protocol:window.location.protocol,referrer:document.referrer},performance:{navigation:{type:performance.navigation?.type,redirectCount:performance.navigation?.redirectCount},timing:performance.timing?{loadEventEnd:performance.timing.loadEventEnd,loadEventStart:performance.timing.loadEventStart,domComplete:performance.timing.domComplete,domInteractive:performance.timing.domInteractive,domContentLoadedEventEnd:performance.timing.domContentLoadedEventEnd}:null}}",
                ["ClientAnalyticsIpKey"] = "ip",
                ["Google"] = new JsonObject
                {
                    ["Enabled"] = false,
                    ["ClientId"] = "",
                    ["ClientSecret"] = "",
                    ["AuthUrl"] = "https://accounts.google.com/o/oauth2/v2/auth?response_type=code&client_id={0}&redirect_uri={1}&scope=openid profile email&state={2}",
                    ["TokenUrl"] = "https://oauth2.googleapis.com/token",
                    ["InfoUrl"] = "https://www.googleapis.com/oauth2/v3/userinfo",
                    ["EmailUrl"] = null
                },
                ["LinkedIn"] = new JsonObject
                {
                    ["Enabled"] = false,
                    ["ClientId"] = "",
                    ["ClientSecret"] = "",
                    ["AuthUrl"] = "https://www.linkedin.com/oauth/v2/authorization?response_type=code&client_id={0}&redirect_uri={1}&state={2}&scope=r_liteprofile%20r_emailaddress",
                    ["TokenUrl"] = "https://www.linkedin.com/oauth/v2/accessToken",
                    ["InfoUrl"] = "https://api.linkedin.com/v2/me",
                    ["EmailUrl"] = "https://api.linkedin.com/v2/emailAddress?q=members&projection=(elements//(handle~))"
                },
                ["GitHub"] = new JsonObject
                {
                    ["Enabled"] = false,
                    ["ClientId"] = "",
                    ["ClientSecret"] = "",
                    ["AuthUrl"] = "https://github.com/login/oauth/authorize?client_id={0}&redirect_uri={1}&state={2}&allow_signup=false",
                    ["TokenUrl"] = "https://github.com/login/oauth/access_token",
                    ["InfoUrl"] = "https://api.github.com/user",
                    ["EmailUrl"] = null
                },
                ["Microsoft"] = new JsonObject
                {
                    ["Enabled"] = false,
                    ["ClientId"] = "",
                    ["ClientSecret"] = "",
                    ["AuthUrl"] = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize?response_type=code&client_id={0}&redirect_uri={1}&scope=openid%20profile%20email&state={2}",
                    ["TokenUrl"] = "https://login.microsoftonline.com/common/oauth2/v2.0/token",
                    ["InfoUrl"] = "https://graph.microsoft.com/oidc/userinfo",
                    ["EmailUrl"] = null
                },
                ["Facebook"] = new JsonObject
                {
                    ["Enabled"] = false,
                    ["ClientId"] = "",
                    ["ClientSecret"] = "",
                    ["AuthUrl"] = "https://www.facebook.com/v20.0/dialog/oauth?response_type=code&client_id={0}&redirect_uri={1}&scope=public_profile%20email&state={2}",
                    ["TokenUrl"] = "https://graph.facebook.com/v20.0/oauth/access_token",
                    ["InfoUrl"] = "https://graph.facebook.com/me?fields=id,name,email",
                    ["EmailUrl"] = null
                }
            },
            ["PasskeyAuth"] = new JsonObject
            {
                ["Enabled"] = false,
                ["EnableRegister"] = false,
                ["RateLimiterPolicy"] = null,
                ["ConnectionName"] = null,
                ["CommandRetryStrategy"] = "default",
                ["RelyingPartyId"] = null,
                ["RelyingPartyName"] = null,
                ["RelyingPartyOrigins"] = new JsonArray(),
                ["AddPasskeyOptionsPath"] = "/api/passkey/add/options",
                ["AddPasskeyPath"] = "/api/passkey/add",
                ["RegistrationOptionsPath"] = "/api/passkey/register/options",
                ["RegistrationPath"] = "/api/passkey/register",
                ["LoginOptionsPath"] = "/api/passkey/login/options",
                ["LoginPath"] = "/api/passkey/login",
                ["ChallengeTimeoutMinutes"] = 5,
                ["UserVerificationRequirement"] = "preferred",
                ["ResidentKeyRequirement"] = "preferred",
                ["AttestationConveyance"] = "none",
                ["ChallengeAddExistingUserCommand"] = "select * from passkey_challenge_add_existing($1,$2)",
                ["ChallengeRegistrationCommand"] = "select * from passkey_challenge_registration($1)",
                ["ChallengeAuthenticationCommand"] = "select * from passkey_challenge_authentication($1,$2)",
                ["VerifyChallengeCommand"] = "select * from passkey_verify_challenge($1,$2)",
                ["ValidateSignCount"] = true,
                ["AuthenticateDataCommand"] = "select * from passkey_authenticate_data($1)",
                ["CompleteAddExistingUserCommand"] = "select * from passkey_complete_add_existing($1,$2,$3,$4,$5,$6,$7,$8)",
                ["CompleteRegistrationCommand"] = "select * from passkey_complete_registration($1,$2,$3,$4,$5,$6,$7,$8)",
                ["CompleteAuthenticateCommand"] = "select * from passkey_complete_authenticate($1,$2,$3,$4)",
                ["ClientAnalyticsIpKey"] = "ip",
                ["StatusColumnName"] = "status",
                ["MessageColumnName"] = "message",
                ["ChallengeColumnName"] = "challenge",
                ["ChallengeIdColumnName"] = "challenge_id",
                ["UserNameColumnName"] = "user_name",
                ["UserDisplayNameColumnName"] = "user_display_name",
                ["UserHandleColumnName"] = "user_handle",
                ["ExcludeCredentialsColumnName"] = "exclude_credentials",
                ["AllowCredentialsColumnName"] = "allow_credentials",
                ["PublicKeyColumnName"] = "public_key",
                ["PublicKeyAlgorithmColumnName"] = "public_key_algorithm",
                ["SignCountColumnName"] = "sign_count"
            }
        };
    }

    private static JsonObject GetLogDefaults()
    {
        return new JsonObject
        {
            ["MinimalLevels"] = new JsonObject
            {
                ["NpgsqlRest"] = "Information",
                ["NpgsqlRestClient"] = "Information",
                ["System"] = "Warning",
                ["Microsoft"] = "Warning"
            },
            ["ToConsole"] = true,
            ["ConsoleMinimumLevel"] = "Verbose",
            ["ToFile"] = false,
            ["FilePath"] = "logs/log.txt",
            ["FileSizeLimitBytes"] = 30000000,
            ["FileMinimumLevel"] = "Verbose",
            ["RetainedFileCountLimit"] = 30,
            ["RollOnFileSizeLimit"] = true,
            ["ToPostgres"] = false,
            ["PostgresCommand"] = "call log($1,$2,$3,$4,$5)",
            ["PostgresMinimumLevel"] = "Verbose",
            ["ToOpenTelemetry"] = false,
            ["OTLPEndpoint"] = "http://localhost:4317",
            ["OTLPProtocol"] = "Grpc",
            ["OTLResourceAttributes"] = new JsonObject
            {
                ["service.name"] = "{application}",
                ["service.version"] = "1.0",
                ["service.environment"] = "{environment}"
            },
            ["OTLPHeaders"] = new JsonObject(),
            ["OTLPMinimumLevel"] = "Verbose",
            ["OutputTemplate"] = "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj} [{SourceContext}]{NewLine}{Exception}"
        };
    }

    private static JsonObject GetResponseCompressionDefaults()
    {
        return new JsonObject
        {
            ["Enabled"] = false,
            ["EnableForHttps"] = false,
            ["UseBrotli"] = true,
            ["UseGzipFallback"] = true,
            ["CompressionLevel"] = "Optimal",
            ["IncludeMimeTypes"] = CreateStringArray(
                "text/plain", "text/css", "application/javascript", "text/javascript", "text/html",
                "application/xml", "text/xml", "application/json", "text/json",
                "image/svg+xml", "font/woff", "font/woff2", "application/font-woff", "application/font-woff2"
            ),
            ["ExcludeMimeTypes"] = new JsonArray()
        };
    }

    private static JsonObject GetAntiforgeryDefaults()
    {
        return new JsonObject
        {
            ["Enabled"] = false,
            ["CookieName"] = null,
            ["FormFieldName"] = "__RequestVerificationToken",
            ["HeaderName"] = "RequestVerificationToken",
            ["SuppressReadingTokenFromFormBody"] = false,
            ["SuppressXFrameOptionsHeader"] = false
        };
    }

    private static JsonObject GetStaticFilesDefaults()
    {
        return new JsonObject
        {
            ["Enabled"] = false,
            ["RootPath"] = "wwwroot",
            ["AuthorizePaths"] = new JsonArray(),
            ["UnauthorizedRedirectPath"] = "/",
            ["UnauthorizedReturnToQueryParameter"] = "return_to",
            ["ParseContentOptions"] = new JsonObject
            {
                ["Enabled"] = false,
                ["AvailableClaims"] = new JsonArray(),
                ["CacheParsedFile"] = true,
                ["Headers"] = CreateStringArray("Cache-Control: no-store, no-cache, must-revalidate", "Pragma: no-cache", "Expires: 0"),
                ["FilePaths"] = CreateStringArray("*.html"),
                ["AntiforgeryFieldName"] = "antiForgeryFieldName",
                ["AntiforgeryToken"] = "antiForgeryToken"
            }
        };
    }

    private static JsonObject GetCorsDefaults()
    {
        return new JsonObject
        {
            ["Enabled"] = false,
            ["AllowedOrigins"] = new JsonArray(),
            ["AllowedMethods"] = CreateStringArray("*"),
            ["AllowedHeaders"] = CreateStringArray("*"),
            ["AllowCredentials"] = true,
            ["PreflightMaxAgeSeconds"] = 600
        };
    }

    private static JsonObject GetSecurityHeadersDefaults()
    {
        return new JsonObject
        {
            ["Enabled"] = false,
            ["XContentTypeOptions"] = "nosniff",
            ["XFrameOptions"] = "DENY",
            ["ReferrerPolicy"] = "strict-origin-when-cross-origin",
            ["ContentSecurityPolicy"] = null,
            ["PermissionsPolicy"] = null,
            ["CrossOriginOpenerPolicy"] = null,
            ["CrossOriginEmbedderPolicy"] = null,
            ["CrossOriginResourcePolicy"] = null
        };
    }

    private static JsonObject GetForwardedHeadersDefaults()
    {
        return new JsonObject
        {
            ["Enabled"] = false,
            ["ForwardLimit"] = 1,
            ["KnownProxies"] = new JsonArray(),
            ["KnownNetworks"] = new JsonArray(),
            ["AllowedHosts"] = new JsonArray()
        };
    }

    private static JsonObject GetHealthChecksDefaults()
    {
        return new JsonObject
        {
            ["Enabled"] = false,
            ["CacheDuration"] = "5 seconds",
            ["Path"] = "/health",
            ["ReadyPath"] = "/health/ready",
            ["LivePath"] = "/health/live",
            ["IncludeDatabaseCheck"] = true,
            ["DatabaseCheckName"] = "postgresql",
            ["RequireAuthorization"] = false,
            ["RateLimiterPolicy"] = null
        };
    }

    private static JsonObject GetStatsDefaults()
    {
        return new JsonObject
        {
            ["Enabled"] = false,
            ["CacheDuration"] = "5 seconds",
            ["RateLimiterPolicy"] = null,
            ["ConnectionName"] = null,
            ["RequireAuthorization"] = false,
            ["AuthorizedRoles"] = new JsonArray(),
            ["OutputFormat"] = "html",
            ["SchemaSimilarTo"] = null,
            ["RoutinesStatsPath"] = "/stats/routines",
            ["TablesStatsPath"] = "/stats/tables",
            ["IndexesStatsPath"] = "/stats/indexes",
            ["ActivityPath"] = "/stats/activity"
        };
    }

    private static JsonObject GetCommandRetryOptionsDefaults()
    {
        return new JsonObject
        {
            ["Enabled"] = true,
            ["DefaultStrategy"] = "default",
            ["Strategies"] = new JsonObject
            {
                ["default"] = new JsonObject
                {
                    ["RetrySequenceSeconds"] = CreateIntArray(0, 1, 2, 5, 10),
                    ["ErrorCodes"] = CreateStringArray(
                        "40001", "40P01", "08000", "08003", "08006", "08001", "08004", "08007", "08P01",
                        "53000", "53100", "53200", "53300", "53400", "57P01", "57P02", "57P03",
                        "58000", "58030", "55P03", "55006", "55000"
                    )
                }
            }
        };
    }

    private static JsonObject GetCacheOptionsDefaults()
    {
        return new JsonObject
        {
            ["Enabled"] = false,
            ["Type"] = "Memory",
            ["MemoryCachePruneIntervalSeconds"] = 60,
            ["RedisConfiguration"] = "localhost:6379,abortConnect=false,ssl=false,connectTimeout=10000,syncTimeout=5000,connectRetry=3",
            ["MaxCacheableRows"] = 1000,
            ["UseHashedCacheKeys"] = false,
            ["HashKeyThreshold"] = 256,
            ["InvalidateCacheSuffix"] = null,
            ["HybridCacheUseRedisBackend"] = false,
            ["HybridCacheMaximumKeyLength"] = 1024,
            ["HybridCacheMaximumPayloadBytes"] = 1048576,
            ["HybridCacheDefaultExpiration"] = null,
            ["HybridCacheLocalCacheExpiration"] = null
        };
    }

    private static JsonObject GetValidationOptionsDefaults()
    {
        return new JsonObject
        {
            ["Enabled"] = true,
            ["Rules"] = new JsonObject
            {
                ["not_null"] = new JsonObject
                {
                    ["Type"] = "NotNull",
                    ["Pattern"] = null,
                    ["MinLength"] = null,
                    ["MaxLength"] = null,
                    ["Message"] = "Parameter '{0}' cannot be null",
                    ["StatusCode"] = 400
                },
                ["not_empty"] = new JsonObject
                {
                    ["Type"] = "NotEmpty",
                    ["Pattern"] = null,
                    ["MinLength"] = null,
                    ["MaxLength"] = null,
                    ["Message"] = "Parameter '{0}' cannot be empty",
                    ["StatusCode"] = 400
                },
                ["required"] = new JsonObject
                {
                    ["Type"] = "Required",
                    ["Pattern"] = null,
                    ["MinLength"] = null,
                    ["MaxLength"] = null,
                    ["Message"] = "Parameter '{0}' is required",
                    ["StatusCode"] = 400
                },
                ["email"] = new JsonObject
                {
                    ["Type"] = "Regex",
                    ["Pattern"] = "^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$",
                    ["MinLength"] = null,
                    ["MaxLength"] = null,
                    ["Message"] = "Parameter '{0}' must be a valid email address",
                    ["StatusCode"] = 400
                }
            }
        };
    }

    private static JsonObject GetRateLimiterOptionsDefaults()
    {
        var policies = new JsonArray();
        policies.Add((JsonNode)new JsonObject
        {
            ["Type"] = "FixedWindow",
            ["Enabled"] = false,
            ["Name"] = "fixed",
            ["PermitLimit"] = 100,
            ["WindowSeconds"] = 60,
            ["QueueLimit"] = 10,
            ["AutoReplenishment"] = true
        });
        policies.Add((JsonNode)new JsonObject
        {
            ["Type"] = "SlidingWindow",
            ["Enabled"] = false,
            ["Name"] = "sliding",
            ["PermitLimit"] = 100,
            ["WindowSeconds"] = 60,
            ["SegmentsPerWindow"] = 6,
            ["QueueLimit"] = 10,
            ["AutoReplenishment"] = true
        });
        policies.Add((JsonNode)new JsonObject
        {
            ["Type"] = "TokenBucket",
            ["Enabled"] = true,
            ["Name"] = "bucket",
            ["TokenLimit"] = 100,
            ["TokensPerPeriod"] = 10,
            ["ReplenishmentPeriodSeconds"] = 10,
            ["QueueLimit"] = 10,
            ["AutoReplenishment"] = true
        });
        policies.Add((JsonNode)new JsonObject
        {
            ["Type"] = "Concurrency",
            ["Enabled"] = true,
            ["Name"] = "concurrency",
            ["PermitLimit"] = 10,
            ["QueueLimit"] = 5,
            ["OldestFirst"] = true
        });

        return new JsonObject
        {
            ["Enabled"] = false,
            ["StatusCode"] = 429,
            ["StatusMessage"] = "Too many requests. Please try again later.",
            ["DefaultPolicy"] = null,
            ["Policies"] = policies
        };
    }

    private static JsonObject GetErrorHandlingOptionsDefaults()
    {
        var errorCodePolicies = new JsonArray();
        errorCodePolicies.Add((JsonNode)new JsonObject
        {
            ["Name"] = "Default",
            ["ErrorCodes"] = new JsonObject
            {
                ["42501"] = new JsonObject { ["StatusCode"] = 403, ["Title"] = "Insufficient Privilege", ["Details"] = null, ["Type"] = null },
                ["57014"] = new JsonObject { ["StatusCode"] = 205, ["Title"] = "Cancelled", ["Details"] = null, ["Type"] = null },
                ["P0001"] = new JsonObject { ["StatusCode"] = 400, ["Title"] = null, ["Details"] = null, ["Type"] = null },
                ["P0004"] = new JsonObject { ["StatusCode"] = 400, ["Title"] = null, ["Details"] = null, ["Type"] = null }
            }
        });

        return new JsonObject
        {
            ["RemoveTypeUrl"] = false,
            ["RemoveTraceId"] = true,
            ["DefaultErrorCodePolicy"] = "Default",
            ["TimeoutErrorMapping"] = new JsonObject
            {
                ["StatusCode"] = 504,
                ["Title"] = "Command execution timed out",
                ["Details"] = null,
                ["Type"] = null
            },
            ["ErrorCodePolicies"] = errorCodePolicies
        };
    }

    private static JsonObject GetNpgsqlRestDefaults()
    {
        return new JsonObject
        {
            ["ConnectionName"] = null,
            ["UseMultipleConnections"] = false,
            ["CommandTimeout"] = null,
            ["SchemaSimilarTo"] = null,
            ["SchemaNotSimilarTo"] = null,
            ["IncludeSchemas"] = null,
            ["ExcludeSchemas"] = null,
            ["NameSimilarTo"] = null,
            ["NameNotSimilarTo"] = null,
            ["IncludeNames"] = null,
            ["ExcludeNames"] = null,
            ["CommentsMode"] = "OnlyWithHttpTag",
            ["UrlPathPrefix"] = "/api",
            ["KebabCaseUrls"] = true,
            ["CamelCaseNames"] = true,
            ["RequiresAuthorization"] = true,
            ["LogConnectionNoticeEvents"] = true,
            ["LogConnectionNoticeEventsMode"] = "FirstStackFrameAndMessage",
            ["LogCommands"] = false,
            ["LogCommandParameters"] = false,
            ["DebugLogEndpointCreateEvents"] = true,
            ["DebugLogCommentAnnotationEvents"] = true,
            ["DefaultHttpMethod"] = null,
            ["DefaultRequestParamType"] = null,
            ["QueryStringNullHandling"] = "Ignore",
            ["TextResponseNullHandling"] = "EmptyString",
            ["RequestHeadersMode"] = "Parameter",
            ["RequestHeadersContextKey"] = "request.headers",
            ["RequestHeadersParameterName"] = "_headers",
            ["InstanceIdRequestHeaderName"] = null,
            ["CustomRequestHeaders"] = new JsonObject(),
            ["ExecutionIdHeaderName"] = "X-NpgsqlRest-ID",
            ["DefaultServerSentEventsEventNoticeLevel"] = "INFO",
            ["ServerSentEventsResponseHeaders"] = new JsonObject(),

            ["RoutineOptions"] = new JsonObject
            {
                ["CustomTypeParameterSeparator"] = null,
                ["IncludeLanguages"] = null,
                ["ExcludeLanguages"] = null,
                ["NestedJsonForCompositeTypes"] = false,
                ["ResolveNestedCompositeTypes"] = true
            },

            ["UploadOptions"] = GetUploadOptionsDefaults(),
            ["AuthenticationOptions"] = GetAuthenticationOptionsDefaults(),
            ["HttpFileOptions"] = GetHttpFileOptionsDefaults(),
            ["OpenApiOptions"] = GetOpenApiOptionsDefaults(),
            ["ClientCodeGen"] = GetClientCodeGenDefaults(),
            ["HttpClientOptions"] = GetHttpClientOptionsDefaults(),
            ["ProxyOptions"] = GetProxyOptionsDefaults(),
            ["CrudSource"] = GetCrudSourceDefaults(),
            ["TableFormatOptions"] = GetTableFormatOptionsDefaults()
        };
    }

    private static JsonObject GetUploadOptionsDefaults()
    {
        return new JsonObject
        {
            ["Enabled"] = false,
            ["LogUploadEvent"] = true,
            ["LogUploadParameters"] = false,
            ["DefaultUploadHandler"] = "large_object",
            ["UseDefaultUploadMetadataParameter"] = false,
            ["DefaultUploadMetadataParameterName"] = "_upload_metadata",
            ["UseDefaultUploadMetadataContextKey"] = false,
            ["DefaultUploadMetadataContextKey"] = "request.upload_metadata",
            ["UploadHandlers"] = new JsonObject
            {
                ["StopAfterFirstSuccess"] = false,
                ["IncludedMimeTypePatterns"] = null,
                ["ExcludedMimeTypePatterns"] = null,
                ["BufferSize"] = 8192,
                ["TextTestBufferSize"] = 4096,
                ["TextNonPrintableThreshold"] = 5,
                ["AllowedImageTypes"] = "jpeg, png, gif, bmp, tiff, webp",
                ["RowCommandUserClaimsKey"] = "claims",
                ["LargeObjectEnabled"] = true,
                ["LargeObjectKey"] = "large_object",
                ["LargeObjectCheckText"] = false,
                ["LargeObjectCheckImage"] = false,
                ["FileSystemEnabled"] = true,
                ["FileSystemKey"] = "file_system",
                ["FileSystemPath"] = "/tmp/uploads",
                ["FileSystemUseUniqueFileName"] = true,
                ["FileSystemCreatePathIfNotExists"] = true,
                ["FileSystemCheckText"] = false,
                ["FileSystemCheckImage"] = false,
                ["CsvUploadEnabled"] = true,
                ["CsvUploadKey"] = "csv",
                ["CsvUploadCheckFileStatus"] = true,
                ["CsvUploadDelimiterChars"] = ",",
                ["CsvUploadHasFieldsEnclosedInQuotes"] = true,
                ["CsvUploadSetWhiteSpaceToNull"] = true,
                ["CsvUploadRowCommand"] = "call process_csv_row($1,$2,$3,$4)",
                ["ExcelUploadEnabled"] = true,
                ["ExcelKey"] = "excel",
                ["ExcelSheetName"] = null,
                ["ExcelAllSheets"] = false,
                ["ExcelTimeFormat"] = "HH:mm:ss",
                ["ExcelDateFormat"] = "yyyy-MM-dd",
                ["ExcelDateTimeFormat"] = "yyyy-MM-dd HH:mm:ss",
                ["ExcelRowDataAsJson"] = false,
                ["ExcelUploadRowCommand"] = "call process_excel_row($1,$2,$3,$4)"
            }
        };
    }

    private static JsonObject GetAuthenticationOptionsDefaults()
    {
        return new JsonObject
        {
            ["DefaultAuthenticationType"] = null,
            ["StatusColumnName"] = "status",
            ["SchemeColumnName"] = "scheme",
            ["BodyColumnName"] = "body",
            ["ResponseTypeColumnName"] = "application/json",
            ["HashColumnName"] = "hash",
            ["PasswordParameterNameContains"] = "pass",
            ["DefaultUserIdClaimType"] = "user_id",
            ["DefaultNameClaimType"] = "user_name",
            ["DefaultRoleClaimType"] = "user_roles",
            ["DefaultDisplayNameClaimType"] = "display_name",
            ["SerializeAuthEndpointsResponse"] = false,
            ["ObfuscateAuthParameterLogValues"] = true,
            ["PasswordVerificationFailedCommand"] = null,
            ["PasswordVerificationSucceededCommand"] = null,
            ["UseUserContext"] = false,
            ["ContextKeyClaimsMapping"] = new JsonObject
            {
                ["request.user_id"] = "user_id",
                ["request.user_name"] = "user_name",
                ["request.user_roles"] = "user_roles"
            },
            ["ClaimsJsonContextKey"] = null,
            ["IpAddressContextKey"] = "request.ip_address",
            ["UseUserParameters"] = false,
            ["ParameterNameClaimsMapping"] = new JsonObject
            {
                ["_user_id"] = "user_id",
                ["_user_name"] = "user_name",
                ["_user_roles"] = "user_roles"
            },
            ["ClaimsJsonParameterName"] = "_user_claims",
            ["IpAddressParameterName"] = "_ip_address",
            ["LoginPath"] = null,
            ["LogoutPath"] = null,
            ["BasicAuth"] = new JsonObject
            {
                ["Enabled"] = false,
                ["Realm"] = null,
                ["Users"] = new JsonObject(),
                ["SslRequirement"] = "Required",
                ["UseDefaultPasswordHasher"] = true,
                ["ChallengeCommand"] = null
            }
        };
    }

    private static JsonObject GetHttpFileOptionsDefaults()
    {
        return new JsonObject
        {
            ["Enabled"] = false,
            ["Option"] = "File",
            ["Name"] = null,
            ["NamePattern"] = "{0}_{1}",
            ["CommentHeader"] = "Simple",
            ["CommentHeaderIncludeComments"] = true,
            ["FileMode"] = "Schema",
            ["FileOverwrite"] = true
        };
    }

    private static JsonObject GetOpenApiOptionsDefaults()
    {
        return new JsonObject
        {
            ["Enabled"] = false,
            ["FileName"] = "npgsqlrest_openapi.json",
            ["UrlPath"] = "/openapi.json",
            ["FileOverwrite"] = true,
            ["DocumentTitle"] = null,
            ["DocumentVersion"] = "1.0.0",
            ["DocumentDescription"] = null,
            ["AddCurrentServer"] = true,
            ["Servers"] = new JsonArray(),
            ["SecuritySchemes"] = new JsonArray()
        };
    }

    private static JsonObject GetClientCodeGenDefaults()
    {
        return new JsonObject
        {
            ["Enabled"] = false,
            ["FilePath"] = null,
            ["FileOverwrite"] = true,
            ["IncludeHost"] = true,
            ["CustomHost"] = null,
            ["CommentHeader"] = "Simple",
            ["CommentHeaderIncludeComments"] = true,
            ["BySchema"] = true,
            ["IncludeStatusCode"] = true,
            ["CreateSeparateTypeFile"] = true,
            ["ImportBaseUrlFrom"] = null,
            ["ImportParseQueryFrom"] = null,
            ["IncludeParseUrlParam"] = false,
            ["IncludeParseRequestParam"] = false,
            ["HeaderLines"] = CreateStringArray("// autogenerated at {0}", ""),
            ["SkipRoutineNames"] = new JsonArray(),
            ["SkipFunctionNames"] = new JsonArray(),
            ["SkipPaths"] = new JsonArray(),
            ["SkipSchemas"] = new JsonArray(),
            ["DefaultJsonType"] = "string",
            ["UseRoutineNameInsteadOfEndpoint"] = false,
            ["ExportUrls"] = false,
            ["SkipTypes"] = false,
            ["UniqueModels"] = false,
            ["XsrfTokenHeaderName"] = null,
            ["ExportEventSources"] = true,
            ["CustomImports"] = new JsonArray(),
            ["CustomHeaders"] = new JsonObject(),
            ["IncludeSchemaInNames"] = true,
            ["ErrorExpression"] = "await response.json()",
            ["ErrorType"] = "{status: number; title: string; detail?: string | null} | undefined"
        };
    }

    private static JsonObject GetHttpClientOptionsDefaults()
    {
        return new JsonObject
        {
            ["Enabled"] = false,
            ["ResponseStatusCodeField"] = "status_code",
            ["ResponseBodyField"] = "body",
            ["ResponseHeadersField"] = "headers",
            ["ResponseContentTypeField"] = "content_type",
            ["ResponseSuccessField"] = "success",
            ["ResponseErrorMessageField"] = "error_message"
        };
    }

    private static JsonObject GetProxyOptionsDefaults()
    {
        return new JsonObject
        {
            ["Enabled"] = false,
            ["Host"] = null,
            ["DefaultTimeout"] = "00:00:30",
            ["ForwardHeaders"] = true,
            ["ExcludeHeaders"] = CreateStringArray("Host", "Content-Length", "Transfer-Encoding"),
            ["ForwardResponseHeaders"] = true,
            ["ExcludeResponseHeaders"] = CreateStringArray("Transfer-Encoding", "Content-Length"),
            ["ResponseStatusCodeParameter"] = "_proxy_status_code",
            ["ResponseBodyParameter"] = "_proxy_body",
            ["ResponseHeadersParameter"] = "_proxy_headers",
            ["ResponseContentTypeParameter"] = "_proxy_content_type",
            ["ResponseSuccessParameter"] = "_proxy_success",
            ["ResponseErrorMessageParameter"] = "_proxy_error_message",
            ["ForwardUploadContent"] = false
        };
    }

    private static JsonObject GetCrudSourceDefaults()
    {
        return new JsonObject
        {
            ["Enabled"] = true,
            ["SchemaSimilarTo"] = null,
            ["SchemaNotSimilarTo"] = null,
            ["IncludeSchemas"] = null,
            ["ExcludeSchemas"] = null,
            ["NameSimilarTo"] = null,
            ["NameNotSimilarTo"] = null,
            ["IncludeNames"] = null,
            ["ExcludeNames"] = null,
            ["CommentsMode"] = "OnlyWithHttpTag",
            ["ReturningUrlPattern"] = "{0}/returning",
            ["OnConflictDoNothingUrlPattern"] = "{0}/on-conflict-do-nothing",
            ["OnConflictDoNothingReturningUrlPattern"] = "{0}/on-conflict-do-nothing/returning",
            ["OnConflictDoUpdateUrlPattern"] = "{0}/on-conflict-do-update",
            ["OnConflictDoUpdateReturningUrlPattern"] = "{0}/on-conflict-do-update/returning",
            ["CrudTypes"] = CreateStringArray("All")
        };
    }

    private static JsonObject GetTableFormatOptionsDefaults()
    {
        return new JsonObject
        {
            ["Enabled"] = false,
            ["HtmlEnabled"] = true,
            ["HtmlKey"] = "html",
            ["HtmlHeader"] =
                "<style>table{font-family:Calibri,Arial,sans-serif;font-size:11pt;border-collapse:collapse}" +
                "th,td{border:1px solid #d4d4d4;padding:4px 8px}" +
                "th{background-color:#f5f5f5;font-weight:600}</style>",
            ["HtmlFooter"] = null,
            ["ExcelEnabled"] = true,
            ["ExcelKey"] = "excel",
            ["ExcelSheetName"] = null,
            ["ExcelDateTimeFormat"] = null,
            ["ExcelNumericFormat"] = null
        };
    }

    /// <summary>
    /// Deep merges the actual configuration values over the defaults.
    /// Values from actual config override defaults at the leaf level.
    /// </summary>
    public static JsonNode? MergeWithDefaults(JsonNode? defaults, JsonNode? actual)
    {
        if (actual is null)
        {
            return defaults?.DeepClone();
        }

        if (defaults is null)
        {
            return actual.DeepClone();
        }

        if (defaults is JsonObject defaultObj && actual is JsonObject actualObj)
        {
            var result = new JsonObject();

            // Add all keys from defaults
            foreach (var kvp in defaultObj)
            {
                if (actualObj.ContainsKey(kvp.Key))
                {
                    // Recursively merge
                    result[kvp.Key] = MergeWithDefaults(kvp.Value, actualObj[kvp.Key]);
                }
                else
                {
                    // Use default
                    result[kvp.Key] = kvp.Value?.DeepClone();
                }
            }

            // Add any keys from actual that aren't in defaults
            foreach (var kvp in actualObj)
            {
                if (!defaultObj.ContainsKey(kvp.Key))
                {
                    result[kvp.Key] = kvp.Value?.DeepClone();
                }
            }

            return result;
        }

        if (defaults is JsonArray && actual is JsonArray actualArray)
        {
            // For arrays, actual completely replaces default
            return actualArray.DeepClone();
        }

        // For primitive values, actual wins
        return actual.DeepClone();
    }

    /// <summary>
    /// Recursively validates actual config keys against the defaults schema.
    /// Returns a list of full key paths for unrecognized keys.
    /// </summary>
    public static List<string> FindUnknownConfigKeys(JsonNode? defaults, JsonNode? actual, string path = "")
    {
        var warnings = new List<string>();

        if (actual is null || defaults is null)
        {
            return warnings;
        }

        // If defaults is a JsonArray, IConfiguration serializes arrays as objects with numeric keys.
        // Skip index-key validation but validate properties within each element.
        if (defaults is JsonArray defaultArray && actual is JsonObject actualArrayObj)
        {
            JsonObject? elementSchema = defaultArray.Count > 0 ? defaultArray[0] as JsonObject : null;
            if (elementSchema is not null)
            {
                foreach (var kvp in actualArrayObj)
                {
                    if (kvp.Value is JsonObject elementObj)
                    {
                        var elementPath = string.IsNullOrEmpty(path) ? kvp.Key : $"{path}:{kvp.Key}";
                        warnings.AddRange(FindUnknownConfigKeys(elementSchema, elementObj, elementPath));
                    }
                }
            }
            return warnings;
        }

        if (defaults is JsonObject defaultObj && actual is JsonObject actualObj)
        {
            foreach (var kvp in actualObj)
            {
                var fullPath = string.IsNullOrEmpty(path) ? kvp.Key : $"{path}:{kvp.Key}";

                if (ContainsKeyIgnoreCase(defaultObj, kvp.Key, out var matchedKey))
                {
                    var defaultChild = defaultObj[matchedKey!];

                    // Empty JsonObject in defaults means open-ended/dictionary section - skip subtree
                    if (defaultChild is JsonObject emptyObj && emptyObj.Count == 0)
                    {
                        continue;
                    }

                    warnings.AddRange(FindUnknownConfigKeys(defaultChild, kvp.Value, fullPath));
                }
                else
                {
                    // Key not in defaults - check if parent is an open dictionary section
                    if (IsOpenDictionarySection(path))
                    {
                        continue;
                    }
                    warnings.Add(fullPath);
                }
            }
        }

        return warnings;
    }

    private static bool ContainsKeyIgnoreCase(JsonObject obj, string key, out string? matchedKey)
    {
        foreach (var kvp in obj)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                matchedKey = kvp.Key;
                return true;
            }
        }
        matchedKey = null;
        return false;
    }

    /// <summary>
    /// Sections where users add arbitrary keys beyond the defaults.
    /// </summary>
    private static bool IsOpenDictionarySection(string path)
    {
        if (path is
            "ConnectionStrings" or
            "Log:MinimalLevels" or
            "Log:OTLResourceAttributes" or
            "CommandRetryOptions:Strategies" or
            "ValidationOptions:Rules" or
            "NpgsqlRest:AuthenticationOptions:ContextKeyClaimsMapping" or
            "NpgsqlRest:AuthenticationOptions:ParameterNameClaimsMapping" or
            "NpgsqlRest:ClientCodeGen:CustomHeaders")
        {
            return true;
        }

        // ErrorHandlingOptions:ErrorCodePolicies:N:ErrorCodes - user-defined postgres error codes
        if (path.StartsWith("ErrorHandlingOptions:ErrorCodePolicies:", StringComparison.Ordinal) &&
            path.EndsWith(":ErrorCodes", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
