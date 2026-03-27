using System.Collections.Frozen;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace NpgsqlRest.HttpClientType;

/// <summary>
/// Handles internal self-referencing HTTP calls by invoking NpgsqlRest endpoint handlers directly,
/// bypassing the network stack entirely. Used for relative-path HTTP client type definitions (e.g., "GET /api/test").
/// </summary>
internal static class InternalRequestHandler
{
    private static IServiceProvider? _serviceProvider;
    private static FrozenDictionary<string, Func<HttpContext, Task>>? _endpointHandlers;

    /// <summary>
    /// Initialize with the application's service provider. Called during UseNpgsqlRest.
    /// </summary>
    internal static void Initialize(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Register endpoint handlers for internal routing. Key is the endpoint path.
    /// </summary>
    internal static void SetEndpointHandlers(Dictionary<string, Func<HttpContext, Task>> handlers)
    {
        _endpointHandlers = handlers.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether internal request handling is available.
    /// </summary>
    internal static bool IsAvailable => _endpointHandlers is not null && _serviceProvider is not null;

    /// <summary>
    /// Execute an internal request by invoking the endpoint handler directly.
    /// </summary>
    internal static async Task<InternalResponse> ExecuteAsync(
        string method,
        string path,
        Dictionary<string, string>? headers,
        string? body,
        string? contentType,
        CancellationToken cancellationToken)
    {
        if (_endpointHandlers is null || _serviceProvider is null)
        {
            throw new InvalidOperationException("InternalRequestHandler not initialized.");
        }

        // Parse path without query string for handler lookup
        var pathOnly = path;
        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0)
        {
            pathOnly = path[..queryIndex];
        }

        // Look up the endpoint handler by path (exact match first, then template matching)
        RouteValueDictionary? routeValues = null;
        if (!_endpointHandlers.TryGetValue(pathOnly, out var handler))
        {
            (handler, routeValues) = MatchTemplatedPath(pathOnly);
            if (handler is null)
            {
                return new InternalResponse { StatusCode = 404, IsSuccess = false };
            }
        }

        await using var scope = _serviceProvider.CreateAsyncScope();

        var responseBody = new NonClosingMemoryStream();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        // Request setup
        context.Request.Method = method;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        if (queryIndex >= 0)
        {
            context.Request.Path = path[..queryIndex];
            context.Request.QueryString = new QueryString(path[queryIndex..]);
        }
        else
        {
            context.Request.Path = path;
        }
        if (headers is not null)
        {
            foreach (var (key, value) in headers)
            {
                context.Request.Headers[key] = value;
            }
        }
        if (body is not null)
        {
            var bodyBytes = System.Text.Encoding.UTF8.GetBytes(body);
            context.Request.Body = new MemoryStream(bodyBytes);
            context.Request.ContentLength = bodyBytes.Length;
            context.Request.ContentType = contentType ?? "application/json";
        }

        // Set route values for path parameter endpoints
        if (routeValues is not null)
        {
            context.Request.RouteValues = routeValues;
        }

        // Response setup — replace the body stream
        context.Response.Body = responseBody;

        // Invoke handler
        try
        {
            await handler(context);
        }
        catch (Exception ex)
        {
            return new InternalResponse { StatusCode = 500, Body = ex.Message, IsSuccess = false };
        }

        // Flush any remaining data
        await context.Response.Body.FlushAsync(cancellationToken);

        // Read response body
        responseBody.Position = 0;
        var responseText = await new StreamReader(responseBody).ReadToEndAsync(cancellationToken);

        // Build response headers JSON
        string? responseHeaders = null;
        if (context.Response.Headers.Count > 0)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append('{');
            bool first = true;
            foreach (var header in context.Response.Headers)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(PgConverters.SerializeString(header.Key));
                sb.Append(':');
                sb.Append(PgConverters.SerializeString(header.Value.ToString()));
            }
            sb.Append('}');
            responseHeaders = sb.ToString();
        }

        return new InternalResponse
        {
            StatusCode = context.Response.StatusCode,
            Body = responseText,
            ContentType = context.Response.ContentType,
            Headers = responseHeaders,
            IsSuccess = context.Response.StatusCode >= 200 && context.Response.StatusCode < 300
        };
    }

    /// <summary>
    /// Match a concrete path (e.g., "/api/users/42") against registered template paths (e.g., "/api/users/{id}").
    /// Compares segment-by-segment: literal segments must match exactly, {param} segments match anything.
    /// Returns the handler and extracted route values.
    /// </summary>
    private static (Func<HttpContext, Task>? Handler, RouteValueDictionary? RouteValues) MatchTemplatedPath(string path)
    {
        if (_endpointHandlers is null) return (null, null);

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var (template, handler) in _endpointHandlers)
        {
            if (!template.Contains('{')) continue;

            var templateSegments = template.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (templateSegments.Length != segments.Length) continue;

            bool match = true;
            RouteValueDictionary? routeValues = null;
            for (int i = 0; i < segments.Length; i++)
            {
                var ts = templateSegments[i];
                if (ts.StartsWith('{') && ts.EndsWith('}'))
                {
                    // Extract parameter name (strip { } and optional ?)
                    var paramName = ts[1..^1].TrimEnd('?');
                    routeValues ??= new();
                    routeValues[paramName] = segments[i];
                    continue;
                }
                if (!string.Equals(segments[i], ts, StringComparison.OrdinalIgnoreCase))
                {
                    match = false;
                    break;
                }
            }

            if (match) return (handler, routeValues);
        }

        return (null, null);
    }
}

/// <summary>
/// MemoryStream that ignores Close/Dispose — prevents PipeWriter.Complete from closing the stream
/// before we can read the response body.
/// </summary>
internal class NonClosingMemoryStream : MemoryStream
{
    protected override void Dispose(bool disposing) { /* ignore */ }
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public override void Close() { /* ignore */ }
}

internal class InternalResponse
{
    public int StatusCode { get; init; }
    public string? Body { get; init; }
    public string? ContentType { get; init; }
    public string? Headers { get; init; }
    public bool IsSuccess { get; init; }
}
