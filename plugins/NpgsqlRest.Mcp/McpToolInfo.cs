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

    /// <summary>Explicit tool description from <c>mcp_description</c> / <c>mcp_desc</c>; null = derive
    /// from the comment prose (<see cref="RoutineEndpoint.UnhandledCommentLines"/>).</summary>
    public string? Description { get; set; }
}
