using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace NpgsqlRest.Mcp;

/// <summary>
/// MCP server transport: serves the JSON-RPC endpoint (Streamable HTTP, single endpoint) at
/// <see cref="McpOptions.UrlPath"/>. Stateless, single application/json response — no SSE.
/// Implements the MCP 2025-11-25 lifecycle subset: initialize, notifications/initialized, ping,
/// tools/list. (tools/call arrives in a later increment.)
/// </summary>
public partial class Mcp
{
    private const string ProtocolVersion = "2025-11-25";

    private IApplicationBuilder _builder = default!;

    public void Setup(IApplicationBuilder builder, NpgsqlRestOptions options) => _builder = builder;

    public void Cleanup()
    {
        if (!_options.Enabled)
        {
            return;
        }

        var path = _options.UrlPath;
        _builder.Use(async (context, next) =>
        {
            if (string.Equals(context.Request.Path, path, StringComparison.Ordinal))
            {
                await HandleAsync(context);
                return;
            }
            await next(context);
        });
    }

    private async Task HandleAsync(HttpContext context)
    {
        // POST carries JSON-RPC. (GET would open an SSE stream — not supported in the stateless model.)
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        string body;
        using (var reader = new StreamReader(context.Request.Body))
        {
            body = await reader.ReadToEndAsync(context.RequestAborted);
        }

        JsonNode? request;
        try
        {
            request = JsonNode.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        }
        catch (JsonException)
        {
            await WriteResponseAsync(context, ErrorEnvelope(null, -32700, "Parse error"));
            return;
        }

        var method = request?["method"]?.GetValue<string>();
        var id = request?["id"]?.DeepClone();

        // Notifications carry no id and expect no response body.
        if (method is not null && method.StartsWith("notifications/", StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            return;
        }

        JsonObject? result = method switch
        {
            "initialize" => BuildInitializeResult(),
            "ping" => new JsonObject(),
            "tools/list" => BuildToolsListResult(),
            _ => null,
        };

        if (result is null)
        {
            await WriteResponseAsync(context, ErrorEnvelope(id, -32601, $"Method not found: {method}"));
            return;
        }

        await WriteResponseAsync(context, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result,
        });
    }

    private JsonObject BuildInitializeResult()
    {
        var result = new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = string.IsNullOrWhiteSpace(_options.ServerName) ? "NpgsqlRest" : _options.ServerName,
                ["version"] = string.IsNullOrWhiteSpace(_options.ServerVersion) ? "1.0.0" : _options.ServerVersion,
            },
        };
        if (!string.IsNullOrWhiteSpace(_options.Instructions))
        {
            result["instructions"] = _options.Instructions;
        }
        return result;
    }

    private JsonObject BuildToolsListResult()
    {
        var tools = new JsonArray();
        foreach (var tool in _tools.Values)
        {
            tools.Add(tool.DeepClone()); // catalog entries are owned by _tools; clone before reparenting
        }
        return new JsonObject { ["tools"] = tools };
    }

    private static JsonObject ErrorEnvelope(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };

    private static async Task WriteResponseAsync(HttpContext context, JsonObject payload)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";
        context.Response.Headers["MCP-Protocol-Version"] = ProtocolVersion;
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, McpJsonContext.Default.JsonObject), context.RequestAborted);
    }
}
