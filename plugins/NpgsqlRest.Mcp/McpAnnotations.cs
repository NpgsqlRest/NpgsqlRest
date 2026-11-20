namespace NpgsqlRest.Mcp;

/// <summary>The comment annotations the MCP plugin reads, as annotation reference data.</summary>
public static class McpAnnotations
{
    public static CommentAnnotation[] All =>
    [
        new()
        {
            Name = "mcp",
            Aliases = ["mcp"],
            Syntax = "mcp [description]",
            Description = "Expose the routine as an MCP tool (Model Context Protocol). Text after the keyword is the tool's description. Without an HTTP tag the routine becomes an MCP-only tool with no public HTTP route. Description precedence: mcp_description, then this text, then the comment's prose.",
            Owner = "mcp",
            Example = "mcp Returns the orders of one customer"
        },
        new()
        {
            Name = "mcp_name",
            Aliases = ["mcp_name"],
            Syntax = "mcp_name <name>",
            Description = "Expose the routine as an MCP tool under this name (one word). Default: the routine's name.",
            Owner = "mcp",
            ValueKind = AnnotationValueKind.Name,
            Example = "mcp_name customer_orders"
        },
        new()
        {
            Name = "mcp_description",
            Aliases = ["mcp_description", "mcp_desc"],
            Syntax = "mcp_description <text>",
            Description = "Expose the routine as an MCP tool with this description, which wins over the text after mcp and over the comment's prose.",
            Owner = "mcp",
            Example = "mcp_description Returns the orders of one customer, newest first"
        },
    ];
}
