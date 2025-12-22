namespace NpgsqlRest.Proxy;

/// <summary>
/// Represents the response from a proxy request.
/// </summary>
public class ProxyResponse
{
    /// <summary>
    /// HTTP status code from the proxy response.
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// Response body as string.
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// Response headers as JSON object string.
    /// </summary>
    public string? Headers { get; set; }

    /// <summary>
    /// Content-Type header value.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// True if status code is 2xx.
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Error message if the request failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Raw response headers for forwarding.
    /// </summary>
    public Dictionary<string, string[]>? RawHeaders { get; set; }

    /// <summary>
    /// Raw body bytes for binary content.
    /// </summary>
    public byte[]? RawBody { get; set; }
}
