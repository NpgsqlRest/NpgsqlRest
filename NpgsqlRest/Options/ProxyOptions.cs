namespace NpgsqlRest;

/// <summary>
/// Options for configuring reverse proxy functionality for NpgsqlRest endpoints.
/// When an endpoint is marked as a proxy, incoming requests are forwarded to another URL.
/// </summary>
public class ProxyOptions
{
    /// <summary>
    /// Enable proxy functionality for annotated endpoints.
    /// When false, proxy annotations are ignored.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Base URL (host) for proxy requests (e.g., "https://api.example.com").
    /// When set, proxy endpoints will forward requests to this host + the original path.
    /// Can be overridden per-endpoint via comment annotation.
    /// </summary>
    public string? Host { get; set; } = null;

    /// <summary>
    /// Default timeout for all proxy requests.
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When true, original request headers are forwarded to the proxy target.
    /// Headers in ExcludeHeaders are not forwarded.
    /// </summary>
    public bool ForwardHeaders { get; set; } = true;

    /// <summary>
    /// Headers to exclude from forwarding to the proxy target.
    /// Default excludes Host, Content-Length, and Transfer-Encoding.
    /// </summary>
    public HashSet<string> ExcludeHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host",
        "Content-Length",
        "Transfer-Encoding"
    };

    /// <summary>
    /// When true, forward response headers from proxy back to client.
    /// Headers in ExcludeResponseHeaders are not forwarded.
    /// </summary>
    public bool ForwardResponseHeaders { get; set; } = true;

    /// <summary>
    /// Response headers to exclude from forwarding back to client.
    /// </summary>
    public HashSet<string> ExcludeResponseHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "Transfer-Encoding",
        "Content-Length"
    };

    /// <summary>
    /// Default name for the proxy response status code parameter.
    /// If a routine has a parameter with this name, it will receive the proxy response status code.
    /// </summary>
    public string ResponseStatusCodeParameter { get; set; } = "_proxy_status_code";

    /// <summary>
    /// Default name for the proxy response body parameter.
    /// If a routine has a parameter with this name, it will receive the proxy response body.
    /// </summary>
    public string ResponseBodyParameter { get; set; } = "_proxy_body";

    /// <summary>
    /// Default name for the proxy response headers parameter.
    /// If a routine has a parameter with this name, it will receive the proxy response headers as JSON.
    /// </summary>
    public string ResponseHeadersParameter { get; set; } = "_proxy_headers";

    /// <summary>
    /// Default name for the proxy response content type parameter.
    /// If a routine has a parameter with this name, it will receive the proxy response content type.
    /// </summary>
    public string ResponseContentTypeParameter { get; set; } = "_proxy_content_type";

    /// <summary>
    /// Default name for the proxy response success parameter.
    /// If a routine has a parameter with this name, it will receive a boolean indicating if the request succeeded (2xx status).
    /// </summary>
    public string ResponseSuccessParameter { get; set; } = "_proxy_success";

    /// <summary>
    /// Default name for the proxy response error message parameter.
    /// If a routine has a parameter with this name, it will receive any error message from the proxy request.
    /// </summary>
    public string ResponseErrorMessageParameter { get; set; } = "_proxy_error_message";

    /// <summary>
    /// When true, for upload endpoints marked as proxy, the raw multipart/form-data content
    /// is forwarded directly to the upstream proxy instead of being processed locally.
    /// This allows the upstream service to handle file uploads.
    /// When false (default), upload endpoints with proxy annotation will process uploads locally
    /// and upload metadata will not be available to the proxy.
    /// </summary>
    public bool ForwardUploadContent { get; set; } = false;
}
