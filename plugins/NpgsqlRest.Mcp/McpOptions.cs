namespace NpgsqlRest.Mcp;

/// <summary>
/// Options for the MCP (Model Context Protocol) server plugin. Disabled by default. Tools are never
/// auto-exposed — only routines opted in with the <c>mcp</c> comment annotation become MCP tools.
/// (Serving-related options — auth/PRM, structured content, filters — are added with the /mcp
/// endpoint in a later increment.)
/// </summary>
public class McpOptions
{
    /// <summary>Enable or disable the MCP endpoint. Disabled by default.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>URL path for the MCP endpoint (Streamable HTTP, single endpoint).</summary>
    public string UrlPath { get; set; } = "/mcp";

    /// <summary>serverInfo.name reported in the initialize handshake. Null = database name (or "NpgsqlRest").</summary>
    public string? ServerName { get; set; } = null;

    /// <summary>serverInfo.version reported in the initialize handshake. Defaults to "1.0.0"; a null/blank value also falls back to "1.0.0".</summary>
    public string? ServerVersion { get; set; } = "1.0.0";

    /// <summary>Optional server-level instructions returned in the initialize handshake.</summary>
    public string? Instructions { get; set; } = null;

    /// <summary>
    /// Optional text appended (as a suffix) to every MCP tool's description. Null = no-op. Use for short,
    /// shared per-tool context the agent should always see (e.g. "Read-only Acme CRM."). For longer
    /// server-wide guidance prefer <see cref="Instructions"/>, which is returned once at initialize.
    /// </summary>
    public string? ToolDescriptionSuffix { get; set; } = null;

    /// <summary>
    /// Name of an ASP.NET rate-limiter policy applied to the whole <c>/mcp</c> endpoint. Null = no limiting.
    /// A routine's own <c>rate_limiter</c> annotation does not carry to MCP (tools/call bypasses route
    /// middleware), so this is how MCP traffic is throttled. The named policy must be registered on the
    /// host (<c>AddRateLimiter(o =&gt; o.AddPolicy("name", …))</c> + <c>UseRateLimiter()</c>); an unregistered
    /// name surfaces as the framework's error when a request hits the endpoint.
    /// </summary>
    public string? RateLimiterPolicy { get; set; } = null;

    /// <summary>OAuth 2.1 Resource Server settings: the transport authorization gate and Protected Resource Metadata (RFC 9728).</summary>
    public McpAuthorizationOptions Authorization { get; set; } = new();

    /// <summary>
    /// Allowed values of the HTTP <c>Origin</c> header (DNS-rebinding protection, required by the
    /// Streamable HTTP transport). A request whose <c>Origin</c> is present but matches neither this
    /// list nor the server's own origin is rejected with 403. Requests without an <c>Origin</c> header
    /// (e.g. server-to-server) are allowed. Empty (default) = only same-origin browser requests pass.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];
}
