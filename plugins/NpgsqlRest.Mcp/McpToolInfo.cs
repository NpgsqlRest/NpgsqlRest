namespace NpgsqlRest.Mcp;

/// <summary>
/// Per-endpoint MCP metadata, parsed from the routine's <c>mcp*</c> comment annotations and stored
/// in <see cref="RoutineEndpoint.Items"/> under the <see cref="Mcp.ItemsKey"/> key. Read later when
/// building the MCP tool catalog. (MCP-only routing is applied directly via
/// <see cref="RoutineEndpoint.InternalOnly"/>, not stored here.)
/// </summary>
public sealed class McpToolInfo
{
    /// <summary>This routine is opted in as an MCP tool (any <c>mcp*</c> annotation sets this).</summary>
    public bool Enabled { get; set; }

    /// <summary>Explicit tool name from <c>mcp_name</c>; null = derive from the routine name.</summary>
    public string? ToolName { get; set; }

    /// <summary>Explicit, authoritative tool description from <c>mcp_description</c> / <c>mcp_desc</c>.
    /// When set it wins over everything and suppresses the comment-prose fallback.</summary>
    public string? Description { get; set; }

    /// <summary>Inline description from <c>mcp &lt;text&gt;</c>. Used when <see cref="Description"/> is not
    /// set; like it, an inline description is explicit and suppresses the comment-prose fallback.</summary>
    public string? InlineText { get; set; }
}
