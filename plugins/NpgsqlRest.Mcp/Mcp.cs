using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NpgsqlRest.Common;
using static NpgsqlRest.NpgsqlRestOptions;

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
public partial class Mcp(McpOptions options) : IEndpointCreateHandler
{
    /// <summary>Key under which <see cref="McpToolInfo"/> is stored in <see cref="RoutineEndpoint.Items"/>.</summary>
    public const string ItemsKey = "mcp";

    private readonly McpOptions _options = options;

    private readonly Dictionary<string, JsonObject> _tools = new(StringComparer.Ordinal);

    // tool name → endpoint, for tools/call execution.
    private readonly Dictionary<string, RoutineEndpoint> _toolEndpoints = new(StringComparer.Ordinal);

    /// <summary>
    /// The MCP tool catalog, keyed by tool name. Built during endpoint creation (<see cref="Handle"/>)
    /// from the routines opted in via the `mcp` annotation. Each value is a tools/list `Tool` object
    /// (name, description, inputSchema, annotations).
    /// </summary>
    public IReadOnlyDictionary<string, JsonObject> Tools => _tools;

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

    public void Handle(RoutineEndpoint endpoint)
    {
        if (!endpoint.TryGetItem(ItemsKey, out var value) || value is not McpToolInfo info || !info.Enabled)
        {
            return;
        }

        var tool = BuildTool(endpoint, info);
        var name = tool["name"]!.GetValue<string>();
        WarnIfNonApplicableFeature(endpoint, name);
        if (_tools.TryAdd(name, tool))
        {
            _toolEndpoints[name] = endpoint;
        }
        else
        {
            // Tool names must be unique (e.g. overloaded routines collide). Keep the first; log the rest.
            // TODO: overload disambiguation (mcp_name, or a typed/arity suffix).
            Logger?.LogWarning("MCP tool name '{Name}' is already in use ({Schema}.{Routine} skipped).",
                name, endpoint.Routine.Schema, endpoint.Routine.Name);
        }
    }

    /// <summary>
    /// Warns when a routine opted in with <c>mcp</c> also carries a feature that does not translate to an
    /// MCP tool call (auth flows, file upload, SSE). The tool is still exposed — this only flags that it
    /// likely won't behave as expected over JSON-RPC.
    /// </summary>
    private static void WarnIfNonApplicableFeature(RoutineEndpoint endpoint, string toolName)
    {
        var feature =
            endpoint.Login ? "login" :
            endpoint.Logout ? "logout" :
            endpoint.BasicAuth is not null ? "basic auth" :
            endpoint.Upload ? "upload" :
            endpoint.SseEventsPath is not null ? "server-sent events" :
            null;
        if (feature is not null)
        {
            Logger?.LogWarning(
                "MCP tool '{Name}' ({Schema}.{Routine}) is annotated `mcp` but uses a feature that does not apply to MCP tools ({Feature}). The tool is exposed but may not behave as expected over JSON-RPC.",
                toolName, endpoint.Routine.Schema, endpoint.Routine.Name, feature);
        }
    }

    private JsonObject BuildTool(RoutineEndpoint endpoint, McpToolInfo info)
    {
        var routine = endpoint.Routine;
        var name = string.IsNullOrWhiteSpace(info.ToolName) ? routine.Name : info.ToolName!;

        var description = info.Description;
        if (string.IsNullOrWhiteSpace(description))
        {
            description = DeriveDescription(endpoint);
        }
        if (string.IsNullOrWhiteSpace(description))
        {
            description = routine.Name;
            Logger?.LogWarning("MCP tool '{Name}' has no description — provide `mcp <text>` or comment prose so agents call it well.", name);
        }
        // Optional shared suffix injected into every tool description (McpOptions.ToolDescriptionSuffix).
        if (!string.IsNullOrWhiteSpace(_options.ToolDescriptionSuffix))
        {
            description = $"{description} {_options.ToolDescriptionSuffix.Trim()}";
        }

        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var p in routine.Parameters)
        {
            if (IsExcludedFromInput(p, endpoint))
            {
                continue;
            }
            properties[p.ConvertedName] = SchemaMapper.GetSchemaForType(p.TypeDescriptor);
            // A parameter is required unless it has a default (PG DEFAULT, tracked on the type
            // descriptor) or an explicit annotation default.
            if (!p.TypeDescriptor.HasDefault && p.DefaultValue is null)
            {
                required.Add((JsonNode?)p.ConvertedName);
            }
        }

        var inputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
        };
        if (required.Count > 0)
        {
            inputSchema["required"] = required;
        }

        // Safety hints derived from the HTTP method. GET → read-only; DELETE → destructive.
        var annotations = new JsonObject { ["readOnlyHint"] = endpoint.Method == Method.GET };
        if (endpoint.Method == Method.DELETE)
        {
            annotations["destructiveHint"] = true;
        }

        var tool = new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema,
            ["annotations"] = annotations,
        };
        // outputSchema describes the structuredContent the tool returns (MCP 2025-11-25 §Output Schema).
        var outputSchema = BuildOutputSchema(endpoint);
        if (outputSchema is not null)
        {
            tool["outputSchema"] = outputSchema;
        }
        return tool;
    }

    /// <summary>
    /// Parameters that the agent must NOT supply are excluded from inputSchema: claim-sourced, IP,
    /// virtual, or server-resolved (via a resolved-parameter SQL expression).
    /// </summary>
    private static bool IsExcludedFromInput(NpgsqlRestParameter p, RoutineEndpoint endpoint)
    {
        if (p.IsFromUserClaims || p.IsIpAddress || p.IsVirtual)
        {
            return true;
        }
        var resolved = endpoint.ResolvedParameterExpressions;
        return resolved is not null
            && (resolved.ContainsKey(p.ActualName) || resolved.ContainsKey(p.ConvertedName));
    }

    private static string? DeriveDescription(RoutineEndpoint endpoint)
    {
        var lines = endpoint.UnhandledCommentLines;
        return lines is { Length: > 0 } ? string.Join('\n', lines) : null;
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
