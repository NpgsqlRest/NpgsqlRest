using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Npgsql;

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
    private string? _connectionString;

    public void Setup(IApplicationBuilder builder, NpgsqlRestOptions options)
    {
        _builder = builder;
        _connectionString = options.ConnectionString;
    }

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

        if (method == "tools/call")
        {
            await HandleToolCallAsync(context, request, id);
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

    private async Task HandleToolCallAsync(HttpContext context, JsonNode? request, JsonNode? id)
    {
        var prms = request?["params"];
        var name = prms?["name"]?.GetValue<string>();
        if (name is null || !_toolEndpoints.TryGetValue(name, out var endpoint))
        {
            // Structural failure → JSON-RPC error (not a tool result).
            await WriteResponseAsync(context, ErrorEnvelope(id, -32602, $"Unknown tool: {name}"));
            return;
        }

        var (httpMethod, path, body, contentType) = BuildInvocation(endpoint, prms?["arguments"] as JsonObject);

        // Forward the /mcp request's principal so the routine's authorize/claims binding applies.
        var invoke = await RoutineInvoker.InvokeAsync(
            httpMethod, path, headers: null, body: body, contentType: contentType,
            user: context.User, cancellationToken: context.RequestAborted);

        // Business/execution outcome → a normal result with isError; the routine's response is the
        // text content verbatim. (Only structural problems above use a JSON-RPC error.)
        var content = new JsonArray();
        content.Add(new JsonObject { ["type"] = "text", ["text"] = invoke.Body ?? string.Empty });

        var toolResult = new JsonObject
        {
            ["content"] = content,
            ["isError"] = !invoke.IsSuccess,
        };

        await WriteResponseAsync(context, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = toolResult,
        });
    }

    /// <summary>
    /// Maps a flat MCP arguments object onto the endpoint's HTTP shape: path-parameter substitution,
    /// then query string (QueryString endpoints) or a JSON body (BodyJson endpoints).
    /// </summary>
    private static (string Method, string Path, string? Body, string? ContentType) BuildInvocation(
        RoutineEndpoint endpoint, JsonObject? arguments)
    {
        var args = arguments is null ? new JsonObject() : (JsonObject)arguments.DeepClone();
        var path = endpoint.Path;

        if (endpoint.PathParameters is { Length: > 0 } pathParams)
        {
            foreach (var pp in pathParams)
            {
                if (args.TryGetPropertyValue(pp, out var v) && v is not null)
                {
                    path = path.Replace("{" + pp + "}", Uri.EscapeDataString(v.ToString()));
                    args.Remove(pp);
                }
            }
        }

        var method = endpoint.Method.ToString();

        if (endpoint.RequestParamType == RequestParamType.BodyJson)
        {
            return (method, path, args.ToJsonString(), "application/json");
        }

        // QueryString endpoints (GET/DELETE by default).
        if (args.Count > 0)
        {
            var query = string.Join("&", args.Select(kv =>
                string.Concat(Uri.EscapeDataString(kv.Key), "=", Uri.EscapeDataString(kv.Value?.ToString() ?? string.Empty))));
            path = string.Concat(path, path.Contains('?') ? "&" : "?", query);
        }
        return (method, path, null, null);
    }

    private JsonObject BuildInitializeResult()
    {
        var result = new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = GetServerName(),
                ["version"] = string.IsNullOrWhiteSpace(_options.ServerVersion) ? "1.0.0" : _options.ServerVersion,
            },
        };
        if (!string.IsNullOrWhiteSpace(_options.Instructions))
        {
            result["instructions"] = _options.Instructions;
        }
        return result;
    }

    /// <summary>
    /// serverInfo.name for the initialize handshake. Explicit <see cref="McpOptions.ServerName"/> wins;
    /// otherwise the database name from the connection string is used (mirrors the OpenAPI document title),
    /// falling back to "NpgsqlRest" when it cannot be resolved.
    /// </summary>
    private string GetServerName()
    {
        if (!string.IsNullOrWhiteSpace(_options.ServerName))
        {
            return _options.ServerName;
        }
        if (!string.IsNullOrWhiteSpace(_connectionString))
        {
            var database = new NpgsqlConnectionStringBuilder(_connectionString).Database;
            if (!string.IsNullOrWhiteSpace(database))
            {
                return database;
            }
        }
        return "NpgsqlRest";
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
