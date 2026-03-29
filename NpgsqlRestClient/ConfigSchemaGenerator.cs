using System.Text.Json.Nodes;

namespace NpgsqlRestClient;

/// <summary>
/// Generates a JSON Schema (draft-07) describing the appsettings.json configuration structure.
/// Walks ConfigDefaults.GetDefaults() to infer types, defaults, and enum constraints.
/// </summary>
public static partial class ConfigSchemaGenerator
{
    /// <summary>
    /// Configuration key descriptions extracted from appsettings.json comments.
    /// Keys use colon-separated paths matching the config structure.
    /// </summary>
    internal static readonly Dictionary<string, string> DescriptionFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ApplicationName"] = "The application name used to set the application name property in connection string by \"NpgsqlRest.SetApplicationNameInConnection\" or the \"NpgsqlRest.UseJsonApplicationName\" settings.\nIt is the name of the top-level directory if set to null.",
        ["EnvironmentName"] = "Production or Development",
        ["Urls"] = "Specify the urls the web host will listen on. See https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.hosting.hostingabstractionswebhostbuilderextensions.useurls?view=aspnetcore-8.0",
        ["StartupMessage"] = "Logs at startup, format placeholders:\n{time} - startup time\n{urls} - listening on urls\n{version} - current version\n{environment} - EnvironmentName\n{application} - ApplicationName\n\nNote: This message is logged at Information level. To disable this message, set to empty string.",
        ["Config"] = "Configuration settings",
        ["Config:AddEnvironmentVariables"] = "Add the environment variables to configuration.\nWhen enabled, environment variables will override the settings in this configuration file but can be overridden by command line arguments.\nComplex hierarchical keys can be defined using double underscore as a separator.\nFor example, \"ConnectionStrings__Default\" environment variable will override the \"ConnectionStrings.Default\" setting in this configuration file.",
        ["Config:ParseEnvironmentVariables"] = "When set, configuration values will be parsed for environment variables in the format {ENV_VAR_NAME}\nand replaced with the value of the environment variable when available.",
        ["Config:EnvFile"] = "Path to a .env file containing environment variables.\nWhen AddEnvironmentVariables or ParseEnvironmentVariables is true and this file exists,\nvariables from this file will be loaded and made available for configuration parsing.\nFormat: KEY=VALUE (one per line)",
        ["Config:ValidateConfigKeys"] = "Validate configuration keys against known defaults at startup.\n\"Ignore\" - no validation\n\"Warning\" - log warnings for unknown keys, continue startup (default)\n\"Error\" - log errors for unknown keys and exit",
        ["ConnectionStrings"] = "List of named connection strings to PostgreSQL databases.\nThe \"Default\" connection string is used when no connection name is specified.\nFor connection string definition see https://www.npgsql.org/doc/connection-string-parameters.html",
        ["ConnectionSettings"] = "Additional connection settings and options.",
        ["ConnectionSettings:SetApplicationNameInConnection"] = "Sets the ApplicationName connection property in the connection string to the value of the ApplicationName configuration.\nNote: This option is ignored if the UseJsonApplicationName option is enabled.",
        ["ConnectionSettings:UseJsonApplicationName"] = "Sets the ApplicationName connection property dynamically on every request in the following format:\n{\"app\":\"<ApplicationName>\",\"uid\":\"<user_id>\",\"id\":\"<NpgsqlRest.ExecutionIdHeaderName>\"}\nNote: The ApplicationName connection property is limited to 64 characters.",
        ["ConnectionSettings:TestConnectionStrings"] = "Test any connection string before initializing the application and using it. The connection string is tested by opening and closing the connection.",
        ["ConnectionSettings:RetryOptions"] = "Connection open retry options.",
        ["ConnectionSettings:RetryOptions:RetrySequenceSeconds"] = "Retry sequence in seconds. Accepts decimal numbers (0.25 is quarter of a second). The length of the array determines the maximum number of retries.",
        ["ConnectionSettings:RetryOptions:ErrorCodes"] = "Error codes that will trigger a retry when opening a connection. See https://www.postgresql.org/docs/current/errcodes-appendix.html",
        ["ConnectionSettings:MetadataQueryConnectionName"] = "The connection name in ConnectionStrings configuration that will be used to execute the metadata query. If this value is null, the default connection string will be used.",
        ["ConnectionSettings:MetadataQuerySchema"] = "Set the search path to this schema before executing the metadata query function.\nWhen null (default), no search path is set and the server's default search path is used.\n\nThis is needed when using non superuser connection roles with limited schema access and mapping the metadata function to a specific schema.\nIf the connection string contains the same \"Search Path=\" it will be skipped.",
        ["ConnectionSettings:MultiHostConnectionTargets"] = "Any: Any successful connection is acceptable.\nPrimary: Server must not be in hot standby mode (pg_is_in_recovery() must return false).\nStandby: Server must be in hot standby mode (pg_is_in_recovery() must return true).\nPreferPrimary: First try to find a primary server, but if none of the listed hosts is a primary server, try again in Any mode.\nPreferStandby: First try to find a standby server, but if none of the listed hosts is a standby server, try again in Any mode.\nReadWrite: Session must accept read-write transactions by default (that is, the server must not be in hot standby mode and the default_transaction_read_only parameter must be off).\nReadOnly: Session must not accept read-write transactions by default (the converse).\nsee https://www.npgsql.org/doc/failover-and-load-balancing.html",
        ["ConnectionSettings:MultiHostConnectionTargets:Default"] = "all connections use the same target mode",
        ["ConnectionSettings:MultiHostConnectionTargets:ByConnectionName"] = "per connection overrides { \"name\": \"Primary|Standby|Any|PreferPrimary|PreferStandby|ReadWrite|ReadOnly\" }",
        ["Ssl"] = "Enable to invoke UseKestrelHttpsConfiguration. See https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.hosting.webhostbuilderkestrelextensions.usekestrelhttpsconfiguration?view=aspnetcore-8.0",
        ["Ssl:UseHttpsRedirection"] = "Adds middleware for redirecting HTTP Requests to HTTPS. See https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.builder.httpspolicybuilderextensions.usehttpsredirection?view=aspnetcore-8.0",
        ["Ssl:UseHsts"] = "Adds middleware for using HSTS, which adds the Strict-Transport-Security header. See https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.builder.hstsbuilderextensions.usehsts?view=aspnetcore-2.1",
        ["DataProtection"] = "Data protection settings. Encryption/decryption settings for Auth Cookies, Antiforgery tokens and custom data protection needs.",
        ["DataProtection:CustomApplicationName"] = "Set to null to use the current \"ApplicationName\" value.\nThis value determines encryption type or class. Meaning, different application names will not be able to decrypt each other's data.",
        ["DataProtection:DefaultKeyLifetimeDays"] = "Sets the default lifetime in days of keys created by the data protection system.\nRepresents a number of days how long before keys are rotated.",
        ["DataProtection:Storage"] = "Data protection location: \"Default\", \"FileSystem\" or \"Database\"\n\nNote: When running on Linux, using Default location means keys will not be persisted.\nWhen keys are lost on restart, encrypted tokens (auth) will also not work on restart.\nLinux users should use FileSystem or Database storage.",
        ["DataProtection:FileSystemPath"] = "FileSystem storage path. Set to a valid path when using FileSystem.\nNote: When running in Docker environment, the path must be a Docker volume path to persist the keys.",
        ["DataProtection:GetAllElementsCommand"] = "GetAllElements database command. Expected to return rows with a single column of type text.",
        ["DataProtection:StoreElementCommand"] = "StoreElement database command. Receives two parameters: name and data of type text. Doesn't return anything.",
        ["DataProtection:EncryptionAlgorithm"] = "Configure encryption algorithms for data protection keys or null to use the default algorithm.\nValues: AES_128_CBC, AES_192_CBC, AES_256_CBC, AES_128_GCM, AES_192_GCM, AES_256_GCM",
        ["DataProtection:ValidationAlgorithm"] = "Configure validation algorithms for data protection keys or null to use the default algorithm.\nValues: HMACSHA256, HMACSHA512",
        ["DataProtection:KeyEncryption"] = "Key encryption method: \"None\", \"Certificate\", or \"Dpapi\" (Windows only)\nNone: Keys are not encrypted at rest (default)\nCertificate: Keys are encrypted using an X.509 certificate\nDpapi: Keys are encrypted using Windows Data Protection API (Windows only)",
        ["DataProtection:CertificatePath"] = "Path to the X.509 certificate file (.pfx) when using Certificate key encryption.",
        ["DataProtection:CertificatePassword"] = "Password for the certificate file. Can be null for certificates without password.\nFor security, consider using environment variable reference: \"${CERT_PASSWORD}\"",
        ["DataProtection:DpapiLocalMachine"] = "When using Dpapi key encryption, set to true to protect keys to the local machine.\nIf false (default), keys are protected to the current user account.",
        ["Kestrel"] = "Uncomment to configure Kestrel web server and to add certificates\nSee https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-9.0",
        ["ThreadPool"] = "Thread pool configuration settings for optimizing application performance",
        ["ThreadPool:MinWorkerThreads"] = "Minimum number of worker threads in the thread pool. Set to null to use system defaults.",
        ["ThreadPool:MinCompletionPortThreads"] = "Minimum number of completion port threads. Set to null to use system defaults.",
        ["ThreadPool:MaxWorkerThreads"] = "Maximum number of worker threads in the thread pool. Set to null to use system defaults.",
        ["ThreadPool:MaxCompletionPortThreads"] = "Maximum number of completion port threads. Set to null to use system defaults.",
        ["Auth"] = "Authentication and Authorization settings",
        ["Auth:CookieAuth"] = "Enable Cookie Auth",
        ["Auth:CookieAuthScheme"] = "Authentication scheme name for cookie authentication. Set to null to use default.",
        ["Auth:CookieValidDays"] = "Number of days the cookie remains valid.",
        ["Auth:CookieName"] = "Custom name for the authentication cookie. Set to null to use default.",
        ["Auth:CookiePath"] = "Path scope for the authentication cookie. Set to null to use default.",
        ["Auth:CookieDomain"] = "Domain scope for the authentication cookie. Set to null to use default.",
        ["Auth:CookieMultiSessions"] = "Allow multiple concurrent sessions for the same user.",
        ["Auth:CookieHttpOnly"] = "Make cookie accessible only via HTTP (not JavaScript).",
        ["Auth:BearerTokenAuth"] = "Enable Microsoft Bearer Token Auth (proprietary format, not JWT)",
        ["Auth:BearerTokenAuthScheme"] = "Authentication scheme name for bearer token authentication. Set to null to use default.",
        ["Auth:BearerTokenExpireHours"] = "Number of hours before bearer token expires.",
        ["Auth:BearerTokenRefreshPath"] = "POST { \"refresh\": \"{{refreshToken}}\" }",
        ["Auth:JwtAuth"] = "Enable standard JWT (JSON Web Token) Bearer Authentication",
        ["Auth:JwtAuthScheme"] = "Authentication scheme name for JWT authentication. Set to null to use default \"JwtBearer\".",
        ["Auth:JwtSecret"] = "Secret key used to sign JWT tokens. Must be at least 32 characters for HS256.\nIMPORTANT: Use a strong, unique secret in production. Store securely (e.g., environment variable).",
        ["Auth:JwtIssuer"] = "JWT issuer (iss claim). Identifies the principal that issued the JWT.",
        ["Auth:JwtAudience"] = "JWT audience (aud claim). Identifies the recipients that the JWT is intended for.",
        ["Auth:JwtExpireMinutes"] = "Number of minutes before JWT access token expires. Default is 60 minutes.",
        ["Auth:JwtRefreshExpireDays"] = "Number of days before JWT refresh token expires. Default is 7 days.",
        ["Auth:JwtValidateIssuer"] = "Validate the issuer (iss) claim. Set to true if JwtIssuer is configured.",
        ["Auth:JwtValidateAudience"] = "Validate the audience (aud) claim. Set to true if JwtAudience is configured.",
        ["Auth:JwtValidateLifetime"] = "Validate the token lifetime (exp claim). Default is true.",
        ["Auth:JwtValidateIssuerSigningKey"] = "Validate the signing key. Default is true.",
        ["Auth:JwtClockSkew"] = "Clock skew to apply when validating token lifetime. Format: PostgreSQL interval.\nDefault is 5 minutes to account for clock differences between servers.",
        ["Auth:JwtRefreshPath"] = "URL path for JWT token refresh endpoint. POST with { \"refreshToken\": \"...\" }\nReturns new access token and refresh token pair.",
        ["Auth:External"] = "Enable external auth providers",
        ["Auth:External:BrowserSessionStatusKey"] = "sessionStorage key to store the status of the external auth process returned by the signin page.\nThe value is HTTP status code (200 for success, 401 for unauthorized, 403 for forbidden, etc.)",
        ["Auth:External:BrowserSessionMessageKey"] = "sessionStorage key to store the message of the external auth process returned by the signin page.",
        ["Auth:External:SigninUrl"] = "Path to the signin page to handle the external auth process. Redirect to this page to start the external auth process.\nFormat placeholder {0} is the provider name in lowercase (google, linkedin, github, etc.)",
        ["Auth:External:SignInHtmlTemplate"] = "Sign in page template. Format placeholders {0} is the provider name, {1} is the script to redirect to the external auth provider.",
        ["Auth:External:RedirectUrl"] = "URL to redirect after the external auth process is completed. Usually this is resolved from the request automatically. Except when it's not.",
        ["Auth:External:ReturnToPath"] = "Path to redirect after the external auth process is completed.",
        ["Auth:External:ReturnToPathQueryStringKey"] = "Query string key to store the path to redirect after the external auth process is completed.\nUse this to set dynamic return path. If this query string key is not found, the ReturnToPath value is used.",
        ["Auth:External:LoginCommand"] = "Login command to execute after the external auth process is completed. There are five positional and optional parameters:\n$1 - external login provider (if parameter exists, type text).\n$2 - external login email (if parameter exists, type text).\n$3 - external login name (if parameter exists, type text).\n$4 - external login JSON data received (if parameter exists, type text, JSON or JSONB).\n$5 - client browser analytics JSON data (if parameter exists, type text, JSON or JSONB).\n\nThe command uses the same rules as the login enabled routine.\nSee: \"NpgsqlRest.“LoginPath\"",
        ["Auth:External:ClientAnalyticsData"] = "Browser client analytics data that will be sent as JSON to external auth command as the 5th parameter if supplied.",
        ["Auth:External:ClientAnalyticsIpKey"] = "Client IP address that will be added to the client analytics data under this JSON key.",
        ["Auth:External:Google"] = "External providers",
        ["Auth:External:Google:Enabled"] = "visit https://console.cloud.google.com/apis/ to configure your Google app and get your client id and client secret",
        ["Auth:External:LinkedIn:Enabled"] = "visit https://www.linkedin.com/developers/apps/ to configure your LinkedIn app and get your client id and client secret",
        ["Auth:External:GitHub:Enabled"] = "visit https://github.com/settings/developers/ to configure your GitHub app and get your client id and client secret",
        ["Auth:External:Microsoft:Enabled"] = "visit https://portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/ApplicationsListBlade to configure your Microsoft app and get your client id and client secret\nDocumentation: https://learn.microsoft.com/en-us/entra/identity-platform/",
        ["Auth:External:Facebook:Enabled"] = "visit https://developers.facebook.com/apps/ to configure your Facebook app and get your client id and client secret\nDocumentation: https://developers.facebook.com/docs/facebook-login/",
        ["Auth:PasskeyAuth"] = "WebAuthn/FIDO2 Passkey Authentication\nProvides phishing-resistant, passwordless authentication using device-native biometrics or PINs.",
        ["Auth:PasskeyAuth:Enabled"] = "Enable passkey authentication.",
        ["Auth:PasskeyAuth:EnableRegister"] = "Enable registration endpoints.",
        ["Auth:PasskeyAuth:RateLimiterPolicy"] = "Rate limiter policy name to apply to all passkey endpoints.\nIt is recommended to enable rate limiting on passkey endpoints to protect against brute-force attacks.\nSet to the name of a configured rate limiter policy, or null to disable rate limiting.",
        ["Auth:PasskeyAuth:ConnectionName"] = "Optional connection name for named DataSource or ConnectionString lookup.\nIf null, uses the default DataSource or ConnectionString from NpgsqlRest options.",
        ["Auth:PasskeyAuth:CommandRetryStrategy"] = "Command retry strategy name from CommandRetryOptions.Strategies.\nSet to null to disable command retry for passkey endpoints.",
        ["Auth:PasskeyAuth:RelyingPartyId"] = "Relying Party ID (domain name). Should match your application domain (e.g., \"example.com\").\nIf null, auto-detected from the request host.\nNote: IP addresses are not permitted - use \"localhost\" for local development.",
        ["Auth:PasskeyAuth:RelyingPartyName"] = "Human-readable Relying Party name displayed to users during registration and authentication.\nIf null, uses the ApplicationName from configuration.",
        ["Auth:PasskeyAuth:RelyingPartyOrigins"] = "Allowed origins for origin validation (scheme + domain + port).\nExample: [\"https://example.com\", \"https://www.example.com\"]\nIf empty, auto-detected from the request.\nNote: IP addresses are not permitted - use \"http://localhost:port\" for local development.",
        ["Auth:PasskeyAuth:AddPasskeyOptionsPath"] = "Post path for adding a passkey to an existing authenticated user (options).\nPost any additional data in the body as JSON (e.g., { \"deviceName\": \"My Phone\" }).\nRequires authentication. Set to null to disable this endpoint.",
        ["Auth:PasskeyAuth:AddPasskeyPath"] = "Post path for adding a passkey to an existing authenticated user (completion).\nPost the WebAuthn response data in the body as JSON (challengeId, credentialId, attestationObject, clientDataJSON, transports).\nAdditional JSON body fields are userContext passed through to the CompleteAddExistingUserCommand and optional analyticsData.\nRequires authentication. Set to null to disable this endpoint.",
        ["Auth:PasskeyAuth:RegistrationOptionsPath"] = "Post path for registration options (new user with passkey).\nPost the user registration data the body as JSON (e.g., { \"user_name\": \"...\", \"user_display_name\": \"...\", \"deviceName\": \"My Phone\"  }).\nNo authentication required. Set to null to disable registration.",
        ["Auth:PasskeyAuth:RegistrationPath"] = "Post path for registration completion (new user with passkey).\nPost the WebAuthn response data in the body as JSON (challengeId, credentialId, attestationObject, clientDataJSON, transports).\nAdditional JSON body fields are userContext passed through to the CompleteAddExistingUserCommand and optional analyticsData.\nNo authentication required. Set to null to disable registration.",
        ["Auth:PasskeyAuth:LoginOptionsPath"] = "Post path for the login options endpoint.\nPost the user login data in the body as JSON (e.g., { \"user_name\": \"...\" } ).\nPosting the user_name is optional when using discoverable credentials. When discoverable credentials ate not enabled on the authenticator, user_name is required.",
        ["Auth:PasskeyAuth:LoginPath"] = "Post path for the login completion endpoint.\nPost the WebAuthn response data in the body as JSON (challengeId, credentialId, authenticatorData, clientDataJSON, signature, userHandle) and optional analyticsData.",
        ["Auth:PasskeyAuth:ChallengeTimeoutMinutes"] = "Challenge timeout in minutes. Challenges not used within this time will expire.",
        ["Auth:PasskeyAuth:UserVerificationRequirement"] = "User verification requirement:\n- \"preferred\": Request UV if available, but allow authentication without it\n- \"required\": Require UV, fail if not available\n- \"discouraged\": Don't request UV (not recommended for most use cases)\n\nPractical implications:\n- \"required\": User MUST authenticate with biometric (fingerprint, face) or device PIN.\nHigh security - proves the person is present, not just possession of the device.\n- \"preferred\": Browser will request biometric/PIN if available, but allows passkey\nauthentication even if UV isn't supported (e.g., older security keys).\n- \"discouraged\": Just proves device possession, no biometric/PIN prompt. Lower security.\n\nFor most apps, use \"preferred\". For banking/sensitive apps, use \"required\".",
        ["Auth:PasskeyAuth:ResidentKeyRequirement"] = "Resident key (discoverable credential) requirement:\n- \"preferred\": Request discoverable credentials if supported\n- \"required\": Require discoverable credentials, fail if not supported\n- \"discouraged\": Request non-discoverable credentials\n\nPractical implications:\n- \"required\": True passwordless. Browser shows passkey picker with all accounts at login.\nUser picks account and authenticates with biometric/PIN. No username input needed.\n- \"preferred\"/\"discouraged\": User enters username first, then authenticates with passkey.\n\nFor passwordless flows (no username field), set to \"required\".",
        ["Auth:PasskeyAuth:AttestationConveyance"] = "Attestation conveyance preference - controls whether the server requests the authenticator\nto provide cryptographic proof of its identity (make/model) and security properties during registration.\n\nOptions:\n- \"none\": Don't request attestation. Accept any valid authenticator without verifying its identity.\nBest for most apps - simpler, better user privacy, wider device compatibility. (Recommended)\n- \"indirect\": Request attestation but allow the browser/platform to anonymize it. Rarely useful.\n- \"direct\": Request full attestation certificate chain from the authenticator.\nUse when you need to verify the authenticator vendor/model meets security requirements.\n- \"enterprise\": Request enterprise-specific attestation for managed corporate devices\nwhere IT needs to verify only organization-approved hardware authenticators are used.\n\nWhen to use non-\"none\" values:\n- Banking/financial apps requiring hardware security keys only\n- Enterprise environments restricting to specific authenticator models\n- Compliance requirements mandating certain security certifications (FIDO2 L1/L2)\n\nFor most consumer applications, \"none\" is the correct choice - you just want the user\nto authenticate securely, not audit their hardware.",
        ["Auth:PasskeyAuth:ValidateSignCount"] = "Whether to validate and update the signature counter (sign count).\nWhen true, validates that the new sign count is greater than stored, and updates it after authentication.\nWhen false, skips sign count validation and update entirely.\nSet to false if authenticators don't support it or you want to simplify your database schema.",
        ["Auth:PasskeyAuth:ChallengeAddExistingUserCommand"] = "SQL command to create a challenge when adding a passkey to an existing authenticated user.\nParameters:\n- $1 = claims (json): JSON object with user claims from the authenticated session\n- $2 = body (json): JSON object from request body (e.g., { \"deviceName\": \"My Phone\" })\nExpected return columns (by name):\n- status (int): HTTP status code. Return 200 to proceed, any other status aborts.\n- message (text): Error message when status != 200.\n- challenge (text): Base64-encoded random challenge bytes (typically 32 bytes).\n- challenge_id: Server-side identifier (uuid, int, bigint, or text).\n- user_handle (text): Base64-encoded random bytes (typically 32 bytes) for WebAuthn user.id.\n- user_name (text): Username displayed in the authenticator UI.\n- user_display_name (text): Display name shown in the authenticator UI.\n- exclude_credentials (text): JSON array of existing credentials.\n- user_context (json): Opaque JSON passed through to CompleteAddExistingUserCommand.\nCalled by AddPasskeyOptionsPath endpoint",
        ["Auth:PasskeyAuth:ChallengeRegistrationCommand"] = "SQL command to create a challenge for standalone registration (new user).\nParameter: $1 = JSON object from request body (e.g., { \"user_name\": \"...\", \"display_name\": \"...\" })\nExpected return columns (by name): Same as ChallengeAddExistingUserCommand\n- user_context should NOT contain \"id\" field (distinguishes from add-existing-user flow)\nCalled by StandaloneRegistrationOptionsPath endpoint",
        ["Auth:PasskeyAuth:ChallengeAuthenticationCommand"] = "SQL command to create a challenge for authentication.\nParameters:\n- $1 = user_name (text, optional - null for discoverable credential flow)\n- $2 = body (json): JSON object from request body (e.g., { \"deviceInfo\": \"...\" })\nExpected return columns (by name): status, message, challenge, challenge_id, allow_credentials\nCalled by AuthenticationOptionsPath endpoint",
        ["Auth:PasskeyAuth:VerifyChallengeCommand"] = "Used by: Flow 1, Flow 2, Flow 3 (ALL flows)\nSQL command to verify and consume a challenge.\nParameters: $1 = challenge_id (uuid, int, bigint, or text), $2 = operation (text: \"registration\" or \"authentication\")\nReturns: challenge (bytea) - the original challenge bytes, or NULL if not found/expired\nCalled by all endpoints",
        ["Auth:PasskeyAuth:AuthenticateDataCommand"] = "SQL command to get credential data for authentication.\nParameter: $1 = credential_id (bytea)\nExpected return columns (by name): status, message, public_key, public_key_algorithm, sign_count, user_context\nNote: user_context is passed through to CompleteAuthenticateCommand (typically contains user_id)\nCalled by AuthenticatePath endpoint",
        ["Auth:PasskeyAuth:CompleteAddExistingUserCommand"] = "SQL command to complete adding a passkey to an existing user account.\nParameters:\n- $1 = credential_id (bytea): Unique credential identifier from authenticator.\n- $2 = user_handle (bytea): WebAuthn user.id from registration options.\n- $3 = public_key (bytea): Public key in COSE format.\n- $4 = algorithm (int): COSE algorithm identifier (-7 for ES256, -257 for RS256).\n- $5 = transports (text[]): Transport hints (e.g., [\"internal\", \"hybrid\"]).\n- $6 = backup_eligible (boolean): Whether credential can be backed up/synced.\n- $7 = user_context (json): Opaque JSON from ChallengeAddExistingUserCommand (contains user ID).\n- $8 = analytics_data (json, optional): Client analytics with server-added IP.\nExpected return columns (by name): status, message\nCalled by RegisterPath endpoint",
        ["Auth:PasskeyAuth:CompleteRegistrationCommand"] = "SQL command to complete standalone passkey registration (creates new user).\nParameters: Same as CompleteAddExistingUserCommand\n- user_context should NOT contain \"id\" field (creates new user instead of linking to existing)\nExpected return columns (by name): status, message\nCalled by RegisterPath endpoint",
        ["Auth:PasskeyAuth:CompleteAuthenticateCommand"] = "Flow 3: Login -> AuthenticatePath endpoint (after signature validation)\nSQL command to update sign count and return user claims.\nParameters:\n- $1 = credential_id (bytea)\n- $2 = new_sign_count (bigint)\n- $3 = user_context (json): Opaque JSON from AuthenticateDataCommand\n- $4 = analytics_data (json, optional): Client analytics with server-added IP\nExpected return columns (by name): status, user_id, user_name, user_roles (plus any custom claims)\nCalled by AuthenticatePath endpoint",
        ["Auth:PasskeyAuth:ClientAnalyticsIpKey"] = "The JSON key name used to add the client's IP address to the analytics data server-side.\nSet to null or empty string to disable IP address collection.",
        ["Auth:PasskeyAuth:StatusColumnName"] = "Column name configuration for database responses",
        ["Log"] = "Serilog settings",
        ["Log:MinimalLevels"] = "See https://github.com/serilog/serilog/wiki/Configuration-Basics#minimum-level\nVerbose, Debug, Information, Warning, Error, Fatal.\nNote: NpgsqlRest logger applies to main application logger, which will, by default have the name defined in the ApplicationName setting.",
        ["Log:ToConsole"] = "Enable logging to console output.",
        ["Log:ConsoleMinimumLevel"] = "Minimum log level for console output: Verbose, Debug, Information, Warning, Error, Fatal.",
        ["Log:ToFile"] = "Enable logging to file system.",
        ["Log:FilePath"] = "File path for log files.",
        ["Log:FileSizeLimitBytes"] = "Maximum size limit for log files in bytes before rolling to a new file.",
        ["Log:FileMinimumLevel"] = "Minimum log level for file output: Verbose, Debug, Information, Warning, Error, Fatal.",
        ["Log:RetainedFileCountLimit"] = "Maximum number of log files to retain.",
        ["Log:RollOnFileSizeLimit"] = "Create a new log file when size limit is reached.",
        ["Log:ToPostgres"] = "Enable logging to PostgreSQL database.",
        ["Log:PostgresCommand"] = "$1 - log level text, $2 - message text, $3 - timestamp with tz in utc, $4 - exception text or null, $5 - source context\n\nPostgreSQL command to execute for database logging. Parameters: $1=level, $2=message, $3=timestamp, $4=exception, $5=source.",
        ["Log:PostgresMinimumLevel"] = "Minimum log level for PostgreSQL output: Verbose, Debug, Information, Warning, Error, Fatal.",
        ["Log:ToOpenTelemetry"] = "Enable OpenTelemetry protocol (OTLP) logging output. Requires an OTLP collector endpoint.",
        ["Log:OTLResourceAttributes:service.name"] = "application name from the ApplicationName setting",
        ["Log:OTLResourceAttributes:service.version"] = "application version, set to a static value or use a build process to update it",
        ["Log:OTLResourceAttributes:service.environment"] = "environment name from the EnvironmentName setting",
        ["Log:OutputTemplate"] = "See https://github.com/serilog/serilog/wiki/Formatting-Output",
        ["ResponseCompression"] = "Response compression settings",
        ["ResponseCompression:Enabled"] = "Enable response compression for HTTP responses.",
        ["ResponseCompression:EnableForHttps"] = "Enable response compression for HTTPS responses.",
        ["ResponseCompression:UseBrotli"] = "Use Brotli compression algorithm when supported by client.",
        ["ResponseCompression:UseGzipFallback"] = "Use Gzip compression as fallback when Brotli is not supported.",
        ["ResponseCompression:CompressionLevel"] = "Compression level: Optimal, Fastest, NoCompression, SmallestSize.",
        ["ResponseCompression:IncludeMimeTypes"] = "MIME types to include for compression.",
        ["ResponseCompression:ExcludeMimeTypes"] = "MIME types to exclude from compression.",
        ["Antiforgery"] = "Antiforgery Token Configuration: Protects against Cross-Site Request Forgery (CSRF/XSRF) attacks.\nCSRF attacks occur when a malicious site tricks a user's browser into making unwanted requests to your application\nusing the user's authenticated session (cookies).\n\nHow it works:\n1. Server generates a unique token for each session/request\n2. Token is embedded in forms (hidden field) or sent via header (for AJAX)\n3. On state-changing requests (POST, PUT, DELETE), server validates the token\n4. Requests without valid tokens are rejected (400 Bad Request)\n\nUsage in HTML forms:\n<form method=\"post\">\n<input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"{antiForgeryToken}\" />\n...\n</form>\n\nUsage in AJAX/JavaScript:\nfetch('/api/endpoint', {\nmethod: 'POST',\nheaders: { 'RequestVerificationToken': tokenValue },\nbody: JSON.stringify(data)\n});\n\nNote: Antiforgery automatically sets the X-Frame-Options: SAMEORIGIN header to help prevent clickjacking.\nIf you're using the SecurityHeaders middleware with X-Frame-Options, the Antiforgery header takes precedence\n(SecurityHeaders will skip X-Frame-Options when Antiforgery is enabled).\n\nReference: https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery",
        ["Antiforgery:Enabled"] = "Enable antiforgery token validation for state-changing requests.",
        ["Antiforgery:CookieName"] = "Name of the cookie that stores the antiforgery token.\nSet to null to use the ASP.NET Core default (unique per application, starts with \".AspNetCore.Antiforgery.\").\nCustom names are useful when running multiple applications on the same domain.",
        ["Antiforgery:FormFieldName"] = "Name of the hidden form field that contains the request verification token.\nThis must match the name used in your HTML forms.",
        ["Antiforgery:HeaderName"] = "Name of the HTTP header that can contain the antiforgery token.\nUseful for AJAX requests where adding a form field is not possible.\nJavaScript can read the token from a cookie or meta tag and send it in this header.",
        ["Antiforgery:SuppressReadingTokenFromFormBody"] = "When true, the server will NOT look for the token in the form body.\nForces header-only validation - useful for pure API scenarios where all requests use headers.\nWhen false (default), server checks both form field and header.",
        ["Antiforgery:SuppressXFrameOptionsHeader"] = "When true, prevents the automatic X-Frame-Options: SAMEORIGIN header from being set.\nX-Frame-Options helps prevent clickjacking attacks by blocking the page from being embedded in iframes.\nOnly set to true if:\n- You need your pages to be embedded in iframes from other origins, OR\n- You're setting X-Frame-Options elsewhere (e.g., in SecurityHeaders or at the proxy level)\nDefault: false (header is set for security)",
        ["StaticFiles"] = "Static files settings",
        ["StaticFiles:AuthorizePaths"] = "List of static file patterns that will require authorization.\nFile paths are relative to the RootPath property and pattern matching is case-insensitive.\nPattern can include wildcards (* matches any chars, ** matches recursively including /, ? matches single char).\nFor example: *.html, /user/*, /admin/**/*.html",
        ["StaticFiles:ParseContentOptions:Enabled"] = "Enable or disable the parsing of the static files.\nWhen enabled, the static files will be parsed and the tags will be replaced with the values from the claims collection.\nThe tags are in the format: {claimType} where claimType is the name of the claim that will be replaced with the value from the claims collection.",
        ["StaticFiles:ParseContentOptions:AvailableClaims"] = "List of claims types used. These will be parsed to NULL if not found in the claims collection or user is not authenticated.",
        ["StaticFiles:ParseContentOptions:CacheParsedFile"] = "Set to true to cache the parsed files in memory. This will improve the performance of the static files. It only applies to parsed content.\nNote: caching will occur before parsing, it applies only to templates, not parsed content.",
        ["StaticFiles:ParseContentOptions:Headers"] = "Headers to be added to the response for static files. Set to null or empty array to ignore.",
        ["StaticFiles:ParseContentOptions:FilePaths"] = "List of static file patterns that will parse the content and replace the tags with the values from the claims collection.\nFile paths are relative to the RootPath property and pattern matching is case-insensitive.\nPattern can include wildcards (* matches any chars, ** matches recursively including /, ? matches single char).\nFor example: *.html, *.htm, /pages/**/*.html",
        ["StaticFiles:ParseContentOptions:AntiforgeryFieldName"] = "Name of the configured Antiforgery form field name to be used in the static files (see Antiforgery FormFieldName setting).",
        ["StaticFiles:ParseContentOptions:AntiforgeryToken"] = "Value of the Antiforgery token if Antiforgery is enabled.",
        ["Cors"] = "Cross-origin resource sharing",
        ["Cors:Enabled"] = "Enable Cross-Origin Resource Sharing (CORS) support.",
        ["Cors:AllowedOrigins"] = "List of allowed origins for CORS requests. Empty array allows no origins.",
        ["Cors:AllowedMethods"] = "List of allowed HTTP methods for CORS requests.",
        ["Cors:AllowedHeaders"] = "List of allowed headers for CORS requests.",
        ["Cors:AllowCredentials"] = "Allow credentials (cookies, authorization headers) in CORS requests.",
        ["Cors:PreflightMaxAgeSeconds"] = "Maximum age in seconds for preflight request caching (10 minutes).",
        ["SecurityHeaders"] = "Security Headers: Adds HTTP security headers to all responses to protect against common web vulnerabilities.\nThese headers instruct browsers how to handle your content securely.\nNote: X-Frame-Options is automatically handled by the Antiforgery middleware when enabled (see Antiforgery.SuppressXFrameOptionsHeader).\nReference: https://owasp.org/www-project-secure-headers/",
        ["SecurityHeaders:Enabled"] = "Enable security headers middleware. When enabled, configured headers are added to all HTTP responses.",
        ["SecurityHeaders:XContentTypeOptions"] = "X-Content-Type-Options: Prevents browsers from MIME-sniffing a response away from the declared content-type.\nRecommended value: \"nosniff\"\nSet to null to not include this header.",
        ["SecurityHeaders:XFrameOptions"] = "X-Frame-Options: Controls whether the browser should allow the page to be rendered in a <frame>, <iframe>, <embed> or <object>.\nValues: \"DENY\" (never allow), \"SAMEORIGIN\" (allow from same origin only)\nNote: This header is SKIPPED if Antiforgery is enabled (Antiforgery already sets X-Frame-Options: SAMEORIGIN by default).\nSet to null to not include this header.",
        ["SecurityHeaders:ReferrerPolicy"] = "Referrer-Policy: Controls how much referrer information should be included with requests.\nValues: \"no-referrer\", \"no-referrer-when-downgrade\", \"origin\", \"origin-when-cross-origin\",\n\"same-origin\", \"strict-origin\", \"strict-origin-when-cross-origin\", \"unsafe-url\"\nRecommended: \"strict-origin-when-cross-origin\" (send origin for cross-origin requests, full URL for same-origin)\nSet to null to not include this header.",
        ["SecurityHeaders:ContentSecurityPolicy"] = "Content-Security-Policy: Defines approved sources of content that the browser may load.\nHelps prevent XSS, clickjacking, and other code injection attacks.\nExample: \"default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'\"\nReference: https://developer.mozilla.org/en-US/docs/Web/HTTP/CSP\nSet to null to not include this header (recommended to configure based on your application needs).",
        ["SecurityHeaders:PermissionsPolicy"] = "Permissions-Policy: Controls which browser features and APIs can be used.\nExample: \"geolocation=(), microphone=(), camera=()\" disables these features entirely.\nExample: \"geolocation=(self), microphone=()\" allows geolocation only from same origin.\nReference: https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Permissions-Policy\nSet to null to not include this header.",
        ["SecurityHeaders:CrossOriginOpenerPolicy"] = "Cross-Origin-Opener-Policy: Controls how your document is shared with cross-origin popups.\nValues: \"unsafe-none\", \"same-origin-allow-popups\", \"same-origin\"\nSet to null to not include this header.",
        ["SecurityHeaders:CrossOriginEmbedderPolicy"] = "Cross-Origin-Embedder-Policy: Prevents a document from loading cross-origin resources that don't explicitly grant permission.\nValues: \"unsafe-none\", \"require-corp\", \"credentialless\"\nRequired for SharedArrayBuffer and high-resolution timers (along with COOP: same-origin).\nSet to null to not include this header.",
        ["SecurityHeaders:CrossOriginResourcePolicy"] = "Cross-Origin-Resource-Policy: Indicates how the resource should be shared cross-origin.\nValues: \"same-site\", \"same-origin\", \"cross-origin\"\nSet to null to not include this header.",
        ["ForwardedHeaders"] = "Forwarded Headers: Enables the application to read proxy headers (X-Forwarded-For, X-Forwarded-Proto, X-Forwarded-Host).\nCRITICAL: Required when running behind a reverse proxy (nginx, Apache, Azure App Service, AWS ALB, Cloudflare, etc.)\nWithout this, the application sees the proxy's IP instead of the client's real IP, and HTTP instead of HTTPS.\nSecurity Warning: Only enable if you're behind a trusted proxy. Malicious clients can spoof these headers.\nReference: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer",
        ["ForwardedHeaders:Enabled"] = "Enable forwarded headers middleware (automatically placed first in the middleware pipeline).",
        ["ForwardedHeaders:ForwardLimit"] = "Limits the number of proxy entries that will be processed from X-Forwarded-For.\nDefault is 1 (trust only the immediate proxy). Increase if you have multiple proxies in a chain.\nSet to null to process all entries (not recommended for security).",
        ["ForwardedHeaders:KnownProxies"] = "List of IP addresses of known proxies to accept forwarded headers from.\nExample: [\"10.0.0.1\", \"192.168.1.1\"]\nIf empty and KnownNetworks is also empty, forwarded headers are accepted from any source (less secure).",
        ["ForwardedHeaders:KnownNetworks"] = "List of CIDR network ranges of known proxies.\nExample: [\"10.0.0.0/8\", \"192.168.0.0/16\", \"172.16.0.0/12\"] for private networks\nUseful when proxy IPs are dynamically assigned within a known range.",
        ["ForwardedHeaders:AllowedHosts"] = "List of allowed values for the X-Forwarded-Host header.\nExample: [\"example.com\", \"www.example.com\"]\nIf empty, any host is allowed (less secure). Helps prevent host header injection attacks.",
        ["HealthChecks"] = "Health Checks: Provides endpoints for monitoring application health, used by container orchestrators (Kubernetes, Docker Swarm),\nload balancers, and monitoring systems to determine if the application is running correctly.\nThree types of checks are supported:\n- /health: Overall health status (combines all checks)\n- /health/ready: Readiness probe - is the app ready to accept traffic? (includes database connectivity)\n- /health/live: Liveness probe - is the app process running? (always returns healthy if app responds)\nReference: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks",
        ["HealthChecks:Enabled"] = "Enable health check endpoints.",
        ["HealthChecks:CacheDuration"] = "Cache health check responses server-side in memory for the specified duration.\nCached responses are served without re-executing the endpoint.\nValue is in PostgreSQL interval format (e.g., '5 seconds', '1 minute', '30s', '1min').\nSet to null to disable caching. Query strings are ignored to prevent cache-busting.",
        ["HealthChecks:Path"] = "Path for the main health check endpoint that reports overall status.\nReturns \"Healthy\", \"Degraded\", or \"Unhealthy\" with HTTP 200 (healthy/degraded) or 503 (unhealthy).",
        ["HealthChecks:ReadyPath"] = "Path for the readiness probe endpoint.\nKubernetes uses this to know when a pod is ready to receive traffic.\nIncludes database connectivity check when IncludeDatabaseCheck is true.\nReturns 503 Service Unavailable if database is unreachable.",
        ["HealthChecks:LivePath"] = "Path for the liveness probe endpoint.\nKubernetes uses this to know when to restart a pod.\nAlways returns Healthy (200) if the application process is responding.\nDoes NOT check database - a slow database shouldn't trigger a container restart.",
        ["HealthChecks:IncludeDatabaseCheck"] = "Include PostgreSQL database connectivity in health checks.\nWhen true, the readiness probe will fail if the database is unreachable.",
        ["HealthChecks:DatabaseCheckName"] = "Name for the database health check (appears in detailed health reports).",
        ["HealthChecks:RequireAuthorization"] = "Require authentication for health check endpoints.\nWhen true, all health endpoints require a valid authenticated user.\nSecurity Consideration: Health endpoints can reveal information about your infrastructure\n(database connectivity, service status). Enable this if your health endpoints are publicly accessible.\nNote: Kubernetes/Docker health probes may need to authenticate if this is enabled.",
        ["HealthChecks:RateLimiterPolicy"] = "Apply a rate limiter policy to health check endpoints.\nSpecify the name of a policy defined in RateLimiterOptions.Policies.\nSecurity Consideration: Prevents denial-of-service attacks targeting health endpoints.\nSet to null to disable rate limiting on health endpoints.\nExample: \"fixed\" or \"bucket\" (must match a policy name from RateLimiterOptions).",
        ["Stats"] = "PostgreSQL Statistics Endpoints\nExposes PostgreSQL statistics through HTTP endpoints for monitoring and debugging.\nProvides access to pg_stat_user_functions, pg_stat_user_tables, pg_stat_user_indexes, and pg_stat_activity.",
        ["Stats:Enabled"] = "Enable PostgreSQL statistics endpoints.",
        ["Stats:CacheDuration"] = "Cache stats responses server-side in memory for the specified duration.\nCached responses are served without re-executing the endpoint.\nValue is in PostgreSQL interval format (e.g., '5 seconds', '1 minute', '30s', '1min').\nSet to null to disable caching. Query strings are ignored to prevent cache-busting.",
        ["Stats:RateLimiterPolicy"] = "Apply a rate limiter policy to stats endpoints.\nSpecify the name of a policy defined in RateLimiterOptions.Policies.\nSet to null to disable rate limiting on stats endpoints.",
        ["Stats:ConnectionName"] = "Use a specific named connection for stats queries.\nWhen null, uses the default connection string.\nUseful when you want to query stats from a different database or use read-only credentials.",
        ["Stats:RequireAuthorization"] = "Require authentication for stats endpoints.\nSecurity Consideration: Stats endpoints can reveal sensitive information about your database\n(table sizes, query patterns, active sessions). Enable this for production environments.",
        ["Stats:AuthorizedRoles"] = "Restrict access to specific roles.\nWhen null or empty, any authenticated user can access (if RequireAuthorization is true).\nExample: [\"admin\", \"dba\"] - only users with admin or dba role can access.",
        ["Stats:OutputFormat"] = "Output format for stats endpoints: \"json\" or \"html\".\n- json: JSON array\n- html: HTML table, Excel-compatible for direct browser copy-paste (default)\nCan be overridden per-request with the ?format= query string parameter (e.g. ?format=json).",
        ["Stats:SchemaSimilarTo"] = "Filter schemas using PostgreSQL SIMILAR TO pattern.\nWhen null, all schemas are included.\nExample: \"public|myapp%\" - includes 'public' and schemas starting with 'myapp'.",
        ["Stats:RoutinesStatsPath"] = "Path for routine (function/procedure) performance statistics.\nReturns data from pg_stat_user_functions including call counts and execution times.\nNote: Requires track_functions = 'pl' or 'all' in postgresql.conf.\nEnable with: alter system set track_functions = 'all'; select pg_reload_conf();\nOr set track_functions = 'all' directly in postgresql.conf and restart/reload.",
        ["Stats:TablesStatsPath"] = "Path for table statistics.\nReturns data from pg_stat_user_tables including tuple counts, sizes, scan counts, and vacuum info.",
        ["Stats:IndexesStatsPath"] = "Path for index statistics.\nReturns data from pg_stat_user_indexes including scan counts and index definitions.",
        ["Stats:ActivityPath"] = "Path for current database activity.\nReturns data from pg_stat_activity showing active sessions, queries, and wait events.\nSecurity Consideration: Shows currently running queries which may contain sensitive data.",
        ["CommandRetryOptions"] = "Command retry strategies and options for client and middleware commands.",
        ["CommandRetryOptions:Strategies:default:RetrySequenceSeconds"] = "Retry sequence in seconds. Accepts decimal numbers (0.25 is quarter of a second). The length of the array determines the maximum number of retries.",
        ["CommandRetryOptions:Strategies:default:ErrorCodes"] = "Error codes that will trigger a retry when executing a command. See https://www.postgresql.org/docs/current/errcodes-appendix.html",
        ["CacheOptions"] = "Caching options for routines that support caching. Currently, routines that return a single result set can be cached. Returning table or \"setof\" cannot be cached.\nTo enable caching for a routine, add the following comment annotation to the routine:\ncached [ param1, param2, param3 [, ...] ] - parameters are optional, if no parameters are specified, all parameters are used for cache key.\ncache_expires [ value ] or cache_expires_in [ value ] - accepts PostgreSQL interval format (for example: '5 minutes' or '5min', '1 second' or '1s', etc.). Default is forever (no expiration).",
        ["CacheOptions:Type"] = "Cache type: Memory, Redis, or Hybrid\n- Memory: In-process memory cache (fastest, single instance only)\n- Redis: Distributed Redis cache (slower, shared across instances)\n- Hybrid: Uses Microsoft.Extensions.Caching.Hybrid which provides:\n- Automatic stampede protection to prevent multiple concurrent requests from hitting the database\n- Optional Redis L2 backend (enable with HybridCacheUseRedisBackend: true) for sharing cache across instances\n- Without Redis, works as in-memory cache with stampede protection",
        ["CacheOptions:MemoryCachePruneIntervalSeconds"] = "When memory cache is used, this value determines how often the cache will be pruned for expired items (in seconds).",
        ["CacheOptions:RedisConfiguration"] = "Redis configuration string. Used when Type is \"Redis\", or when Type is \"Hybrid\" with UseRedisBackend: true.\nSee: https://stackexchange.github.io/StackExchange.Redis/Configuration.html",
        ["CacheOptions:MaxCacheableRows"] = "Maximum number of rows that can be cached for set-returning functions.\nIf a result set exceeds this limit, it will not be cached (but will still be returned).\nSet to 0 to disable caching for sets entirely. Set to null for unlimited (use with caution).",
        ["CacheOptions:UseHashedCacheKeys"] = "When true, cache keys longer than HashKeyThreshold characters are hashed to a fixed-length SHA256 string (64 characters).\nThis reduces memory usage for long cache keys and improves Redis performance with large keys.\nRecommended for Redis cache or when caching routines with many/large parameters.",
        ["CacheOptions:HashKeyThreshold"] = "Cache keys longer than this threshold (in characters) will be hashed when UseHashedCacheKeys is true.\nKeys shorter than this threshold are stored as-is for better debuggability.",
        ["CacheOptions:InvalidateCacheSuffix"] = "When set, creates an additional invalidation endpoint for each cached endpoint.\nThe invalidation endpoint has the same path with this suffix appended.\nFor example, if a cached endpoint is /api/my-endpoint/ and this is set to \"invalidate\",\nan invalidation endpoint /api/my-endpoint/invalidate will be created.\nCalling the invalidation endpoint with the same parameters removes the cached entry.",
        ["CacheOptions:HybridCacheUseRedisBackend"] = "--- Hybrid Cache specific options (only used when Type is \"Hybrid\") ---\n\nWhen true, uses Redis as the L2 (secondary/distributed) cache backend.\nWhen false (default), HybridCache uses in-memory only but still provides stampede protection.\nStampede protection prevents multiple concurrent requests from hitting the database when cache expires.",
        ["CacheOptions:HybridCacheMaximumKeyLength"] = "Maximum length of cache keys in characters. Keys longer than this will be rejected.\nDefault: 1024",
        ["CacheOptions:HybridCacheMaximumPayloadBytes"] = "Maximum size of cached payloads in bytes.\nDefault: 1048576 (1 MB)",
        ["CacheOptions:HybridCacheDefaultExpiration"] = "Default expiration for cached entries (both L1 and L2). Accepts PostgreSQL interval format.\nExamples: '5 minutes', '1 hour', '30 seconds'\nIf not set, individual endpoint cache_expires annotations are used, or entries don't expire.",
        ["CacheOptions:HybridCacheLocalCacheExpiration"] = "Expiration for L1 (in-memory) cache. If not set, uses DefaultExpiration value.\nSet this shorter than DefaultExpiration to refresh local cache more frequently from Redis.\nAccepts PostgreSQL interval format.",
        ["ValidationOptions"] = "Parameter validation options for validating endpoint parameters before database execution.\nValidation rules can be referenced in comment annotations using \"validate _param using rule_name\" syntax.",
        ["ValidationOptions:Rules"] = "Named validation rules that can be referenced in comment annotations.\nDefault rules: not_null, not_empty, required, email\n\nEach rule can have:\n- Type: NotNull, NotEmpty, Required, Regex, MinLength, MaxLength\n- Pattern: Regular expression pattern for Regex type\n- MinLength: Minimum length for MinLength type\n- MaxLength: Maximum length for MaxLength type\n- Message: Error message with placeholders {0}=original name, {1}=converted name, {2}=rule name\n- StatusCode: HTTP status code to return (default: 400)",
        ["RateLimiterOptions"] = "Rate Limiter settings to limit the number of requests from clients.",
        ["RateLimiterOptions:Policies"] = "Policy types: FixedWindow, SlidingWindow, BucketWindow, Concurrency",
        ["RateLimiterOptions:Policies:Type"] = "see https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit#fixed",
        ["RateLimiterOptions:Policies:Type"] = "see https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit#sliding-window-limiter",
        ["RateLimiterOptions:Policies:Type"] = "see https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit#token-bucket-limiter",
        ["RateLimiterOptions:Policies:Type"] = "see https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit#concurrency-limiter",
        ["ErrorHandlingOptions"] = "Error handling options for NpgsqlRest middleware",
        ["ErrorHandlingOptions:RemoveTypeUrl"] = "Remove Type URL from error responses. Middleware automatically sets a default Type URL based on the HTTP status code that points to the RFC documentation.",
        ["ErrorHandlingOptions:RemoveTraceId"] = "Remove TraceId field from error responses. Useful in development and debugging scenarios to correlate logs with error responses.",
        ["ErrorHandlingOptions:DefaultErrorCodePolicy"] = "Default policy name to use from the ErrorCodePolicies section.",
        ["ErrorHandlingOptions:TimeoutErrorMapping"] = "Timeout error mapping when command timeout occurs (see NpgsqlRest CommandTimeout setting).\ntimeout error case -> 504 Gateway Timeout",
        ["ErrorHandlingOptions:ErrorCodePolicies"] = "Named policies for mapping of PostgreSQL error codes to HTTP Status Codes.\n\nIf routine raises these PostgreSQL error codes, endpoint will return these HTTP Status Codes.\nSee https://www.postgresql.org/docs/current/errcodes-appendix.html\nException is timeout, which is not a PostgreSQL error code, but a special case when command timeout occurs.\n\n- StatusCode: HTTP status code to return.\n- Title: Optional title field in response JSON. When null, actual error message is used.\n- Details: Optional details field in response JSON. When null, PostgreSQL Error Code is used.\n- Type: Optional types field in response JSON. A URI reference [RFC3986] that identifies the problem type. Set to null to use default. Or RemoveTypeUrl to true to disable.",
        ["ErrorHandlingOptions:ErrorCodePolicies:ErrorCodes:42501"] = "query_canceled      -> 403 Forbidden",
        ["ErrorHandlingOptions:ErrorCodePolicies:ErrorCodes:57014"] = "query_canceled      -> 205 Reset Content",
        ["ErrorHandlingOptions:ErrorCodePolicies:ErrorCodes:P0001"] = "raise_exception     -> 400 Bad Request",
        ["ErrorHandlingOptions:ErrorCodePolicies:ErrorCodes:P0004"] = "assert_failure      -> 400 Bad Request",
        ["NpgsqlRest"] = "NpgsqlRest HTTP Middleware General Configuration",
        ["NpgsqlRest:ConnectionName"] = "Connection name to be used from the ConnectionStrings section or NULL to use the first available connection string.",
        ["NpgsqlRest:UseMultipleConnections"] = "Allow using multiple connections from the ConnectionStrings section. When set to true, the connection name can be set for individual Routines.\nSome routines might use the primary database connection string, while others might want to use a read-only connection string from the replica servers.",
        ["NpgsqlRest:CommandTimeout"] = "Command timeout, after which the command will be cancelled and default timeout error policy will be applied. (see ErrorCodePolicies)\nValue is in PostgreSQL interval format (for example: '30 seconds' or '30s', '1 minute' or '1min', etc.) or `null` to use the default timeout of 30 seconds.",
        ["NpgsqlRest:SchemaSimilarTo"] = "Filter schema names similar to this parameter or `null` to ignore this parameter.",
        ["NpgsqlRest:SchemaNotSimilarTo"] = "Filter schema names NOT similar to this parameter or `null` to ignore this parameter.",
        ["NpgsqlRest:IncludeSchemas"] = "List of schema names to be included or `null` to ignore this parameter.",
        ["NpgsqlRest:ExcludeSchemas"] = "List of schema names to be excluded or `null` to ignore this parameter.",
        ["NpgsqlRest:NameSimilarTo"] = "Filter names similar to this parameter or `null` to ignore this parameter.",
        ["NpgsqlRest:NameNotSimilarTo"] = "Filter names NOT similar to this parameter or `null` to ignore this parameter.",
        ["NpgsqlRest:IncludeNames"] = "List of names to be included or `null` to ignore this parameter.",
        ["NpgsqlRest:ExcludeNames"] = "List of names to be excluded or `null` to ignore this parameter.",
        ["NpgsqlRest:CommentsMode"] = "Configure how the comment annotations will behave. `Ignore` will create all endpoints and ignore comment annotations. `ParseAll` will create all endpoints and parse comment annotations to alter the endpoint. `OnlyWithHttpTag` (default) will only create endpoints that contain the `HTTP` tag in the comments and then parse comment annotations.",
        ["NpgsqlRest:UrlPathPrefix"] = "The URL prefix string for every URL created by the default URL builder or `null` to ignore the URL prefix.",
        ["NpgsqlRest:KebabCaseUrls"] = "Convert all URL paths to kebab-case from the original PostgreSQL names.",
        ["NpgsqlRest:CamelCaseNames"] = "Convert all parameter names to camel case from the original PostgreSQL paramater names.",
        ["NpgsqlRest:RequiresAuthorization"] = "When set to true, it will force all created endpoints to require authorization. Authorization requirements for individual endpoints can be changed with the `EndpointCreated` function callback, or by using comment annotations.",
        ["NpgsqlRest:LogConnectionNoticeEvents"] = "When this value is true, all connection events are logged (depending on the level). This is usually triggered by the PostgreSQL RAISE statements.\nSet to false to turn off logging these events.",
        ["NpgsqlRest:LogConnectionNoticeEventsMode"] = "MessageOnly - Log only connection messages. FirstStackFrameAndMessage - Log first stack frame and the message. FullStackAndMessage - Log full stack trace and message.",
        ["NpgsqlRest:LogCommands"] = "Set this option to true to log information for every executed command and query (including parameters and parameter values) in debug level.",
        ["NpgsqlRest:LogCommandParameters"] = "Set this option to true to include parameter values when logging commands. This only applies when `LogCommands` is true.",
        ["NpgsqlRest:DebugLogEndpointCreateEvents"] = "Set this option to false to suppress debug-level logs when endpoints are created.\nWhen true (default), debug logs are emitted for each endpoint creation showing URL and method.",
        ["NpgsqlRest:DebugLogCommentAnnotationEvents"] = "Set this option to false to suppress debug-level logs when comment annotations are parsed.\nWhen true (default), debug logs are emitted for each comment annotation that is successfully processed.",
        ["NpgsqlRest:DefaultHttpMethod"] = "When not null, forces a method type for all created endpoints. Method types are `GET`, `PUT`, `POST`, `DELETE`, `HEAD`, `OPTIONS`, `TRACE`, `PATCH` or `CONNECT`. When this value is null (default), the method type is always `GET` when the routine volatility option is not volatile or the routine name starts with, `get_`, contains `_get_` or ends with `_get` (case-insensitive). Otherwise, it is `POST`. This option for individual endpoints can be changed with the `EndpointCreated` function callback, or by using comment annotations.",
        ["NpgsqlRest:DefaultRequestParamType"] = "When not null, sets the request parameter position (request parameter types) for all created endpoints. Values are `QueryString` (parameters are sent using query string) or `BodyJson` (parameters are sent using JSON request body). When this value is null (default), request parameter type is `QueryString` for all `GET` and `DELETE` endpoints, otherwise, request parameter type is `BodyJson`. This option for individual endpoints can be changed with the `EndpointCreated` function callback, or by using comment annotations.",
        ["NpgsqlRest:QueryStringNullHandling"] = "Sets the default behavior for handling NULL values in query string parameters.\n- `Ignore` (default): No special handling - empty strings stay as empty strings, \"null\" literal stays as \"null\" string.\n- `EmptyString`: Empty query string values are interpreted as NULL values. This limits sending empty strings via query strings.\n- `NullLiteral`: Literal string \"null\" (case insensitive) is interpreted as NULL value.\nThis option for individual endpoints can be changed with the `EndpointCreated` function callback, or by using comment annotations.",
        ["NpgsqlRest:TextResponseNullHandling"] = "Sets the default behavior for plain text responses when the execution returns NULL from the database.\n- `EmptyString` (default): Returns an empty string response with status code 200 OK.\n- `NullLiteral`: Returns a string literal \"NULL\" with status code 200 OK.\n- `NoContent`: Returns status code 204 NO CONTENT.\nThis option for individual endpoints can be changed with the `EndpointCreated` function callback, or by using comment annotations.",
        ["NpgsqlRest:RequestHeadersMode"] = "Configure how to send request headers to PostgreSQL routines execution:\n- `Ignore` (default) don't send any request headers to routines.\n- `Context` sets a context variable for the current session `context.headers` containing JSON string with current request headers. This executes `set_config('context.headers', headers, false)` before any routine executions.\n- `Parameter` sends request headers to the routine parameter defined with the `RequestHeadersParameterName` option. Parameter with this name must exist, must be one of the JSON or text types and must have the default value defined. This option for individual endpoints can be changed with the `EndpointCreated` function callback, or by using comment annotations.",
        ["NpgsqlRest:RequestHeadersContextKey"] = "Name of the context variable that will receive the request headers when RequestHeadersMode is set to Context.",
        ["NpgsqlRest:RequestHeadersParameterName"] = "Sets a parameter name that will receive a request headers JSON when the `Parameter` value is used in `RequestHeadersMode` options. A parameter with this name must exist, must be one of the JSON or text types and must have the default value defined. This option for individual endpoints can be changed with the `EndpointCreated` function callback, or by using comment annotations.",
        ["NpgsqlRest:InstanceIdRequestHeaderName"] = "Add the unique NpgsqlRest instance id request header with this name to the response or set to null to ignore.",
        ["NpgsqlRest:CustomRequestHeaders"] = "Custom request headers dictionary that will be added to NpgsqlRest requests. Note: these values are added to the request headers dictionary before they are sent as a context or parameter to the PostgreSQL routine and as such not visible to the browser debugger.",
        ["NpgsqlRest:ExecutionIdHeaderName"] = "Name of the request ID header that will be used to track requests. This is used to correlate requests with server event streaming connection ids.",
        ["NpgsqlRest:DefaultServerSentEventsEventNoticeLevel"] = "Default server-sent event notice message level: INFO, NOTICE, WARNING.\nWhen SSE path is set, generate SSE events for PostgreSQL notice messages with this level or higher.\nThis can be overridden for individual endpoints using comment annotations.",
        ["NpgsqlRest:ServerSentEventsResponseHeaders"] = "Collection of custom server-sent events response headers that will be added to the response when connected to the endpoint that is configured to return server-sent events.",
        ["NpgsqlRest:RoutineOptions"] = "Options for handling PostgreSQL routines (functions and procedures)",
        ["NpgsqlRest:RoutineOptions:Enabled"] = "Set to false to disable the routine source (PostgreSQL functions and procedures). Default is true.",
        ["NpgsqlRest:RoutineOptions:CustomTypeParameterSeparator"] = "Name separator for parameter names when using custom type parameters.\nParameter names will be in the format: {ParameterName}{CustomTypeParameterSeparator}{CustomTypeFieldName}. When NULL, default underscore is used.\nThis is used when using custom types for parameters. For example: with \"create type custom_type1 as (value text);\" and parameter \"_p custom_type1\", this name will be merged into \"_p_value\"",
        ["NpgsqlRest:RoutineOptions:IncludeLanguages"] = "List of PostgreSQL routine language names to include. If NULL, all languages are included. Names are case-insensitive.",
        ["NpgsqlRest:RoutineOptions:ExcludeLanguages"] = "List of PostgreSQL routine language names to exclude. If NULL, \"C\" and \"INTERNAL\" are excluded by default. Names are case-insensitive.",
        ["NpgsqlRest:RoutineOptions:NestedJsonForCompositeTypes"] = "When true, composite type columns in return tables are serialized as nested JSON objects.\nFor example, a table column \"req\" of type \"my_request(id int, name text)\" becomes {\"req\": {\"id\": 1, \"name\": \"test\"}}\ninstead of the default flat structure {\"id\": 1, \"name\": \"test\"}.\nDefault is false for backward compatibility.",
        ["NpgsqlRest:RoutineOptions:ResolveNestedCompositeTypes"] = "When true, nested composite types and arrays of composite types within composite fields\nare serialized as JSON objects/arrays instead of PostgreSQL tuple strings.\nFor example, a nested composite \"(1,x)\" becomes {\"id\":1,\"name\":\"x\"} and\nan array of composites [\"(1,a)\",\"(2,b)\"] becomes [{\"id\":1,\"name\":\"a\"},{\"id\":2,\"name\":\"b\"}].\nDefault is true.",
        ["NpgsqlRest:UploadOptions"] = "Options for different upload handlers and general upload settings",
        ["NpgsqlRest:UploadOptions:DefaultUploadHandler"] = "Handler that will be used when upload handler or handlers are not specified.",
        ["NpgsqlRest:UploadOptions:UseDefaultUploadMetadataParameter"] = "Gets or sets a value indicating whether the default upload metadata parameter should be used.",
        ["NpgsqlRest:UploadOptions:DefaultUploadMetadataParameterName"] = "Name of the default upload metadata parameter. This parameter is used to pass metadata to the upload handler. The metadata is passed as a JSON object.",
        ["NpgsqlRest:UploadOptions:UseDefaultUploadMetadataContextKey"] = "Gets or sets a value indicating whether the default upload metadata context key should be used.",
        ["NpgsqlRest:UploadOptions:DefaultUploadMetadataContextKey"] = "Name of the default upload metadata context key. This key is used to pass the metadata to the upload handler. The metadata is passed as a JSON object.",
        ["NpgsqlRest:UploadOptions:UploadHandlers"] = "Upload handlers specific settings.",
        ["NpgsqlRest:UploadOptions:UploadHandlers:StopAfterFirstSuccess"] = "General settings for all upload handlers",
        ["NpgsqlRest:UploadOptions:UploadHandlers:IncludedMimeTypePatterns"] = "csv string containing mime type patters, set to null to ignore",
        ["NpgsqlRest:UploadOptions:UploadHandlers:ExcludedMimeTypePatterns"] = "csv string containing mime type patters, set to null to ignore",
        ["NpgsqlRest:UploadOptions:UploadHandlers:BufferSize"] = "Buffer size for the upload handlers file_system and large_object, in bytes. Default is 8192 bytes (8 KB).",
        ["NpgsqlRest:UploadOptions:UploadHandlers:TextTestBufferSize"] = "Buffer sample size for testing textual content, in bytes. Default is 4096 bytes (4 KB).",
        ["NpgsqlRest:UploadOptions:UploadHandlers:TextNonPrintableThreshold"] = "Threshold for non-printable characters in the text buffer. Default is 5 non-printable characters.",
        ["NpgsqlRest:UploadOptions:UploadHandlers:AllowedImageTypes"] = "Comma-separated list of allowed image types when checking images.",
        ["NpgsqlRest:UploadOptions:UploadHandlers:RowCommandUserClaimsKey"] = "When set, authenticated user claims are included in the row metadata JSON parameter ($4) under this key name.\nSet to null or empty string to disable adding claims to row metadata. Example: \"claims\" adds {\"claims\": {...}} to metadata.\nAccess in SQL: (_meta->'claims'->>'name_identifier')",
        ["NpgsqlRest:UploadOptions:UploadHandlers:LargeObjectEnabled"] = "Enables upload handlers for the NpgsqlRest endpoints that uses PostgreSQL Large Objects API",
        ["NpgsqlRest:UploadOptions:UploadHandlers:FileSystemEnabled"] = "Enables upload handlers for the NpgsqlRest endpoints that uses file system",
        ["NpgsqlRest:UploadOptions:UploadHandlers:CsvUploadEnabled"] = "Enables upload handlers for the NpgsqlRest endpoints that uploads CSV files to a row command",
        ["NpgsqlRest:UploadOptions:UploadHandlers:CsvUploadRowCommand"] = "$1 - row index (1-based), $2 - parsed value text array, $3 - result of previous row command, $4 - JSON metadata for upload",
        ["NpgsqlRest:UploadOptions:UploadHandlers:ExcelUploadEnabled"] = "Enables upload handlers for the NpgsqlRest endpoints that uploads Excel files to a row command",
        ["NpgsqlRest:UploadOptions:UploadHandlers:ExcelSheetName"] = "null to use the first available",
        ["NpgsqlRest:UploadOptions:UploadHandlers:ExcelUploadRowCommand"] = "$1 - row index (1-based), $2 - parsed value text array, $3 - result of previous row command, $4 - JSON metadata for upload",
        ["NpgsqlRest:TableFormatOptions"] = "Table format handlers for custom rendering of set/record results.\nWhen an endpoint has @table_format = <name> custom parameter, the matching handler renders the response.",
        ["NpgsqlRest:TableFormatOptions:Enabled"] = "Enable or disable table format handlers. When false, @table_format annotations are ignored.",
        ["NpgsqlRest:TableFormatOptions:HtmlEnabled"] = "Built-in HTML table handler. Renders results as an HTML table for easy copy-paste to Excel.\nActivated by @table_format = html annotation on PostgreSQL functions.",
        ["NpgsqlRest:TableFormatOptions:HtmlKey"] = "The key name used to match @table_format = <key> annotation. Default is \"html\".",
        ["NpgsqlRest:TableFormatOptions:HtmlHeader"] = "Content written before the HTML table. Typically a CSS style block.\nSet to null to omit.",
        ["NpgsqlRest:TableFormatOptions:HtmlFooter"] = "Content written after the closing HTML table tag.\nSet to null to omit.",
        ["NpgsqlRest:TableFormatOptions:ExcelEnabled"] = "Built-in Excel (.xlsx) handler using SpreadCheetah. Renders results as an Excel spreadsheet download.\nActivated by @table_format = excel annotation on PostgreSQL functions.",
        ["NpgsqlRest:TableFormatOptions:ExcelKey"] = "The key name used to match @table_format = <key> annotation. Default is \"excel\".",
        ["NpgsqlRest:TableFormatOptions:ExcelSheetName"] = "Worksheet name. When null, uses the routine name.",
        ["NpgsqlRest:TableFormatOptions:ExcelDateTimeFormat"] = "Excel Format Code for DateTime cells. When null, uses SpreadCheetah default (yyyy-MM-dd HH:mm:ss).\nUses Excel Format Codes (not .NET format strings). Examples: \"yyyy-mm-dd\", \"dd/mm/yyyy hh:mm\", \"m/d/yy h:mm\".",
        ["NpgsqlRest:TableFormatOptions:ExcelNumericFormat"] = "Excel Format Code for numeric cells. When null, uses Excel default (General).\nUses Excel Format Codes (not .NET format strings). Examples: \"#,##0.00\", \"0.00\", \"#,##0\".",
        ["NpgsqlRest:AuthenticationOptions"] = "Authentication options for NpgsqlRest endpoints",
        ["NpgsqlRest:AuthenticationOptions:DefaultAuthenticationType"] = "Authentication type used with the Login endpoints to set the authentication type for the new `ClaimsIdentity` created by the login. This value must be set to non-null when using login endpoints, otherwise, the following error will raise: `SignInAsync when principal.Identity.IsAuthenticated is false is not allowed when AuthenticationOptions.RequireAuthenticatedSignIn is true.` If the value is not set and the login endpoint is present, it will automatically get the database name from the connection string.",
        ["NpgsqlRest:AuthenticationOptions:StatusColumnName"] = "The default column name in the data reader which will be used to read the value to determine the success or failure of the login operation. If this column is not present, the success is when the endpoint returns any records. If this column is present, it must be either a boolean to indicate success or a numeric value to indicate the HTTP Status Code to return. If this column is present and retrieves a numeric value, that value is assigned to the HTTP Status Code and the login will authenticate only when this value is 200.",
        ["NpgsqlRest:AuthenticationOptions:SchemeColumnName"] = "The default column name in the data reader which will be used to read the value of the authentication scheme of the login process. If this column is not present in the login response the default authentication scheme is used. Return new value to use a different authentication scheme with the login endpoint.",
        ["NpgsqlRest:AuthenticationOptions:BodyColumnName"] = "The default column name in the data reader which will return a response body message for the login operation where writing to body is possible.",
        ["NpgsqlRest:AuthenticationOptions:ResponseTypeColumnName"] = "The default column name in the data reader which will set the response content type for the login operation where writing to body is possible.",
        ["NpgsqlRest:AuthenticationOptions:HashColumnName"] = "The default column name in the data reader which will be used to read the value of the hash of the password.\nIf this column is present, the value will be used to verify the password from the password parameter.\nPassword parameter is the first parameter which name contains the value of PasswordParameterNameContains.\nIf verification fails, the login will fail and the HTTP Status Code will be set to 404 Not Found.",
        ["NpgsqlRest:AuthenticationOptions:PasswordParameterNameContains"] = "The default name of the password parameter.\nThe first parameter which name contains this value will be used as the password parameter.\nThis is used to verify the password from the password parameter when login endpoint returns a hash of the password (see HashColumnName).",
        ["NpgsqlRest:AuthenticationOptions:DefaultUserIdClaimType"] = "Default claim type for user id.",
        ["NpgsqlRest:AuthenticationOptions:DefaultNameClaimType"] = "Default claim type for username.",
        ["NpgsqlRest:AuthenticationOptions:DefaultRoleClaimType"] = "Default claim type for user roles.",
        ["NpgsqlRest:AuthenticationOptions:DefaultDisplayNameClaimType"] = "Default claim type for user display name.",
        ["NpgsqlRest:AuthenticationOptions:SerializeAuthEndpointsResponse"] = "If true, return any response from auth endpoints (login and logout) if response hasn't been written by auth handler. For cookie auth, this will return full record to response as returned by the routine. For bearer token auth, this will be ignored because bearer token auth writes its own response (with tokens). This option will also be ignored if message column is present (see BodyColumnName option).",
        ["NpgsqlRest:AuthenticationOptions:ObfuscateAuthParameterLogValues"] = "Don't write real parameter values when logging parameters from auth endpoints and obfuscate instead. This prevents user credentials including password from ending up in application logs.",
        ["NpgsqlRest:AuthenticationOptions:PasswordVerificationFailedCommand"] = "Command that is executed when the password verification fails. There are three positional and optional parameters:\n- $1: Authentication scheme used for the login (if parameter exists, type text).\n- $2: User id used for the login (if parameter exists, type text).\n- $3: Username used for the login (if parameter exists, type text).",
        ["NpgsqlRest:AuthenticationOptions:PasswordVerificationSucceededCommand"] = "Command that is executed when the password verification succeeds. There are three positional and optional parameters:\n- $1: authentication scheme used for the login (if parameter exists, type text).\n- $2: user id used for the login (if parameter exists, type text).\n- $3: username used for the login (if parameter exists, type text).",
        ["NpgsqlRest:AuthenticationOptions:UseUserContext"] = "Enable setting authenticated user claims to context variables automatically. See ContextKeyClaimsMapping and ClaimsJsonContextKey options. You can set this individually for each request by using UserContext endpoint property or user_context comment annotation.\nNote: For proxy endpoints, when user_context is enabled, these values are also forwarded as HTTP headers to the upstream proxy using the context key names.",
        ["NpgsqlRest:AuthenticationOptions:ContextKeyClaimsMapping"] = "Mapping of context keys to user claim names. Keys are the context variable names and values are the user claim names. When <see cref=\"UseUserContext\"/> is enabled, the user claims from will be automatically mapped to the context variables.",
        ["NpgsqlRest:AuthenticationOptions:ClaimsJsonContextKey"] = "Context key that is used to set context variable for all available user claims. When this option is not null, and user is authenticated, the user claims will be serialized to JSON value and set to the context variable.",
        ["NpgsqlRest:AuthenticationOptions:IpAddressContextKey"] = "IP address context key that is used to set context variable for the IP address. When this option is not null, the IP address will be set to the context variable when <see cref=\"UseUserContext\"/> is enabled and even when user is not authenticated.",
        ["NpgsqlRest:AuthenticationOptions:UseUserParameters"] = "Enable mapping authenticated user claims to parameters by name automatically. See ParameterNameClaimsMapping and ClaimsJsonParameterName options. You can set this individually for each request by using UseUserParameters endpoint property or user_parameters comment annotation.\nNote: For proxy endpoints, when user_params is enabled, these values are also forwarded as query string parameters to the upstream proxy.",
        ["NpgsqlRest:AuthenticationOptions:ParameterNameClaimsMapping"] = "Mapping of parameter names to user claim names. Keys are the parameter names and values are the user claim names. When <see cref=\"UseUserParameters\"/> is enabled, the user claims from will be automatically mapped to the parameters.",
        ["NpgsqlRest:AuthenticationOptions:ClaimsJsonParameterName"] = "Parameter name that is used to set value for all available user claims. When this option is not null, and user is authenticated, the user claims will be serialized to JSON value and set to the parameter with this name.",
        ["NpgsqlRest:AuthenticationOptions:IpAddressParameterName"] = "IP address parameter name that is used to set parameter value for the IP address. When this option is not null, the IP address will be set to the parameter when <see cref=\"UseUserContext\"/> is enabled and even when user is not authenticated.",
        ["NpgsqlRest:AuthenticationOptions:LoginPath"] = "Url path that will be used for the login endpoint. If NULL, the login endpoint will not be created.\nLogin endpoint expects a PostgreSQL command that will be executed to authenticate the user that follow this convention:\n\n- Must return at least one record when authentication is successful. If no records are returned endpoint will return 401 Unauthorized.\n- If record is returned, the authentication is successful, if not set in StatusColumnName column otherwise.\n- All records will be added to user principal claim collection where column name is claim type and column value is claim value,\nexcept for four special columns defined in StatusColumnName, SchemeColumnName, BodyColumnName and HashColumnName options:\n\n- If \"StatusColumnName\" is present in the returned record, it must be either boolean (true for success, false for failure) or numeric (HTTP Status Code, 200 for success, anything else for failure). If not present, the success is when the endpoint returns any records.\n- If \"SchemeColumnName\" is present in the returned record, it must be text value that defines the authentication scheme to use for the login.\n- If \"BodyColumnName\" is present in the returned record, it must be text value that defines the message to return to the client as response body where possible. This only works for authentication that doesn't write response body (cookie authentication).\n- If \"HashColumnName\" is present in the returned record, it must be text value that defines the hash of the password. Password parameter is the first parameter which name contains the value of PasswordParameterNameContains option. If verification fails, the login will fail and the HTTP Status Code will be set to 404 Not Found.",
        ["NpgsqlRest:AuthenticationOptions:LogoutPath"] = "Url path that will be used for the logout endpoint. If NULL, the logout endpoint will not be created.\nLogin endpoint expects a PostgreSQL command that performs the logout or the sign-out operation.\n\nIf the routine doesn't return any data, the default authorization scheme is signed out.\nAny values returned will be interpreted as scheme names (converted to string) to sign out.",
        ["NpgsqlRest:AuthenticationOptions:BasicAuth"] = "Settings for basic authentication support.\nBasic authentication is a simple authentication scheme built into the HTTP protocol.\nIt expects request header `Authorization: Basic base64(username:password)` where username and password are the credentials for the user.",
        ["NpgsqlRest:AuthenticationOptions:BasicAuth:Enabled"] = "Enable or disable the Basic Authentication support.",
        ["NpgsqlRest:AuthenticationOptions:BasicAuth:Realm"] = "The default realm for the Basic Authentication. If not set, \"NpgsqlRest\" will be used.",
        ["NpgsqlRest:AuthenticationOptions:BasicAuth:Users"] = "Default users dictionary for the Basic Authentication. Key is the username and value is the password or password hash depending on the UseDefaultPasswordHasher option.\nUsers can be set on individual endpoints using multiple annotations: basic_authentication [ username ] [ password ]",
        ["NpgsqlRest:AuthenticationOptions:BasicAuth:SslRequirement"] = "When using Basic Authentication, set this to Required to enforce SSL/TLS connection.\nUse Warning to issue a warning in the log when connection is not secure.\nUse Ignore to allow Basic Authentication (debug level log will show a warning).\nIgnore, Warning, Required",
        ["NpgsqlRest:AuthenticationOptions:BasicAuth:UseDefaultPasswordHasher"] = "Use default password hasher for Basic Authentication to verify the password when Password is set on endpoint or options.\nWhen this is true, Password set in configuration, endpoint or header (depending on PasswordHashLocation) is expected to be a hashed with default hasher.",
        ["NpgsqlRest:AuthenticationOptions:BasicAuth:ChallengeCommand"] = "PostgreSQL command executed when the Basic Authentication is challenged.\nSame convention applies as with \"LoginPath\" command. See \"NpgsqlRest.LoginPath\" option for details.\nUse this command to validate the username and password and/or return user claims.\n\nPositional parameters:\n- $1: Username from basic authentication header (if parameter exists, type text).\n- $2: Password from basic authentication header (if parameter exists, type text).\n- $3: Password is valid, true or false. If endpoint or configuration has a password defined, it will be validated.\nThis the result of that validation or NULL of no password is defined.\nThis allows for password to be validated before the command and use command for additional user claims. (if parameter exists, type boolean).\n- $4: Basic authentication realm (if parameter exists, type text).\n- $5: Endpoint path (if parameter exists, type text).",
        ["NpgsqlRest:HttpFileOptions"] = "Enable or disable the generation of HTTP files for NpgsqlRest endpoints.\nSee more on HTTP files at:\nhttps://marketplace.visualstudio.com/items?itemName=humao.rest-client or\nhttps://learn.microsoft.com/en-us/aspnet/core/test/http-files?view=aspnetcore-8.0",
        ["NpgsqlRest:HttpFileOptions:Option"] = "Options for HTTP file generation:\n- File: Generate HTTP files in the file system.\n- Endpoint: Generate Endpoint(s) with HTTP file(s) content.\n- Both: Generate HTTP files in the file system and Endpoint(s) with HTTP file(s) content.",
        ["NpgsqlRest:HttpFileOptions:Name"] = "File name. If not set, the database name will be used if connection string is set.\nIf neither ConnectionString nor Name is set, the file name will be \"npgsqlrest\".",
        ["NpgsqlRest:HttpFileOptions:NamePattern"] = "The pattern to use when generating file names. {0} is database name, {1} is schema suffix with underline when FileMode is set to Schema.\nUse this property to set a custom file name.\n.http extension will be added automatically.",
        ["NpgsqlRest:HttpFileOptions:CommentHeader"] = "Adds comment header to above request based on PostgreSQL routine.\n- None: skip.\n- Simple: Add name, parameters and return values to comment header. This default.\n- Full: Add the entire routine code as comment header.",
        ["NpgsqlRest:HttpFileOptions:CommentHeaderIncludeComments"] = "When CommentHeader is set to Simple or Full, set to true to include routine comments in comment header.",
        ["NpgsqlRest:HttpFileOptions:FileMode"] = "- Database: to create one http file for entire database.\n- Schema: to create one http file for each schema.",
        ["NpgsqlRest:HttpFileOptions:FileOverwrite"] = "Set to true to overwrite existing files.",
        ["NpgsqlRest:OpenApiOptions"] = "Enable or disable the generation of OpenAPI files for NpgsqlRest endpoints.",
        ["NpgsqlRest:OpenApiOptions:FileName"] = "File name for the generated OpenAPI file. Set to null to skip the file generation.",
        ["NpgsqlRest:OpenApiOptions:UrlPath"] = "URL path for the OpenAPI endpoint. Set to null to skip the endpoint generation.",
        ["NpgsqlRest:OpenApiOptions:FileOverwrite"] = "Set to true to overwrite existing files.",
        ["NpgsqlRest:OpenApiOptions:DocumentTitle"] = "The title of the OpenAPI document. This appears in the \"info\" section of the OpenAPI specification.\nIf not set, the database name from the ConnectionString will be used.",
        ["NpgsqlRest:OpenApiOptions:DocumentVersion"] = "The version of the OpenAPI document. This appears in the \"info\" section of the OpenAPI specification.\nWhen null, default is \"1.0.0\".",
        ["NpgsqlRest:OpenApiOptions:DocumentDescription"] = "Optional description of the API. This appears in the \"info\" section of the OpenAPI specification.",
        ["NpgsqlRest:OpenApiOptions:AddCurrentServer"] = "Include current server information in the \"servers\" section of the OpenAPI document.",
        ["NpgsqlRest:OpenApiOptions:Servers"] = "Additional server entries to add to the \"servers\" section of the OpenAPI document.\nEach server entry must have \"Url\" property and optional \"Description\" property.\napi.example.com\", \"Description\": \"Production server\"}*/],",
        ["NpgsqlRest:OpenApiOptions:SecuritySchemes"] = "Security schemes to include in the OpenAPI document.\nIf not specified, a default Bearer authentication scheme will be added for endpoints requiring authorization.\nSupported types: \"Http\" (for Bearer/Basic auth) and \"ApiKey\" (for Cookie/Header/Query auth).\nExamples:\n- Bearer token: {\"Name\": \"bearerAuth\", \"Type\": \"Http\", \"Scheme\": \"Bearer\", \"BearerFormat\": \"JWT\"}\n- Cookie auth: {\"Name\": \"cookieAuth\", \"Type\": \"ApiKey\", \"In\": \".AspNetCore.Cookies\", \"ApiKeyLocation\": \"Cookie\"}\n- Basic auth: {\"Name\": \"basicAuth\", \"Type\": \"Http\", \"Scheme\": \"Basic\"}",
        ["NpgsqlRest:ClientCodeGen"] = "Enable or disable the generation of TypeScript/Javascript client source code files for NpgsqlRest endpoints.",
        ["NpgsqlRest:ClientCodeGen:FilePath"] = "File path for the generated code. Set to null to skip the code generation. Use {0} to set schema name when BySchema is true",
        ["NpgsqlRest:ClientCodeGen:FileOverwrite"] = "Force file overwrite.",
        ["NpgsqlRest:ClientCodeGen:IncludeHost"] = "Include current host information in the URL prefix.",
        ["NpgsqlRest:ClientCodeGen:CustomHost"] = "Set the custom host prefix information.",
        ["NpgsqlRest:ClientCodeGen:CommentHeader"] = "Adds comment header to above request based on PostgreSQL routine\nSet None to skip.\nSet Simple (default) to add name, parameters and return values to comment header.\nSet Full to add the entire routine code as comment header.",
        ["NpgsqlRest:ClientCodeGen:CommentHeaderIncludeComments"] = "When CommentHeader is set to Simple or Full, set to true to include routine comments in comment header.",
        ["NpgsqlRest:ClientCodeGen:BySchema"] = "Create files by PostgreSQL schema. File name will use formatted FilePath where {0} is the schema name in pascal case.",
        ["NpgsqlRest:ClientCodeGen:IncludeStatusCode"] = "Set to true to include status code in response: {status: response.status, response: model}",
        ["NpgsqlRest:ClientCodeGen:CreateSeparateTypeFile"] = "Create separate file with global types {name}Types.d.ts",
        ["NpgsqlRest:ClientCodeGen:ImportBaseUrlFrom"] = "Module name to import \"baseUrl\" constant, instead of defining it in a module.",
        ["NpgsqlRest:ClientCodeGen:ImportParseQueryFrom"] = "Module name to import \"parseQuery\" function, instead of defining it in a module.",
        ["NpgsqlRest:ClientCodeGen:IncludeParseUrlParam"] = "Include optional parameter `parseUrl: (url: string) => string = url=>url` that will parse the constructed URL.",
        ["NpgsqlRest:ClientCodeGen:IncludeParseRequestParam"] = "Include optional parameter `parseRequest: (request: RequestInit) => RequestInit = request=>request` that will parse the constructed request.",
        ["NpgsqlRest:ClientCodeGen:HeaderLines"] = "Header lines on each auto-generated source file. Default is [\"// autogenerated at {0}\", \"\", \"\"] where {0} is the current timestamp.",
        ["NpgsqlRest:ClientCodeGen:SkipRoutineNames"] = "Array of routine names to skip (without schema)",
        ["NpgsqlRest:ClientCodeGen:SkipFunctionNames"] = "Array of generated function names to skip (without schema)",
        ["NpgsqlRest:ClientCodeGen:SkipPaths"] = "Array of url paths to skip",
        ["NpgsqlRest:ClientCodeGen:SkipSchemas"] = "Array of schema names to skip",
        ["NpgsqlRest:ClientCodeGen:DefaultJsonType"] = "Default TypeScript type for JSON types",
        ["NpgsqlRest:ClientCodeGen:UseRoutineNameInsteadOfEndpoint"] = "Use routine name instead of endpoint name when generating function names.",
        ["NpgsqlRest:ClientCodeGen:ExportUrls"] = "Export URLs as constants in the generated code.",
        ["NpgsqlRest:ClientCodeGen:SkipTypes"] = "Skip generating types and produce pure JavaScript code. Setting this to true will also change the .ts extension to .js where applicable.",
        ["NpgsqlRest:ClientCodeGen:UniqueModels"] = "Keep TypeScript models unique, meaning models with the same fields and types will be merged into one model with the name of the last model. This significantly reduces the number of generated models.",
        ["NpgsqlRest:ClientCodeGen:XsrfTokenHeaderName"] = "Name of the XSRF Token Header (Anti-forgery Token). This is used in FORM POSTS to the server when Anti-forgery is enabled. Currently, only Upload requests use FORM POST.",
        ["NpgsqlRest:ClientCodeGen:ExportEventSources"] = "Export event sources create functions for streaming events.",
        ["NpgsqlRest:ClientCodeGen:CustomImports"] = "List of custom imports to add to the generated code. It adds line to a file. Use full expression like `import { MyType } from './my-type';`",
        ["NpgsqlRest:ClientCodeGen:CustomHeaders"] = "Dictionary of custom headers to add to each request in generated code. Header key is automatically quoted if it doesn't contain quotes.",
        ["NpgsqlRest:ClientCodeGen:IncludeSchemaInNames"] = "When true, include PostgreSQL schema name in the generated type names to avoid name collisions. Set to false to simplify type names when no name collisions are expected.",
        ["NpgsqlRest:ClientCodeGen:ErrorExpression"] = "Expression to parse error response. Only used when IncludeStatusCode is true.",
        ["NpgsqlRest:ClientCodeGen:ErrorType"] = "TypeScript type for error response. Only used when IncludeStatusCode is true.",
        ["NpgsqlRest:HttpClientOptions"] = "HTTP client functionality for annotated composite types.\nAllows PostgreSQL functions to make HTTP requests by using specially annotated types as parameters.",
        ["NpgsqlRest:HttpClientOptions:Enabled"] = "Enable HTTP client functionality for annotated types.",
        ["NpgsqlRest:HttpClientOptions:ResponseStatusCodeField"] = "Default name for the response status code field within annotated types.",
        ["NpgsqlRest:HttpClientOptions:ResponseBodyField"] = "Default name for the response body field within annotated types.",
        ["NpgsqlRest:HttpClientOptions:ResponseHeadersField"] = "Default name for the response headers field within annotated types.",
        ["NpgsqlRest:HttpClientOptions:ResponseContentTypeField"] = "Default name for the response content type field within annotated types.",
        ["NpgsqlRest:HttpClientOptions:ResponseSuccessField"] = "Default name for the response success field within annotated types.",
        ["NpgsqlRest:HttpClientOptions:ResponseErrorMessageField"] = "Default name for the response error message field within annotated types.",
        ["NpgsqlRest:ProxyOptions"] = "Reverse proxy functionality for NpgsqlRest endpoints.\nWhen an endpoint is marked with 'proxy' annotation, incoming requests are forwarded to another URL.",
        ["NpgsqlRest:ProxyOptions:Enabled"] = "Enable proxy functionality for annotated endpoints.",
        ["NpgsqlRest:ProxyOptions:Host"] = "Base URL (host) for proxy requests (e.g., \"https://api.example.com\").\nWhen set, proxy endpoints will forward requests to this host + the original path.",
        ["NpgsqlRest:ProxyOptions:DefaultTimeout"] = "Default timeout for all proxy requests. Format: \"HH:MM:SS\" or PostgreSQL interval.",
        ["NpgsqlRest:ProxyOptions:ForwardHeaders"] = "When true, original request headers are forwarded to the proxy target.",
        ["NpgsqlRest:ProxyOptions:ExcludeHeaders"] = "Headers to exclude from forwarding to the proxy target.",
        ["NpgsqlRest:ProxyOptions:ForwardResponseHeaders"] = "When true, forward response headers from proxy back to client.",
        ["NpgsqlRest:ProxyOptions:ExcludeResponseHeaders"] = "Response headers to exclude from forwarding back to client.",
        ["NpgsqlRest:ProxyOptions:ResponseStatusCodeParameter"] = "Default name for the proxy response status code parameter.",
        ["NpgsqlRest:ProxyOptions:ResponseBodyParameter"] = "Default name for the proxy response body parameter.",
        ["NpgsqlRest:ProxyOptions:ResponseHeadersParameter"] = "Default name for the proxy response headers parameter.",
        ["NpgsqlRest:ProxyOptions:ResponseContentTypeParameter"] = "Default name for the proxy response content type parameter.",
        ["NpgsqlRest:ProxyOptions:ResponseSuccessParameter"] = "Default name for the proxy response success parameter.",
        ["NpgsqlRest:ProxyOptions:ResponseErrorMessageParameter"] = "Default name for the proxy response error message parameter.",
        ["NpgsqlRest:ProxyOptions:ForwardUploadContent"] = "When true, for upload endpoints marked as proxy, the raw multipart/form-data content is forwarded directly to the upstream proxy instead of being processed locally. This allows the upstream service to handle file uploads. When false (default), upload endpoints with proxy annotation will process uploads locally and upload metadata will not be available to the proxy.",
        ["NpgsqlRest:CrudSource"] = "CRUD endpoints for the PostgreSQL tables and views.",
        ["NpgsqlRest:CrudSource:Enabled"] = "Enable or disable the creation of the endpoints for the PostgreSQL tables and views.",
        ["NpgsqlRest:CrudSource:SchemaSimilarTo"] = "Filter schema names similar to this parameter or `null` to ignore this parameter.",
        ["NpgsqlRest:CrudSource:SchemaNotSimilarTo"] = "Filter schema names NOT similar to this parameter or `null` to ignore this parameter.",
        ["NpgsqlRest:CrudSource:IncludeSchemas"] = "List of schema names to be included or `null` to ignore this parameter.",
        ["NpgsqlRest:CrudSource:ExcludeSchemas"] = "List of schema names to be excluded or `null` to ignore this parameter.",
        ["NpgsqlRest:CrudSource:NameSimilarTo"] = "Filter names similar to this parameter or `null` to ignore this parameter.",
        ["NpgsqlRest:CrudSource:NameNotSimilarTo"] = "Filter names NOT similar to this parameter or `null` to ignore this parameter.",
        ["NpgsqlRest:CrudSource:IncludeNames"] = "List of names to be included or `null` to ignore this parameter.",
        ["NpgsqlRest:CrudSource:ExcludeNames"] = "List of names to be excluded or `null` to ignore this parameter.",
        ["NpgsqlRest:CrudSource:CommentsMode"] = "Configure how the comment annotations will behave. `Ignore` will create all endpoints and ignore comment annotations. `ParseAll` will create all endpoints and parse comment annotations to alter the endpoint. `OnlyWithHttpTag` (default) will only create endpoints that contain the `HTTP` tag in the comments and then parse comment annotations.",
        ["NpgsqlRest:CrudSource:ReturningUrlPattern"] = "URL pattern for all \"returning\" endpoints. Parameter is the original URL. Parameter placeholder {0} is default URL.",
        ["NpgsqlRest:CrudSource:OnConflictDoNothingUrlPattern"] = "URL pattern for all \"do nothing\" endpoints. Parameter is the original URL. Parameter placeholder {0} is default URL.",
        ["NpgsqlRest:CrudSource:OnConflictDoNothingReturningUrlPattern"] = "URL pattern for all \"do nothing returning \" endpoints. Parameter is the original URL. Parameter placeholder {0} is default URL.",
        ["NpgsqlRest:CrudSource:OnConflictDoUpdateUrlPattern"] = "URL pattern for all \"do update\" endpoints. Parameter is the original URL. Parameter placeholder {0} is default URL.",
        ["NpgsqlRest:CrudSource:OnConflictDoUpdateReturningUrlPattern"] = "URL pattern for all \"do update returning\" endpoints. Parameter is the original URL. Parameter placeholder {0} is default URL.",
        ["NpgsqlRest:CrudSource:CrudTypes"] = "Set of flags to enable or disable the creation of the CRUD endpoints for the specific types of the PostgreSQL tables and views.\n\nPossible values are:\nSelect, Update, UpdateReturning, Insert, InsertReturning, InsertOnConflictDoNothing, InsertOnConflictDoUpdate, InsertOnConflictDoNothingReturning,\nInsertOnConflictDoUpdateReturning, Delete, DeleteReturning, All",
        ["NpgsqlRest:SqlFileSource"] = "SQL file source for generating REST API endpoints from .sql files.",
        ["NpgsqlRest:SqlFileSource:Enabled"] = "Enable or disable SQL file source endpoints. Default is false.",
        ["NpgsqlRest:SqlFileSource:FilePattern"] = "Glob pattern for SQL files, e.g. \"sql/**/*.sql\", \"queries/*.sql\".\nSupports * (any chars), ** (recursive, any including /), ? (single char).\nEmpty string disables the feature.",
        ["NpgsqlRest:SqlFileSource:CommentsMode"] = "How comment annotations are processed for SQL file endpoints.\nPossible values: Ignore, ParseAll, OnlyWithHttpTag.\nDefault: OnlyWithHttpTag — SQL files must contain an explicit HTTP annotation (e.g., '-- HTTP GET') to become endpoints.",
        ["NpgsqlRest:SqlFileSource:CommentScope"] = "Which comments in the SQL file to parse as annotations.\nPossible values: All (default — all comments), Header (only comments before the first statement).",
        ["NpgsqlRest:SqlFileSource:ErrorMode"] = "Behavior when a SQL file fails to parse or describe.\nPossible values: Exit (default — log error, exit process), Skip (log error, continue).",
        ["NpgsqlRest:SqlFileSource:ResultPrefix"] = "Prefix for result keys in multi-command JSON responses.\nDefault keys are \"result1\", \"result2\", etc. Override per-result with @resultN annotation in the SQL file.",
        ["NpgsqlRest:SqlFileSource:UnnamedSingleColumnSet"] = "When true, queries returning a single column produce a flat JSON array of values (e.g., [\"a\", \"b\", \"c\"]) instead of an array of objects (e.g., [{\"col\": \"a\"}, {\"col\": \"b\"}]). This matches the behavior of PostgreSQL functions returning setof single values. Default is true.",
        ["NpgsqlRest:SqlFileSource:NestedJsonForCompositeTypes"] = "When true, composite type columns in return results are serialized as nested JSON objects.\nFor example, a column \"data\" of type \"my_type(id int, name text)\" becomes {\"data\": {\"id\": 1, \"name\": \"test\"}}\ninstead of the default flat structure {\"id\": 1, \"name\": \"test\"}.\nDefault is false for backward compatibility. Can also be enabled per-endpoint with the 'nested' annotation.",
    };

    /// <summary>
    /// Known enum fields mapped by their config path to valid string values.
    /// </summary>
    private static readonly Dictionary<string, string[]> EnumFields = new(StringComparer.OrdinalIgnoreCase)
    {
        // Config section
        ["Config:ValidateConfigKeys"] = ["Ignore", "Warning", "Error"],

        // DataProtection
        ["DataProtection:Storage"] = ["Default", "FileSystem", "Database"],
        ["DataProtection:KeyEncryption"] = ["None", "Certificate", "Dpapi"],

        // Log
        ["Log:ConsoleMinimumLevel"] = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"],
        ["Log:FileMinimumLevel"] = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"],
        ["Log:PostgresMinimumLevel"] = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"],
        ["Log:OTLPMinimumLevel"] = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"],
        ["Log:OTLPProtocol"] = ["Grpc", "HttpProtobuf"],

        // ResponseCompression
        ["ResponseCompression:CompressionLevel"] = ["Optimal", "Fastest", "NoCompression", "SmallestSize"],

        // Auth:PasskeyAuth
        ["Auth:PasskeyAuth:UserVerificationRequirement"] = ["preferred", "required", "discouraged"],
        ["Auth:PasskeyAuth:ResidentKeyRequirement"] = ["preferred", "required", "discouraged"],
        ["Auth:PasskeyAuth:AttestationConveyance"] = ["none", "indirect", "direct", "enterprise"],

        // Auth:External:BasicAuth
        ["NpgsqlRest:AuthenticationOptions:BasicAuth:SslRequirement"] = ["Required", "NotRequired", "None"],

        // CacheOptions
        ["CacheOptions:Type"] = ["Memory", "Redis", "Hybrid"],

        // NpgsqlRest core
        ["NpgsqlRest:CommentsMode"] = ["Ignore", "ParseAll", "OnlyWithHttpTag"],
        ["NpgsqlRest:DefaultHttpMethod"] = ["GET", "PUT", "POST", "DELETE", "PATCH", "HEAD", "OPTIONS"],
        ["NpgsqlRest:DefaultRequestParamType"] = ["QueryString", "BodyJson"],
        ["NpgsqlRest:QueryStringNullHandling"] = ["Ignore", "EmptyString", "NullLiteral"],
        ["NpgsqlRest:TextResponseNullHandling"] = ["EmptyString", "NullLiteral", "NoContent"],
        ["NpgsqlRest:RequestHeadersMode"] = ["Ignore", "Context", "Parameter"],
        ["NpgsqlRest:LogConnectionNoticeEventsMode"] = ["FirstStackFrameAndMessage", "FullStackTrace", "MessageOnly"],
        ["NpgsqlRest:DefaultServerSentEventsEventNoticeLevel"] = ["DEBUG", "LOG", "INFO", "NOTICE", "WARNING"],

        // NpgsqlRest sub-options
        ["NpgsqlRest:HttpFileOptions:Option"] = ["File", "Console", "Both"],
        ["NpgsqlRest:HttpFileOptions:CommentHeader"] = ["None", "Simple", "Full"],
        ["NpgsqlRest:HttpFileOptions:FileMode"] = ["Database", "Schema"],
        ["NpgsqlRest:ClientCodeGen:CommentHeader"] = ["None", "Simple", "Full"],
        ["NpgsqlRest:CrudSource:CommentsMode"] = ["Ignore", "ParseAll", "OnlyWithHttpTag"],

        // ConnectionSettings
        ["ConnectionSettings:MultiHostConnectionTargets:Default"] = ["Any", "Primary", "Standby", "PreferPrimary", "PreferStandby", "ReadWrite", "ReadOnly"],

        // Stats
        ["Stats:OutputFormat"] = ["json", "html", "tsv"],

        // RateLimiterOptions policies
        ["RateLimiterOptions:Policies:Type"] = ["FixedWindow", "SlidingWindow", "TokenBucket", "Concurrency"],

        // ValidationOptions rules
        ["ValidationOptions:Rules:Type"] = ["NotNull", "NotEmpty", "Required", "Regex", "MinLength", "MaxLength", "Range"],
    };

    private static void AddDescription(JsonObject schema, string path)
    {
        if (DescriptionFields.TryGetValue(path, out var description))
        {
            schema["description"] = description;
        }
    }

    public static JsonObject Generate()
    {
        var defaults = ConfigDefaults.GetDefaults();
        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft-07/schema#",
            ["title"] = "NpgsqlRest Configuration",
            ["description"] = "Configuration schema for NpgsqlRest appsettings.json",
            ["type"] = "object",
            ["properties"] = GenerateProperties(defaults, "")
        };
        return schema;
    }

    private static JsonObject GenerateProperties(JsonObject obj, string parentPath)
    {
        var properties = new JsonObject();
        foreach (var kvp in obj)
        {
            var path = string.IsNullOrEmpty(parentPath) ? kvp.Key : $"{parentPath}:{kvp.Key}";
            properties[kvp.Key] = GeneratePropertySchema(kvp.Value, path);
        }
        return properties;
    }

    private static JsonNode GeneratePropertySchema(JsonNode? value, string path)
    {
        if (value is null)
        {
            // Null default — check if it's a known enum
            if (EnumFields.TryGetValue(path, out var enumValues))
            {
                var schema = new JsonObject();
                var enumArray = new JsonArray();
                enumArray.Add((JsonNode?)null);
                foreach (var v in enumValues) enumArray.Add((JsonNode)v);
                schema["enum"] = enumArray;
                schema["default"] = null;
                AddDescription(schema, path);
                return schema;
            }
            // Nullable string by default
            var nullSchema = new JsonObject
            {
                ["type"] = new JsonArray { (JsonNode)"string", (JsonNode)"null" },
                ["default"] = null
            };
            AddDescription(nullSchema, path);
            return nullSchema;
        }

        if (value is JsonObject childObj)
        {
            var schema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = GenerateProperties(childObj, path)
            };
            AddDescription(schema, path);
            return schema;
        }

        if (value is JsonArray childArr)
        {
            var schema = new JsonObject
            {
                ["type"] = "array"
            };

            if (childArr.Count > 0)
            {
                var firstItem = childArr[0];
                if (firstItem is JsonObject itemObj)
                {
                    schema["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = GenerateProperties(itemObj, path)
                    };
                }
                else
                {
                    schema["items"] = InferValueSchema(firstItem, path);
                }
            }

            // Include default array values
            var defaultArr = new JsonArray();
            foreach (var item in childArr)
            {
                defaultArr.Add(item?.DeepClone());
            }
            schema["default"] = defaultArr;
            AddDescription(schema, path);
            return schema;
        }

        // Primitive value
        return InferValueSchema(value, path);
    }

    private static JsonObject InferValueSchema(JsonNode? value, string path)
    {
        var schema = new JsonObject();

        // Check for known enum
        if (EnumFields.TryGetValue(path, out var enumValues))
        {
            var enumArray = new JsonArray();
            foreach (var v in enumValues) enumArray.Add((JsonNode)v);
            schema["enum"] = enumArray;
            if (value is not null) schema["default"] = value.DeepClone();
            AddDescription(schema, path);
            return schema;
        }

        if (value is null)
        {
            schema["type"] = new JsonArray { (JsonNode)"string", (JsonNode)"null" };
            AddDescription(schema, path);
            return schema;
        }

        if (value is JsonValue jv)
        {
            if (jv.TryGetValue<bool>(out var boolVal))
            {
                schema["type"] = "boolean";
                schema["default"] = boolVal;
            }
            else if (jv.TryGetValue<int>(out var intVal))
            {
                schema["type"] = "integer";
                schema["default"] = intVal;
            }
            else if (jv.TryGetValue<long>(out var longVal))
            {
                schema["type"] = "integer";
                schema["default"] = longVal;
            }
            else if (jv.TryGetValue<double>(out var doubleVal))
            {
                schema["type"] = "number";
                schema["default"] = doubleVal;
            }
            else if (jv.TryGetValue<string>(out var strVal))
            {
                schema["type"] = "string";
                schema["default"] = strVal;
            }
            else
            {
                schema["type"] = "string";
                schema["default"] = value.DeepClone();
            }
        }

        AddDescription(schema, path);
        return schema;
    }
}
