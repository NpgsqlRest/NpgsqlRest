namespace NpgsqlRest.Mcp;

/// <summary>
/// OAuth 2.1 Resource Server settings for the MCP endpoint (bring-your-own Authorization Server).
/// Token validation reuses the host's bearer authentication; these keys drive the transport gate
/// and the Protected Resource Metadata document (RFC 9728 / RFC 8707). NpgsqlRest does not act as an
/// Authorization Server — point <see cref="AuthorizationServers"/> at an external IdP (or NpgsqlRest's
/// own JWT login acting separately).
/// </summary>
public class McpAuthorizationOptions
{
    /// <summary>
    /// True = every MCP request requires an authenticated principal (the host's bearer middleware must
    /// have populated <c>context.User</c>). False (default) = anonymous allowed; a tool's own
    /// <c>authorize</c> annotation still gates it per call.
    /// </summary>
    public bool RequireAuthorization { get; set; } = false;

    /// <summary>Authorization Server issuer URL(s) advertised in the Protected Resource Metadata. When empty, no PRM document is served.</summary>
    public string[] AuthorizationServers { get; set; } = [];

    /// <summary>Optional scopes advertised in the Protected Resource Metadata (<c>scopes_supported</c>).</summary>
    public string[] ScopesSupported { get; set; } = [];

    /// <summary>
    /// Canonical resource URI tokens must target (RFC 8707 audience) and the <c>resource</c> value in the
    /// PRM document. Null = derived from the request (scheme + host + <see cref="McpOptions.UrlPath"/>).
    /// </summary>
    public string? Audience { get; set; } = null;

    /// <summary>
    /// Path the Protected Resource Metadata document is served at. Null = the RFC 9728 well-known path
    /// derived from the MCP URL path (<c>/.well-known/oauth-protected-resource</c> + UrlPath).
    /// </summary>
    public string? ProtectedResourceMetadataPath { get; set; } = null;

    /// <summary>
    /// When true, <c>tools/list</c> hides tools the calling principal could not run (their routine's
    /// <c>authorize</c>/role check would deny it). False (default) lists every opted-in tool — keeping
    /// them discoverable — and authorization is enforced on <c>tools/call</c> regardless.
    /// </summary>
    public bool FilterToolsByRole { get; set; } = false;
}
