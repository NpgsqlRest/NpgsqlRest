using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using static NpgsqlRest.NpgsqlRestOptions;

namespace NpgsqlRest.Mcp;

/// <summary>
/// Function-calling schema documents (OpenAI / Anthropic) and llms.txt, projected from the MCP tool
/// set. Every output here is a projection of the tool objects <see cref="Mcp.BuildTool"/> already
/// produced — no schema logic (parameter filtering, description resolution, type mapping) is rebuilt.
/// </summary>
public partial class Mcp
{
    private static readonly JsonSerializerOptions ToolSchemaJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [GeneratedRegex("[^a-zA-Z0-9_-]")]
    private static partial Regex InvalidToolNameChars();

    /// <summary>
    /// Sanitizes a tool name to the OpenAI function-name form (^[a-zA-Z0-9_-]{1,64}$): every invalid
    /// character becomes '_', then the result is truncated to 64 characters. Anthropic's pattern is
    /// compatible with this form, so the same function is used for both documents.
    /// </summary>
    public static string SanitizeToolName(string name)
    {
        var sanitized = InvalidToolNameChars().Replace(name, "_");
        return sanitized.Length > 64 ? sanitized[..64] : sanitized;
    }

    /// <summary>
    /// Builds the OpenAI Chat Completions and Anthropic Messages API `tools` arrays from an MCP tool
    /// catalog. Iterates tools ordered by name (ordinal) for deterministic output; `parameters` /
    /// `input_schema` are deep clones of each tool's existing inputSchema, passed through verbatim.
    /// Throws when two tool names collide after sanitization (fail fast at startup).
    /// </summary>
    public static (JsonArray OpenAi, JsonArray Anthropic) BuildFunctionCallingDocuments(
        IReadOnlyDictionary<string, JsonObject> tools)
    {
        var openAi = new JsonArray();
        var anthropic = new JsonArray();
        var sanitizedNames = new Dictionary<string, string>(StringComparer.Ordinal); // sanitized -> original

        foreach (var name in tools.Keys.OrderBy(n => n, StringComparer.Ordinal))
        {
            var tool = tools[name];
            var sanitized = SanitizeToolName(name);
            if (sanitizedNames.TryGetValue(sanitized, out var previous))
            {
                throw new InvalidOperationException(
                    $"MCP tool names '{previous}' and '{name}' both sanitize to '{sanitized}' for the function-calling schema documents. " +
                    "Rename one of them (mcp_name annotation) so the sanitized names are unique.");
            }
            sanitizedNames[sanitized] = name;
            if (!string.Equals(sanitized, name, StringComparison.Ordinal))
            {
                Logger?.LogWarning(
                    "MCP tool name '{Name}' contains characters outside [a-zA-Z0-9_-] and was sanitized to '{Sanitized}' in the function-calling schema documents.",
                    name, sanitized);
            }

            var description = tool["description"]?.GetValue<string>() ?? "";
            var inputSchema = tool["inputSchema"];

            openAi.Add((JsonNode?)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = sanitized,
                    ["description"] = description,
                    ["parameters"] = inputSchema?.DeepClone(),
                },
            });
            anthropic.Add((JsonNode?)new JsonObject
            {
                ["name"] = sanitized,
                ["description"] = description,
                ["input_schema"] = inputSchema?.DeepClone(),
            });
        }
        return (openAi, anthropic);
    }

    /// <summary>
    /// Generates and publishes the function-calling schema documents and llms.txt. Runs at Cleanup
    /// time (tool set complete) and is independent of <see cref="McpOptions.Enabled"/> — the schemas
    /// do not require the /mcp endpoint to be served.
    /// </summary>
    private void GenerateToolSchemas()
    {
        var schemas = _options.ToolSchemas;
        if (!schemas.Enabled)
        {
            return;
        }
        if (_tools.Count == 0)
        {
            Logger?.LogWarning("MCP ToolSchemas is enabled but no routines carry the `mcp` annotation - the generated documents are empty.");
        }

        var (openAi, anthropic) = BuildFunctionCallingDocuments(_tools);
        var llmsTxt = BuildLlmsTxt();

        List<(string Path, string ContentType, string Body)> served = [];

        Publish(schemas.OpenAiFileName, schemas.OpenAiUrlPath, "application/json",
            openAi.ToJsonString(ToolSchemaJson));
        Publish(schemas.AnthropicFileName, schemas.AnthropicUrlPath, "application/json",
            anthropic.ToJsonString(ToolSchemaJson));
        Publish(schemas.LlmsTxtFileName, schemas.LlmsTxtUrlPath, "text/markdown; charset=utf-8",
            llmsTxt);

        if (served.Count > 0)
        {
            _builder.Use(async (context, next) =>
            {
                if (HttpMethods.IsGet(context.Request.Method))
                {
                    foreach (var (path, contentType, body) in served)
                    {
                        if (string.Equals(context.Request.Path, path, StringComparison.Ordinal))
                        {
                            context.Response.StatusCode = 200;
                            context.Response.ContentType = contentType;
                            await context.Response.WriteAsync(body);
                            return;
                        }
                    }
                }
                await next(context);
            });
        }
        return;

        void Publish(string? fileName, string? urlPath, string contentType, string content)
        {
            if (fileName is not null)
            {
                var fullFileName = Path.Combine(Environment.CurrentDirectory, fileName);
                if (!schemas.FileOverwrite && File.Exists(fullFileName))
                {
                    Logger?.LogDebug("MCP ToolSchemas file already exists and FileOverwrite is false: {fileName}", fullFileName);
                }
                else
                {
                    var dir = Path.GetDirectoryName(fullFileName);
                    if (dir is not null && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.WriteAllText(fullFileName, content);
                    Logger?.LogDebug("Created MCP ToolSchemas file: {fileName}", fullFileName);
                }
            }
            if (urlPath is not null)
            {
                served.Add((urlPath, contentType, content));
                Logger?.LogDebug("Exposed MCP ToolSchemas document on URL: {path}", urlPath);
            }
        }
    }

    /// <summary>
    /// Renders llms.txt (H1 + blockquote summary + Endpoints + Machine-readable sections) from the
    /// tool set and each tool's REST endpoint. Original (unsanitized) tool names in headings;
    /// parameter names and types are read from the existing inputSchema JSON. LF line endings,
    /// no trailing whitespace.
    /// </summary>
    private string BuildLlmsTxt()
    {
        var schemas = _options.ToolSchemas;
        List<string> lines = [];

        lines.Add($"# {GetServerName()}");
        lines.Add("");
        if (!string.IsNullOrWhiteSpace(_options.Instructions))
        {
            foreach (var instructionLine in _options.Instructions.Split('\n'))
            {
                var trimmed = instructionLine.TrimEnd('\r');
                lines.Add(trimmed.Length == 0 ? ">" : $"> {trimmed}");
            }
        }
        else
        {
            lines.Add($"> REST API generated from PostgreSQL by NpgsqlRest. {_tools.Count} callable endpoints listed below.");
        }

        lines.Add("");
        lines.Add("## Endpoints");
        foreach (var name in _tools.Keys.OrderBy(n => n, StringComparer.Ordinal))
        {
            var tool = _tools[name];
            var endpoint = _toolEndpoints[name];
            lines.Add("");
            lines.Add($"### {name}");
            lines.Add($"- Method: {endpoint.Method} {endpoint.Path}");
            lines.Add($"- Description: {tool["description"]?.GetValue<string>() ?? ""}");

            var properties = tool["inputSchema"]?["properties"] as JsonObject;
            var required = tool["inputSchema"]?["required"] as JsonArray;
            if (properties is null || properties.Count == 0)
            {
                lines.Add("- Parameters: none");
            }
            else
            {
                lines.Add("- Parameters:");
                foreach (var property in properties)
                {
                    var isRequired = required?.Any(r => string.Equals(r?.GetValue<string>(), property.Key, StringComparison.Ordinal)) is true;
                    var typeNode = property.Value?["type"];
                    var type = typeNode is JsonArray typeArray
                        ? string.Join("|", typeArray.Select(t => t?.GetValue<string>()))
                        : typeNode?.GetValue<string>() ?? "object";
                    lines.Add($"  - `{property.Key}` ({type}{(isRequired ? ", required" : ", default available")})");
                }
            }
        }

        List<string> machineReadable = [];
        if (schemas.OpenApiUrlPath is not null)
        {
            machineReadable.Add($"- OpenAPI: {schemas.OpenApiUrlPath}");
        }
        if (_options.Enabled)
        {
            machineReadable.Add($"- MCP endpoint: {_options.UrlPath}");
        }
        if (schemas.OpenAiUrlPath is not null)
        {
            machineReadable.Add($"- OpenAI tools: {schemas.OpenAiUrlPath}");
        }
        if (schemas.AnthropicUrlPath is not null)
        {
            machineReadable.Add($"- Anthropic tools: {schemas.AnthropicUrlPath}");
        }
        if (machineReadable.Count > 0)
        {
            lines.Add("");
            lines.Add("## Machine-readable");
            lines.AddRange(machineReadable);
        }

        StringBuilder sb = new();
        foreach (var line in lines)
        {
            sb.Append(line.TrimEnd());
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
