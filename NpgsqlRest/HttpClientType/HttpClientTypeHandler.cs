using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Npgsql;

namespace NpgsqlRest.HttpClientType;

public class HttpClientTypeHandler(HttpTypeDefinition definition, Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>>? replacements = null)
{
    private static readonly HttpClient SharedClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan // We handle timeout per-request
    };

    // Response properties
    private int StatusCode { get; set; }
    private string? Body { get; set; }
    private string? ResponseHeaders { get; set; }
    private string? ContentType { get; set; }
    private bool IsSuccess { get; set; }
    private string? ErrorMessage { get; set; }

    public async Task InvokeAsync(CancellationToken cancellationToken = default)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        Logger?.LogDebug("HTTP client starting {Method} request to '{Url}'", definition.Method, definition.Url);

        try
        {
            using var request = CreateRequest();
            using var cts = CreateTimeoutCancellationTokenSource(cancellationToken);

            using var response = await SharedClient.SendAsync(request, cts?.Token ?? cancellationToken);

            await ProcessResponseAsync(response, startTimestamp);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            StatusCode = 408; // Request Timeout
            IsSuccess = false;
            ErrorMessage = $"Request timed out after {definition.Timeout?.TotalSeconds ?? 30} seconds";
            Logger?.LogWarning("HTTP client request to '{Url}' timed out after {Timeout}s", definition.Url, definition.Timeout?.TotalSeconds ?? 30);
        }
        catch (HttpRequestException ex)
        {
            StatusCode = (int?)ex.StatusCode ?? 0;
            IsSuccess = false;
            ErrorMessage = ex.Message;
            Logger?.LogError(ex, "HTTP client request to '{Url}' failed with status {StatusCode}", definition.Url, StatusCode);
        }
        catch (Exception ex)
        {
            StatusCode = 0;
            IsSuccess = false;
            ErrorMessage = ex.Message;
            Logger?.LogError(ex, "HTTP client request to '{Url}' failed with unexpected error", definition.Url);
        }
    }

    private HttpRequestMessage CreateRequest()
    {
        var url = ResolveValue(definition.Url);
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
            definition.Url, StatusCode, ContentType, Body?.Length ?? 0, duration.TotalMilliseconds);
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
            
            if (string.Equals(parameter.TypeDescriptor.CustomTypeName, Options.HttpClientOptions.ResponseStatusCodeField, StringComparison.InvariantCulture))
            {
                if (parameter.TypeDescriptor.IsText)
                {
                    parameter.Value = handler.StatusCode.ToString();
                }
                else
                {
                    parameter.Value = handler.StatusCode;
                }
            }
            else  if (string.Equals(parameter.TypeDescriptor.CustomTypeName, Options.HttpClientOptions.ResponseBodyField, StringComparison.InvariantCulture))
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
                parameter.Value = (object?)handler.IsSuccess ?? DBNull.Value;
            }
            else if (string.Equals(parameter.TypeDescriptor.CustomTypeName, Options.HttpClientOptions.ResponseErrorMessageField, StringComparison.InvariantCulture))
            {
                parameter.Value = (object?)handler.ErrorMessage ?? DBNull.Value;
            } 
        }
    }
}
