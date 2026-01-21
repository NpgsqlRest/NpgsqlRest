using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http;
using Npgsql;
using static NpgsqlRest.NpgsqlRestOptions;

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

        // Build the target URL with user claim parameters
        var targetUrl = BuildTargetUrl(host, context.Request, parameters);

        // Determine HTTP method
        var method = endpoint.ProxyMethod?.ToString().ToUpperInvariant() ?? context.Request.Method;

        Logger?.LogDebug("Proxy starting {Method} request to '{Url}'", method, targetUrl);

        try
        {
            using var request = await CreateRequestAsync(context, method, targetUrl, requestBody, proxyOptions, userContextHeaders);
            using var cts = CreateTimeoutCancellationTokenSource(proxyOptions.DefaultTimeout, cancellationToken);

            using var response = await SharedClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts?.Token ?? cancellationToken);

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

    private static string BuildTargetUrl(string host, HttpRequest request, NpgsqlParameterCollection? parameters)
    {
        // Ensure host doesn't end with /
        host = host.TrimEnd('/');

        // Get the path and query string
        var path = request.Path.Value ?? "";
        var queryString = request.QueryString.Value ?? "";

        // Append user claim and IP address parameters to query string
        if (parameters is not null)
        {
            var additionalParams = new StringBuilder();
            foreach (NpgsqlParameter param in parameters)
            {
                if (param is NpgsqlRestParameter restParam &&
                    (restParam.IsFromUserClaims || restParam.IsIpAddress) &&
                    restParam.Value is not null && restParam.Value != DBNull.Value)
                {
                    if (additionalParams.Length > 0)
                    {
                        additionalParams.Append('&');
                    }
                    additionalParams.Append(Uri.EscapeDataString(restParam.ConvertedName));
                    additionalParams.Append('=');
                    additionalParams.Append(Uri.EscapeDataString(restParam.Value.ToString() ?? ""));
                }
            }

            if (additionalParams.Length > 0)
            {
                if (string.IsNullOrEmpty(queryString))
                {
                    queryString = "?" + additionalParams.ToString();
                }
                else
                {
                    queryString = queryString + "&" + additionalParams.ToString();
                }
            }
        }

        return $"{host}{path}{queryString}";
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
