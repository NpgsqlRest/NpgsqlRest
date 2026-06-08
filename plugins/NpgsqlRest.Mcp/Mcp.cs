using NpgsqlRest.Common;

namespace NpgsqlRest.Mcp;

/// <summary>
/// MCP server plugin. Projects opted-in PostgreSQL routines as MCP tools.
///
/// Annotation layer (this increment): claims the <c>mcp*</c> comment annotations during core's
/// single comment-parse pass (<see cref="HandleCommentLine"/>), records per-endpoint metadata in
/// <see cref="RoutineEndpoint.Items"/> (<see cref="McpToolInfo"/>), and applies MCP-only routing by
/// setting <see cref="RoutineEndpoint.InternalOnly"/>. Catalog generation and the /mcp endpoint are
/// added in later increments.
///
/// Annotations:
///   mcp              opt this routine in as an MCP tool; description = comment prose (derived)
///   mcp &lt;text&gt;       opt in; &lt;text&gt; is the tool description (overrides derived prose)
///   mcp_name name    explicit tool name (otherwise derived from the routine name)
///
/// MCP-only (tool exposed, no public HTTP route) is composed with the core `internal` annotation:
/// `mcp` + `internal`. (With <see cref="CommentsMode.OnlyAnnotated"/>, `mcp` alone creates the
/// endpoint even without an HTTP tag.)
/// </summary>
public class Mcp(McpOptions options) : IEndpointCreateHandler
{
    /// <summary>Key under which <see cref="McpToolInfo"/> is stored in <see cref="RoutineEndpoint.Items"/>.</summary>
    public const string ItemsKey = "mcp";

    private readonly McpOptions _options = options;

    public CommentLineResult? HandleCommentLine(RoutineEndpoint endpoint, string line, string[] words, string[] wordsLower)
    {
        if (wordsLower.Length == 0)
        {
            return null;
        }

        var key = wordsLower[0];

        // mcp_name <name>
        if (CommentPrimitives.StrEquals(key, "mcp_name"))
        {
            var info = GetOrAdd(endpoint);
            info.Enabled = true;
            if (words.Length > 1)
            {
                info.ToolName = words[1];
            }
            return new CommentLineResult(string.Concat("mcp_name ", info.ToolName), RequestsEndpoint: true);
        }

        // mcp | mcp <text>  — text (rest of line, case preserved) becomes the tool description.
        if (CommentPrimitives.StrEquals(key, "mcp"))
        {
            var info = GetOrAdd(endpoint);
            info.Enabled = true;
            var text = RemainderAfterFirstWord(line);
            if (!string.IsNullOrWhiteSpace(text))
            {
                info.Description = text;
                return new CommentLineResult(string.Concat("mcp: ", text), RequestsEndpoint: true);
            }
            return new CommentLineResult("mcp", RequestsEndpoint: true);
        }

        return null;
    }

    private static McpToolInfo GetOrAdd(RoutineEndpoint endpoint)
    {
        if (endpoint.TryGetItem(ItemsKey, out var value) && value is McpToolInfo existing)
        {
            return existing;
        }
        var info = new McpToolInfo();
        endpoint.Items[ItemsKey] = info;
        return info;
    }

    private static string? RemainderAfterFirstWord(string line)
    {
        var idx = line.IndexOfAny([' ', '\t']);
        return idx < 0 ? null : line[(idx + 1)..].Trim();
    }
}
