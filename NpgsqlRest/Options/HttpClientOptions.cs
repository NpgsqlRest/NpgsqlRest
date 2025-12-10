namespace NpgsqlRest;

public class HttpClientOptions
{
    /// <summary>
    /// Enable HTTP client functionality for annotated types.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Default name for the response status code field within annotated types.
    /// </summary>
    public string ResponseStatusCodeField { get; set; } = "status_code";
    
    /// <summary>
    /// Default name for the response body field within annotated types.
    /// </summary>
    public string ResponseBodyField { get; set; } = "body";
    
    /// <summary>
    /// Default name for the response headers field within annotated types.
    /// </summary>
    public string ResponseHeadersField { get; set; } = "headers";
    
    /// <summary>
    /// Default name for the response content type field within annotated types.
    /// </summary>
    public string ResponseContentTypeField { get; set; } = "content_type";

    /// <summary>
    /// Default name for the response success field within annotated types.
    /// </summary>
    public string ResponseSuccessField { get; set; } = "success";
    
    /// <summary>
    /// Default name for the response error message field within annotated types.
    /// </summary>
    public string ResponseErrorMessageField { get; set; } = "error_message";
}
