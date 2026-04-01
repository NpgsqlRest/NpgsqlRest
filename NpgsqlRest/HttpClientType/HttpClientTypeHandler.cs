using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace NpgsqlRest.HttpClientType;

public class HttpClientTypeHandler(HttpTypeDefinition definition, Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>>? replacements = null)
{
    private static readonly HttpClient SharedClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan // We handle timeout per-request
    };

    /// <summary>
    /// HttpClient for self-referencing calls (relative paths). Uses SelfBaseUrl or a custom handler.
    /// </summary>
    private static HttpClient? _selfClient;
    private string? _resolvedUrl;

    /// <summary>
    /// Base URL for resolving relative paths (e.g., "/api/test" → "http://localhost:5000/api/test").
    /// Auto-detected from the server's listening address, or set via HttpClientOptions.SelfBaseUrl.
    /// </summary>
    internal static string? SelfBaseUrl { get; set; }

    /// <summary>
    /// Set a custom HttpClient for self-referencing calls (e.g., from WebApplicationFactory TestServer).
    /// </summary>
    internal static void SetSelfClient(HttpClient client)
    {
        _selfClient = client;
    }

    // Response properties
    private int StatusCode { get; set; }
    private string? Body { get; set; }
    private string? ResponseHeaders { get; set; }
    private string? ContentType { get; set; }
    private bool IsSuccess { get; set; }
    private string? ErrorMessage { get; set; }

    public async Task InvokeAsync(CancellationToken cancellationToken = default)
    {
        int maxRetries = definition.RetryDelays?.Length ?? 0;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            var startTimestamp = Stopwatch.GetTimestamp();
            _resolvedUrl = ResolveValue(definition.Url);
            var resolvedUrl = _resolvedUrl;
            if (attempt == 0)
            {
                Logger?.LogDebug("HTTP client starting {Method} request to '{Url}'", definition.Method, resolvedUrl);
            }
            else
            {
                Logger?.LogDebug("HTTP client retrying {Method} request to '{Url}' (attempt {Attempt}/{MaxRetries})",
                    definition.Method, resolvedUrl, attempt + 1, maxRetries + 1);
            }

            try
            {
                bool isSelfCall = definition.Url.StartsWith('/');

                // Use internal request handler for self-calls (bypasses HTTP stack entirely)
                if (isSelfCall && InternalRequestHandler.IsAvailable)
                {
                    var url = definition.NeedsParsing && replacements is not null
                        ? Formatter.FormatString(definition.Url.AsSpan(), replacements.Value).ToString()
                        : definition.Url;

                    var internalResponse = await InternalRequestHandler.ExecuteAsync(
                        definition.Method,
                        url,
                        definition.Headers,
                        definition.NeedsParsing && definition.Body is not null && replacements is not null
                            ? Formatter.FormatString(definition.Body.AsSpan(), replacements.Value).ToString()
                            : definition.Body,
                        definition.ContentType,
                        cancellationToken);

                    StatusCode = internalResponse.StatusCode;
                    Body = internalResponse.Body;
                    ContentType = internalResponse.ContentType;
                    ResponseHeaders = internalResponse.Headers;
                    IsSuccess = internalResponse.IsSuccess;
                    ErrorMessage = IsSuccess ? null : $"Internal request returned {StatusCode}";

                    var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
                    Logger?.LogDebug("Internal request to '{Url}' completed with status {StatusCode} in {Elapsed}ms",
                        resolvedUrl, StatusCode, elapsed.TotalMilliseconds.ToString("F1"));

                    if (!IsSuccess && attempt < maxRetries && ShouldRetry(StatusCode))
                    {
                        Logger?.LogWarning("Internal request to '{Url}' returned {StatusCode}, retrying after {Delay}ms",
                            resolvedUrl, StatusCode, definition.RetryDelays![attempt].TotalMilliseconds);
                        await Task.Delay(definition.RetryDelays![attempt], cancellationToken);
                        continue;
                    }
                    return;
                }

                using var request = CreateRequest();
                using var cts = CreateTimeoutCancellationTokenSource(cancellationToken);

                var client = isSelfCall && _selfClient is not null ? _selfClient : SharedClient;
                using var response = await client.SendAsync(request, cts?.Token ?? cancellationToken);

                await ProcessResponseAsync(response, startTimestamp);

                if (!IsSuccess && attempt < maxRetries && ShouldRetry(StatusCode))
                {
                    Logger?.LogWarning("HTTP client request to '{Url}' returned {StatusCode}, retrying after {Delay}ms",
                        resolvedUrl, StatusCode, definition.RetryDelays![attempt].TotalMilliseconds);
                    await Task.Delay(definition.RetryDelays![attempt], cancellationToken);
                    continue;
                }
                return;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                StatusCode = 408;
                IsSuccess = false;
                ErrorMessage = $"Request timed out after {definition.Timeout?.TotalSeconds ?? 30} seconds";
                if (attempt < maxRetries)
                {
                    Logger?.LogWarning("HTTP client request to '{Url}' timed out, retrying after {Delay}ms",
                        resolvedUrl, definition.RetryDelays![attempt].TotalMilliseconds);
                    await Task.Delay(definition.RetryDelays![attempt], cancellationToken);
                    continue;
                }
                Logger?.LogWarning("HTTP client request to '{Url}' timed out after {Timeout}s",
                    resolvedUrl, definition.Timeout?.TotalSeconds ?? 30);
            }
            catch (HttpRequestException ex)
            {
                StatusCode = (int?)ex.StatusCode ?? 0;
                IsSuccess = false;
                ErrorMessage = ex.Message;
                if (attempt < maxRetries)
                {
                    Logger?.LogWarning("HTTP client request to '{Url}' failed ({Message}), retrying after {Delay}ms",
                        resolvedUrl, ex.Message, definition.RetryDelays![attempt].TotalMilliseconds);
                    await Task.Delay(definition.RetryDelays![attempt], cancellationToken);
                    continue;
                }
                Logger?.LogError(ex, "HTTP client request to '{Url}' failed with status {StatusCode}",
                    resolvedUrl, StatusCode);
            }
            catch (Exception ex)
            {
                StatusCode = 0;
                IsSuccess = false;
                ErrorMessage = ex.Message;
                Logger?.LogError(ex, "HTTP client request to '{Url}' failed with unexpected error", resolvedUrl);
                return; // Unexpected errors are not retryable
            }
        }
    }

    private bool ShouldRetry(int statusCode)
    {
        if (definition.RetryOnStatusCodes is null)
        {
            return true; // No filter = retry any failure
        }
        return definition.RetryOnStatusCodes.Contains(statusCode);
    }

    private HttpRequestMessage CreateRequest()
    {
        var url = ResolveValue(definition.Url);

        // Resolve relative paths against the server's own base URL (skip if _selfClient handles it via BaseAddress)
        if (url.StartsWith('/') && _selfClient is null && SelfBaseUrl is not null)
        {
            url = string.Concat(SelfBaseUrl, url);
        }

        var method = new HttpMethod(definition.Method);

        var request = new HttpRequestMessage(method, url);

        // Add headers
        if (definition.Headers is { Count: > 0 })
        {
            foreach (var header in definition.Headers)
            {
                var headerValue = ResolveValue(header.Value);
                request.Headers.TryAddWithoutValidation(header.Key, headerValue);
            }
        }

        // Add body
        if (definition.Body is not null)
        {
            var body = ResolveValue(definition.Body);
            var contentType = definition.ContentType is not null
                ? ResolveValue(definition.ContentType)
                : "application/json";

            request.Content = new StringContent(body);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        return request;
    }

    private CancellationTokenSource? CreateTimeoutCancellationTokenSource(CancellationToken cancellationToken)
    {
        if (definition.Timeout is null)
        {
            return null;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(definition.Timeout.Value);
        return cts;
    }

    private async Task ProcessResponseAsync(HttpResponseMessage response, long startTimestamp)
    {
        StatusCode = (int)response.StatusCode;
        IsSuccess = response.IsSuccessStatusCode;
        ContentType = response.Content.Headers.ContentType?.ToString();
        Body = await response.Content.ReadAsStringAsync();

        // Build response headers as JSON object string
        ResponseHeaders = BuildHeadersJson(response);

        var duration = Stopwatch.GetElapsedTime(startTimestamp);
        Logger?.LogDebug("HTTP client request to '{Url}' completed with status {StatusCode}, content-type: {ContentType}, body length: {BodyLength}, duration: {Duration}ms",
            _resolvedUrl ?? definition.Url, StatusCode, ContentType, Body?.Length ?? 0, duration.TotalMilliseconds);
    }

    private static string BuildHeadersJson(HttpResponseMessage response)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        bool first = true;

        foreach (var header in response.Headers)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(PgConverters.SerializeString(header.Key));
            sb.Append(':');
            sb.Append(PgConverters.SerializeString(string.Join(", ", header.Value)));
        }

        foreach (var header in response.Content.Headers)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(PgConverters.SerializeString(header.Key));
            sb.Append(':');
            sb.Append(PgConverters.SerializeString(string.Join(", ", header.Value)));
        }

        sb.Append('}');
        return sb.ToString();
    }

    private string ResolveValue(string value)
    {
        if (definition.NeedsParsing is false || replacements is null)
        {
            return value;
        }

        return new string(Formatter.FormatString(value.AsSpan(), replacements.Value));
    }

    public static async Task InvokeAllAsync(
        IEnumerable<string> typeNames,
        Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>>? replacements,
        NpgsqlParameterCollection parameters,
        CancellationToken cancellationToken)
    {
        var handlers = new Dictionary<string, HttpClientTypeHandler>();
        var tasks = new List<(string TypeName, HttpClientTypeHandler Handler, Task Task)>();

        foreach (var typeName in typeNames)
        {
            if (HttpClientTypes.Definitions.TryGetValue(typeName, out var definition))
            {
                var handler = new HttpClientTypeHandler(definition, replacements);
                handlers[typeName] = handler;
                tasks.Add((typeName, handler, handler.InvokeAsync(cancellationToken)));
            }
        }

        await Task.WhenAll(tasks.Select(t => t.Task));

        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = (NpgsqlRestParameter)parameters[i];
            if (parameter.TypeDescriptor.CustomType is null)
            {
                continue;
            }

            if (!handlers.TryGetValue(parameter.TypeDescriptor.CustomType, out var handler))
            {
                continue;
            }
            
            // For parameters with NpgsqlDbType.Unknown (e.g., SQL file source), all values must be strings
            bool asText = parameter.NpgsqlDbType == NpgsqlDbType.Unknown || parameter.TypeDescriptor.IsText;

            if (string.Equals(parameter.TypeDescriptor.CustomTypeName, Options.HttpClientOptions.ResponseStatusCodeField, StringComparison.InvariantCulture))
            {
                parameter.Value = asText ? handler.StatusCode.ToString() : handler.StatusCode;
            }
            else if (string.Equals(parameter.TypeDescriptor.CustomTypeName, Options.HttpClientOptions.ResponseBodyField, StringComparison.InvariantCulture))
            {
                parameter.Value = (object?)handler.Body ?? DBNull.Value;
            }
            else if (string.Equals(parameter.TypeDescriptor.CustomTypeName, Options.HttpClientOptions.ResponseHeadersField, StringComparison.InvariantCulture))
            {
                parameter.Value = (object?)handler.ResponseHeaders ?? DBNull.Value;
            }
            else if (string.Equals(parameter.TypeDescriptor.CustomTypeName, Options.HttpClientOptions.ResponseContentTypeField, StringComparison.InvariantCulture))
            {
                parameter.Value = (object?)handler.ContentType ?? DBNull.Value;
            }
            else if (string.Equals(parameter.TypeDescriptor.CustomTypeName, Options.HttpClientOptions.ResponseSuccessField, StringComparison.InvariantCulture))
            {
                parameter.Value = asText ? (object)handler.IsSuccess.ToString().ToLowerInvariant() : handler.IsSuccess;
            }
            else if (string.Equals(parameter.TypeDescriptor.CustomTypeName, Options.HttpClientOptions.ResponseErrorMessageField, StringComparison.InvariantCulture))
            {
                parameter.Value = (object?)handler.ErrorMessage ?? DBNull.Value;
            } 
        }
    }
}
