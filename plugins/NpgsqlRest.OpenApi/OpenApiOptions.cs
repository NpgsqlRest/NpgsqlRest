namespace NpgsqlRest.OpenAPI;

public class OpenApiOptions
{
    /// <summary>
    /// The file name to use for the OpenAPI document. If null, no file will be generated.
    /// You can use a relative urlPath (e.g., "docs/openapi.json") or just a file name.
    /// </summary>
    public string? FileName { get; set; } = null;

    /// <summary>
    /// The urlPath to serve the OpenAPI document at. If null, the document will not be served.
    /// Example: "/openapi.json" or "/api/docs/openapi.json"
    /// </summary>
    public string? UrlPath { get; set; } = null;

    /// <summary>
    /// Set to true to overwrite existing files. Default is false.
    /// </summary>
    public bool FileOverwrite { get; set; } = false;

    /// <summary>
    /// The title of the OpenAPI document.
    /// This appears in the "info" section of the OpenAPI specification.
    /// If not set, the database name from the ConnectionString will be used.
    /// </summary>
    public string? DocumentTitle { get; set; } = null;

    /// <summary>
    /// The version of the OpenAPI document.
    /// This appears in the "info" section of the OpenAPI specification.
    /// </summary>
    public string DocumentVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Optional description of the API.
    /// This appears in the "info" section of the OpenAPI specification.
    /// </summary>
    public string? DocumentDescription { get; set; } = null;
    
    /// <summary>
    /// The connection string to the database used in NpgsqlRest.
    /// Used to get the name of the database for the DocumentTitle.
    /// If DocumentTitle is set, this property is ignored.
    /// If null, and DocumentTitle is not set, a default title "NpgsqlRest API" will be used.
    /// </summary>
    public string? ConnectionString { get; set; } = null;

    /// <summary>
    /// Optional servers array for the OpenAPI document.
    /// If null, and AddCurrentServer is false, no servers section will be added.
    /// If null, but AddCurrentServer is true, the current application URL will be added automatically.
    /// Example: new[] { new OpenApiServer { Url = "https://api.example.com", Description = "Production" } }
    /// </summary>
    public OpenApiServer[]? Servers { get; set; } = null;

    /// <summary>
    /// If true, automatically adds the current application URL to the servers array.
    /// Default is true.
    /// </summary>
    public bool AddCurrentServer { get; set; } = true;

    /// <summary>
    /// Security schemes to include in the OpenAPI document.
    /// If null or empty, a default Bearer authentication scheme will be added for endpoints requiring authorization.
    /// </summary>
    public OpenApiSecurityScheme[]? SecuritySchemes { get; set; } = null;

    /// <summary>
    /// Schema-name allow-list. When non-null and non-empty, only endpoints whose routine schema appears
    /// in this list are documented; all others are skipped. Comparison is case-sensitive
    /// (PostgreSQL identifiers are already lowercased unless quoted). Combine with
    /// <see cref="ExcludeSchemas"/> for fine-grained control — both filters apply.
    /// Default null = document every schema (existing behavior).
    /// </summary>
    public string[]? IncludeSchemas { get; set; } = null;

    /// <summary>
    /// Schema-name deny-list. When non-null and non-empty, any endpoint whose routine schema appears
    /// in this list is skipped. Applied alongside <see cref="IncludeSchemas"/> — both must pass.
    /// Default null = no schema exclusions.
    /// </summary>
    public string[]? ExcludeSchemas { get; set; } = null;

    /// <summary>
    /// PostgreSQL-style SIMILAR TO pattern matched against the routine name. When set, only endpoints
    /// whose name matches the pattern are documented. <c>_</c> matches any single character and <c>%</c>
    /// matches any sequence (including empty); other PostgreSQL SIMILAR TO meta-characters (<c>|</c>,
    /// <c>*</c>, <c>+</c>, <c>?</c>, <c>(...)</c>, <c>[...]</c>) work via translation to .NET regex.
    /// The match is anchored — the pattern must cover the entire name. Default null = no name filter.
    /// </summary>
    public string? NameSimilarTo { get; set; } = null;

    /// <summary>
    /// PostgreSQL-style SIMILAR TO pattern matched against the routine name; matches are excluded from
    /// the OpenAPI document. Same syntax as <see cref="NameSimilarTo"/>. Applied alongside
    /// <see cref="NameSimilarTo"/> — both must pass. Default null = no name exclusion.
    /// </summary>
    public string? NameNotSimilarTo { get; set; } = null;

    /// <summary>
    /// When true, only endpoints that require authorization (<see cref="RoutineEndpoint.RequiresAuthorization"/>)
    /// are documented. Anonymous endpoints — typically health checks, login, internal probes — are
    /// omitted. Default false = document everything. Useful for partner-facing documents where the
    /// internal anonymous surface should not be advertised.
    /// </summary>
    public bool RequiresAuthorizationOnly { get; set; } = false;
}

/// <summary>
/// Represents a server in the OpenAPI servers array.
/// </summary>
public class OpenApiServer
{
    /// <summary>
    /// The URL of the server. Required.
    /// Example: "https://api.example.com" or "http://localhost:8080"
    /// </summary>
    public required string Url { get; set; }

    /// <summary>
    /// Optional description of the server.
    /// Example: "Production server" or "Development server"
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Represents the type of security scheme in OpenAPI.
/// </summary>
public enum OpenApiSecuritySchemeType
{
    /// <summary>
    /// HTTP authentication including Basic and Bearer
    /// </summary>
    Http,
    /// <summary>
    /// API Key authentication (in header, query, or cookie)
    /// </summary>
    ApiKey
}

/// <summary>
/// Represents an HTTP authentication scheme (for OpenApiSecuritySchemeType.Http)
/// </summary>
public enum HttpAuthScheme
{
    /// <summary>
    /// HTTP Basic authentication
    /// </summary>
    Basic,
    /// <summary>
    /// HTTP Bearer token authentication (e.g., JWT)
    /// </summary>
    Bearer
}

/// <summary>
/// Represents the location of an API key (for OpenApiSecuritySchemeType.ApiKey)
/// </summary>
public enum ApiKeyLocation
{
    /// <summary>
    /// API key in header
    /// </summary>
    Header,
    /// <summary>
    /// API key in query string
    /// </summary>
    Query,
    /// <summary>
    /// API key in cookie
    /// </summary>
    Cookie
}

/// <summary>
/// Represents a security scheme in the OpenAPI document.
/// Maps to OpenAPI 3.0 Security Scheme Object.
/// </summary>
public class OpenApiSecurityScheme
{
    /// <summary>
    /// The name of the security scheme (used as the key in securitySchemes).
    /// Required.
    /// Example: "bearerAuth", "cookieAuth"
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// The type of the security scheme.
    /// Required.
    /// </summary>
    public required OpenApiSecuritySchemeType Type { get; set; }

    /// <summary>
    /// A short description for the security scheme.
    /// </summary>
    public string? Description { get; set; }

    // --- For Http type ---

    /// <summary>
    /// The name of the HTTP Authorization scheme.
    /// Required when Type is Http.
    /// Example: HttpAuthScheme.Basic or HttpAuthScheme.Bearer
    /// </summary>
    public HttpAuthScheme? Scheme { get; set; }

    /// <summary>
    /// A hint to the client to identify how the bearer token is formatted.
    /// Optional, used with Bearer scheme.
    /// Example: "JWT"
    /// </summary>
    public string? BearerFormat { get; set; }

    // --- For ApiKey type ---

    /// <summary>
    /// The name of the header, query or cookie parameter.
    /// Required when Type is ApiKey.
    /// Example: "X-API-Key", ".AspNetCore.Cookies"
    /// </summary>
    public string? In { get; set; }

    /// <summary>
    /// The location of the API key (header, query, or cookie).
    /// Required when Type is ApiKey.
    /// </summary>
    public ApiKeyLocation? ApiKeyLocation { get; set; }
}
