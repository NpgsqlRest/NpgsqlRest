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

    /// <summary>serverInfo.version reported in the initialize handshake. Null = application version.</summary>
    public string? ServerVersion { get; set; } = null;

    /// <summary>Optional server-level instructions returned in the initialize handshake.</summary>
    public string? Instructions { get; set; } = null;
}
