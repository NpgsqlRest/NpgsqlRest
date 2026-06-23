using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlRest.HttpClientType;

namespace NpgsqlRest.Proxy;

/// <summary>
/// Handles forwarding HTTP requests to a proxy target and returning the response.
/// </summary>
public static class ProxyRequestHandler
{
    private static readonly HttpClient SharedClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan // We handle timeout per-request
    };

    /// <summary>
    /// HttpClient for self-referencing proxy calls (relative paths).
    /// When set, relative URL proxy requests use this client (e.g., TestServer in-memory handler).
    /// </summary>
    private static HttpClient? _selfClient;

    /// <summary>
    /// Base URL for resolving relative proxy paths. Auto-detected from server addresses or set via ProxyOptions.SelfBaseUrl.
    /// </summary>
    internal static string? SelfBaseUrl { get; set; }

    /// <summary>
    /// Set a custom HttpClient for self-referencing proxy calls.
    /// </summary>
    internal static void SetSelfClient(HttpClient client)
    {
        _selfClient = client;
    }

    /// <summary>
    /// Forward the incoming request to the proxy target and return the response.
    /// </summary>
    public static async Task<ProxyResponse> InvokeAsync(
        HttpContext context,
        RoutineEndpoint endpoint,
        string? requestBody,
        NpgsqlParameterCollection? parameters = null,
        Dictionary<string, string>? userContextHeaders = null,
        CancellationToken cancellationToken = default)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var proxyOptions = Options.ProxyOptions;

        // Determine the target URL
        var host = endpoint.ProxyHost ?? proxyOptions.Host;
        if (string.IsNullOrEmpty(host))
        {
            return new ProxyResponse
            {
                StatusCode = 500,
                IsSuccess = false,
                ErrorMessage = "Proxy host is not configured. Set ProxyOptions.Host or specify host in proxy annotation."
            };
        }

        // Resolve relative paths for self-referencing proxy calls
        bool isSelfCall = host.StartsWith('/');
        if (isSelfCall && _selfClient is null && SelfBaseUrl is not null)
        {
            host = string.Concat(SelfBaseUrl, host);
        }

        // Build the target URL with user claim parameters
        var targetUrl = isSelfCall && _selfClient is not null
            ? BuildSelfTargetUrl(host)
            : BuildTargetUrl(host, context.Request);

        // Determine HTTP method
        var method = endpoint.ProxyMethod?.ToString().ToUpperInvariant() ?? context.Request.Method;

        // Forward server-filled HTTP Custom Type field params to the upstream the same way the
        // endpoint takes its own parameters. Placement mirrors the endpoint, NOT the HTTP verb:
        //  - a param designated as the body parameter (@body_parameter_name) carries the raw body;
        //  - otherwise RequestParamType decides: BodyJson -> merged JSON body (when the proxy method
        //    can physically carry a JSON body), QueryString -> query string.
        // Additive — the verbatim incoming request is still forwarded; this only adds filled values.
        string? effectiveBody = requestBody;
        var forwardParams = GetForwardableParams(endpoint, parameters);
        if (forwardParams is not null)
        {
            var bodyParam = FindForwardableBodyParam(endpoint, forwardParams);
            if (bodyParam is not null)
            {
                if (bodyParam.Value is not null && bodyParam.Value != DBNull.Value)
                {
                    effectiveBody = bodyParam.Value.ToString();
                }
                forwardParams.Remove(bodyParam);
            }

            if (forwardParams.Count > 0)
            {
                bool mergeIntoBody = bodyParam is null
                    && endpoint.RequestParamType == RequestParamType.BodyJson
                    && HasRequestBody(method)
                    && IsJsonContentType(context.Request.ContentType);
                if (mergeIntoBody)
                {
                    effectiveBody = MergeParamsIntoJsonBody(effectiveBody, forwardParams);
                }
                else
                {
                    targetUrl = AppendParamsToQuery(targetUrl, forwardParams);
                }
            }
        }

        Logger?.LogDebug("Proxy starting {Method} request to '{Url}'", method, targetUrl);

        try
        {
            // Use internal request handler for self-calls (bypasses HTTP stack entirely)
            if (isSelfCall && InternalRequestHandler.IsAvailable)
            {
                // Build headers from the proxy request
                Dictionary<string, string>? proxyHeaders = null;
                if (proxyOptions.ForwardHeaders)
                {
                    proxyHeaders = new();
                    foreach (var header in context.Request.Headers)
                    {
                        if (!string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                        {
                            proxyHeaders[header.Key] = header.Value.ToString();
                        }
                    }
                }
                if (userContextHeaders is not null)
                {
                    proxyHeaders ??= new();
                    foreach (var header in userContextHeaders)
                    {
                        proxyHeaders[header.Key] = header.Value;
                    }
                }

                var internalResponse = await InternalRequestHandler.ExecuteAsync(
                    method,
                    targetUrl,
                    proxyHeaders,
                    effectiveBody,
                    context.Request.ContentType,
                    cancellationToken);

                var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
                Logger?.LogDebug("Internal proxy request to '{Url}' completed with status {StatusCode} in {Elapsed}ms",
                    targetUrl, internalResponse.StatusCode, elapsed.TotalMilliseconds.ToString("F1"));

                return new ProxyResponse
                {
                    StatusCode = internalResponse.StatusCode,
                    Body = internalResponse.Body,
                    RawBody = internalResponse.Body is not null ? Encoding.UTF8.GetBytes(internalResponse.Body) : null,
                    ContentType = internalResponse.ContentType,
                    Headers = internalResponse.Headers,
                    IsSuccess = internalResponse.IsSuccess
                };
            }

            using var request = await CreateRequestAsync(context, method, targetUrl, effectiveBody, proxyOptions, userContextHeaders);
            using var cts = CreateTimeoutCancellationTokenSource(proxyOptions.DefaultTimeout, cancellationToken);

            var client = isSelfCall && _selfClient is not null ? _selfClient : SharedClient;
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts?.Token ?? cancellationToken);

            return await ProcessResponseAsync(response, proxyOptions, startTimestamp, targetUrl);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var timeoutSeconds = proxyOptions.DefaultTimeout.TotalSeconds;
            Logger?.LogWarning("Proxy request to '{Url}' timed out after {Timeout}s", targetUrl, timeoutSeconds);
            return new ProxyResponse
            {
                StatusCode = 504, // Gateway Timeout
                IsSuccess = false,
                ErrorMessage = $"Proxy request timed out after {timeoutSeconds} seconds"
            };
        }
        catch (HttpRequestException ex)
        {
            Logger?.LogError(ex, "Proxy request to '{Url}' failed with status {StatusCode}", targetUrl, ex.StatusCode);
            return new ProxyResponse
            {
                StatusCode = (int?)ex.StatusCode ?? 502, // Bad Gateway
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Proxy request to '{Url}' failed with unexpected error", targetUrl);
            return new ProxyResponse
            {
                StatusCode = 502, // Bad Gateway
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Build target URL for self-referencing proxy calls. Uses the host as the full path (no appending of request path).
    /// </summary>
    private static string BuildSelfTargetUrl(string host)
    {
        // For self-calls, host IS the full relative path (e.g., /api/hello-world)
        // Don't append the incoming request path
        return host;
    }

    private static string BuildTargetUrl(string host, HttpRequest request)
    {
        // Ensure host doesn't end with /
        host = host.TrimEnd('/');

        // Get the path and query string. Automatic (server-filled) parameters — user claims, IP,
        // HTTP Custom Type fields, resolved-parameter expressions — are appended consistently by the
        // unified forwarding step in InvokeAsync (query string or JSON body, per the endpoint shape).
        var path = request.Path.Value ?? "";
        var queryString = request.QueryString.Value ?? "";

        return $"{host}{path}{queryString}";
    }

    private static bool IsJsonContentType(string? contentType) =>
        contentType is not null && contentType.Contains("json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when a parameter is filled by the server (not supplied by the client) and so should be
    /// forwarded to the proxy upstream the same way the endpoint receives it. All automatic parameter
    /// sources are treated consistently: user claims, IP address, HTTP Custom Type fields (the expanded
    /// per-field params on DB functions), and resolved-parameter expressions. Single-composite HTTP
    /// params (SQL-file shape, CustomTypeName null) are not forwarded.
    /// </summary>
    private static bool IsAutomaticParam(RoutineEndpoint endpoint, NpgsqlRestParameter p)
    {
        if (p.IsFromUserClaims || p.IsIpAddress)
        {
            return true;
        }
        if (p.TypeDescriptor.CustomType is not null
            && p.TypeDescriptor.CustomTypeName is not null
            && HttpClientTypes.Definitions.ContainsKey(p.TypeDescriptor.CustomType))
        {
            return true;
        }
        if (endpoint.ResolvedParameterExpressions is not null
            && (endpoint.ResolvedParameterExpressions.ContainsKey(p.ActualName)
                || endpoint.ResolvedParameterExpressions.ContainsKey(p.ConvertedName)))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// The forwardable parameter (if any) designated as the request body parameter
    /// (<c>@body_parameter_name</c>) — it carries the raw request body rather than a query/JSON field.
    /// </summary>
    private static NpgsqlRestParameter? FindForwardableBodyParam(RoutineEndpoint endpoint, List<NpgsqlRestParameter> parameters)
    {
        if (!endpoint.HasBodyParameter)
        {
            return null;
        }
        foreach (var p in parameters)
        {
            if (string.Equals(endpoint.BodyParameterName, p.ConvertedName, StringComparison.Ordinal)
                || string.Equals(endpoint.BodyParameterName, p.ActualName, StringComparison.Ordinal))
            {
                return p;
            }
        }
        return null;
    }

    /// <summary>
    /// Collects all server-filled (automatic) parameters to forward to the proxy upstream.
    /// </summary>
    private static List<NpgsqlRestParameter>? GetForwardableParams(RoutineEndpoint endpoint, NpgsqlParameterCollection? parameters)
    {
        if (parameters is null)
        {
            return null;
        }
        List<NpgsqlRestParameter>? result = null;
        for (var i = 0; i < parameters.Count; i++)
        {
            if (parameters[i] is NpgsqlRestParameter rp && IsAutomaticParam(endpoint, rp))
            {
                (result ??= []).Add(rp);
            }
        }
        return result;
    }

    private static string AppendParamsToQuery(string url, List<NpgsqlRestParameter> parameters)
    {
        var sb = new StringBuilder();
        foreach (var p in parameters)
        {
            if (p.Value is null || p.Value == DBNull.Value)
            {
                continue;
            }
            if (sb.Length > 0)
            {
                sb.Append('&');
            }
            sb.Append(Uri.EscapeDataString(p.ConvertedName));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(p.Value.ToString() ?? ""));
        }
        if (sb.Length == 0)
        {
            return url;
        }
        return url.Contains('?') ? $"{url}&{sb}" : $"{url}?{sb}";
    }

    private static string MergeParamsIntoJsonBody(string? body, List<NpgsqlRestParameter> parameters)
    {
        JsonObject obj;
        if (string.IsNullOrWhiteSpace(body))
        {
            obj = [];
        }
        else
        {
            try
            {
                obj = JsonNode.Parse(body) as JsonObject ?? [];
            }
            catch
            {
                obj = [];
            }
        }
        foreach (var p in parameters)
        {
            obj[p.ConvertedName] = ToJsonNode(p);
        }
        return obj.ToJsonString();
    }

    // Typed JSON value for a server-filled HTTP-type field, following the field's declared type:
    // numeric/boolean fields become JSON numbers/bools, json fields are embedded as JSON, the rest
    // become JSON strings. The value was already typed by the HTTP-type fill (asText vs native).
    private static JsonNode? ToJsonNode(NpgsqlRestParameter p)
    {
        var v = p.Value;
        if (v is null || v == DBNull.Value)
        {
            return null;
        }
        if (p.TypeDescriptor.IsJson)
        {
            var s = v.ToString();
            if (string.IsNullOrEmpty(s))
            {
                return null;
            }
            try { return JsonNode.Parse(s); }
            catch { return JsonValue.Create(s); }
        }
        return v switch
        {
            bool b => JsonValue.Create(b),
            int i => JsonValue.Create(i),
            long l => JsonValue.Create(l),
            short sh => JsonValue.Create((int)sh),
            decimal dec => JsonValue.Create(dec),
            double d => JsonValue.Create(d),
            _ => JsonValue.Create(v.ToString())
        };
    }

    private static async Task<HttpRequestMessage> CreateRequestAsync(
        HttpContext context,
        string method,
        string targetUrl,
        string? requestBody,
        ProxyOptions proxyOptions,
        Dictionary<string, string>? userContextHeaders)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), targetUrl);

        // Forward headers if enabled
        if (proxyOptions.ForwardHeaders)
        {
            foreach (var header in context.Request.Headers)
            {
                if (proxyOptions.ExcludeHeaders.Contains(header.Key))
                {
                    continue;
                }

                // Skip content headers - they'll be set with content
                if (IsContentHeader(header.Key))
                {
                    continue;
                }

                request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        // Add user context headers (from UserContext feature)
        if (userContextHeaders is not null)
        {
            foreach (var header in userContextHeaders)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        // Forward body for methods that support it
        if (HasRequestBody(method))
        {
            var contentType = context.Request.ContentType;
            var isMultipart = contentType?.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase) == true;

            // For multipart uploads when ForwardUploadContent is enabled, forward raw stream
            if (isMultipart && proxyOptions.ForwardUploadContent)
            {
                context.Request.EnableBuffering();
                context.Request.Body.Position = 0;

                // Use StreamContent for efficient streaming without loading into memory
                request.Content = new StreamContent(context.Request.Body);
                if (!string.IsNullOrEmpty(contentType))
                {
                    request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
                }
            }
            else if (!string.IsNullOrEmpty(requestBody))
            {
                request.Content = new StringContent(requestBody, Encoding.UTF8);

                if (!string.IsNullOrEmpty(contentType))
                {
                    request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
                }
            }
            else if (context.Request.ContentLength > 0)
            {
                // Read body from request if not already provided
                context.Request.EnableBuffering();
                context.Request.Body.Position = 0;

                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                var body = await reader.ReadToEndAsync();

                if (!string.IsNullOrEmpty(body))
                {
                    request.Content = new StringContent(body, Encoding.UTF8);
                    if (!string.IsNullOrEmpty(contentType))
                    {
                        request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
                    }
                }
            }
        }

        return request;
    }

    private static bool HasRequestBody(string method)
    {
        return method is "POST" or "PUT" or "PATCH";
    }

    private static bool IsContentHeader(string headerName)
    {
        return headerName.StartsWith("Content-", StringComparison.OrdinalIgnoreCase);
    }

    private static CancellationTokenSource? CreateTimeoutCancellationTokenSource(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        return cts;
    }

    private static async Task<ProxyResponse> ProcessResponseAsync(
        HttpResponseMessage response,
        ProxyOptions proxyOptions,
        long startTimestamp,
        string targetUrl)
    {
        var result = new ProxyResponse
        {
            StatusCode = (int)response.StatusCode,
            IsSuccess = response.IsSuccessStatusCode,
            ContentType = response.Content.Headers.ContentType?.ToString()
        };

        // Read body
        result.RawBody = await response.Content.ReadAsByteArrayAsync();
        result.Body = Encoding.UTF8.GetString(result.RawBody);

        // Build headers
        result.RawHeaders = new Dictionary<string, string[]>();
        var headersJson = new StringBuilder();
        headersJson.Append('{');
        bool first = true;

        foreach (var header in response.Headers)
        {
            result.RawHeaders[header.Key] = header.Value.ToArray();

            if (!first) headersJson.Append(',');
            first = false;
            headersJson.Append(PgConverters.SerializeString(header.Key));
            headersJson.Append(':');
            headersJson.Append(PgConverters.SerializeString(string.Join(", ", header.Value)));
        }

        foreach (var header in response.Content.Headers)
        {
            result.RawHeaders[header.Key] = header.Value.ToArray();

            if (!first) headersJson.Append(',');
            first = false;
            headersJson.Append(PgConverters.SerializeString(header.Key));
            headersJson.Append(':');
            headersJson.Append(PgConverters.SerializeString(string.Join(", ", header.Value)));
        }

        headersJson.Append('}');
        result.Headers = headersJson.ToString();

        var duration = Stopwatch.GetElapsedTime(startTimestamp);
        Logger?.LogDebug("Proxy request to '{Url}' completed with status {StatusCode}, content-type: {ContentType}, body length: {BodyLength}, duration: {Duration}ms",
            targetUrl, result.StatusCode, result.ContentType, result.Body?.Length ?? 0, duration.TotalMilliseconds);

        return result;
    }

    /// <summary>
    /// Forward the function result body to an upstream proxy target (proxy_out mode).
    /// </summary>
    public static async Task<ProxyResponse> InvokeOutAsync(
        HttpContext context,
        RoutineEndpoint endpoint,
        byte[] functionBodyBytes,
        CancellationToken cancellationToken = default)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var proxyOptions = Options.ProxyOptions;

        var host = endpoint.ProxyOutHost ?? proxyOptions.Host;
        if (string.IsNullOrEmpty(host))
        {
            return new ProxyResponse
            {
                StatusCode = 500,
                IsSuccess = false,
                ErrorMessage = "Proxy host is not configured. Set ProxyOptions.Host or specify host in proxy_out annotation."
            };
        }

        bool isSelfCall = host.StartsWith('/');
        if (isSelfCall && _selfClient is null && SelfBaseUrl is not null)
        {
            host = string.Concat(SelfBaseUrl, host);
        }

        string targetUrl;
        if (isSelfCall && _selfClient is not null)
        {
            targetUrl = host; // relative path, _selfClient handles BaseAddress
        }
        else
        {
            host = host.TrimEnd('/');
            var path = context.Request.Path.Value ?? "";
            var queryString = context.Request.QueryString.Value ?? "";
            targetUrl = $"{host}{path}{queryString}";
        }

        var method = endpoint.ProxyOutMethod?.ToString().ToUpperInvariant() ?? context.Request.Method;

        Logger?.LogDebug("ProxyOut starting {Method} request to '{Url}'", method, targetUrl);

        try
        {
            // Use internal request handler for self-calls (bypasses HTTP stack entirely)
            if (isSelfCall && InternalRequestHandler.IsAvailable)
            {
                var bodyStr = functionBodyBytes.Length > 0 ? Encoding.UTF8.GetString(functionBodyBytes) : null;
                var internalResponse = await InternalRequestHandler.ExecuteAsync(
                    method, targetUrl, null, bodyStr, "application/json", cancellationToken);

                var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
                Logger?.LogDebug("Internal proxyOut request to '{Url}' completed with status {StatusCode} in {Elapsed}ms",
                    targetUrl, internalResponse.StatusCode, elapsed.TotalMilliseconds.ToString("F1"));

                return new ProxyResponse
                {
                    StatusCode = internalResponse.StatusCode,
                    Body = internalResponse.Body,
                    ContentType = internalResponse.ContentType,
                    Headers = internalResponse.Headers,
                    IsSuccess = internalResponse.IsSuccess
                };
            }

            using var request = new HttpRequestMessage(new HttpMethod(method), targetUrl);

            if (HasRequestBody(method) && functionBodyBytes.Length > 0)
            {
                request.Content = new ByteArrayContent(functionBodyBytes);
                request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            }

            using var cts = CreateTimeoutCancellationTokenSource(proxyOptions.DefaultTimeout, cancellationToken);

            var client = isSelfCall && _selfClient is not null ? _selfClient : SharedClient;
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts?.Token ?? cancellationToken);

            return await ProcessResponseAsync(response, proxyOptions, startTimestamp, targetUrl);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var timeoutSeconds = proxyOptions.DefaultTimeout.TotalSeconds;
            Logger?.LogWarning("ProxyOut request to '{Url}' timed out after {Timeout}s", targetUrl, timeoutSeconds);
            return new ProxyResponse
            {
                StatusCode = 504,
                IsSuccess = false,
                ErrorMessage = $"ProxyOut request timed out after {timeoutSeconds} seconds"
            };
        }
        catch (HttpRequestException ex)
        {
            Logger?.LogError(ex, "ProxyOut request to '{Url}' failed with status {StatusCode}", targetUrl, ex.StatusCode);
            return new ProxyResponse
            {
                StatusCode = (int?)ex.StatusCode ?? 502,
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "ProxyOut request to '{Url}' failed with unexpected error", targetUrl);
            return new ProxyResponse
            {
                StatusCode = 502,
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Write the proxy response directly to the HTTP context response.
    /// Used when the routine has no proxy response parameters.
    /// </summary>
    public static async Task WriteResponseAsync(
        HttpContext context,
        ProxyResponse proxyResponse,
        ProxyOptions proxyOptions,
        CancellationToken cancellationToken = default)
    {
        context.Response.StatusCode = proxyResponse.StatusCode;

        // Set content type
        if (!string.IsNullOrEmpty(proxyResponse.ContentType))
        {
            context.Response.ContentType = proxyResponse.ContentType;
        }

        // Forward response headers if enabled
        if (proxyOptions.ForwardResponseHeaders && proxyResponse.RawHeaders is not null)
        {
            foreach (var header in proxyResponse.RawHeaders)
            {
                if (proxyOptions.ExcludeResponseHeaders.Contains(header.Key))
                {
                    continue;
                }

                // Skip content-type as it's set separately
                if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                context.Response.Headers.TryAdd(header.Key, header.Value);
            }
        }

        // Write body
        if (proxyResponse.RawBody is not null && proxyResponse.RawBody.Length > 0)
        {
            await context.Response.Body.WriteAsync(proxyResponse.RawBody, cancellationToken);
        }
    }
}
