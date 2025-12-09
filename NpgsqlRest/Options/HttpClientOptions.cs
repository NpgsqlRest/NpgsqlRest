namespace NpgsqlRest;

public class HttpClientOptions
{
    /// <summary>
    /// Enable HTTP client functionality for annotated types.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Filter schema names [similar to](https://www.postgresql.org/docs/current/functions-matching.html#FUNCTIONS-SIMILARTO-REGEXP) this parameter or `null` to ignore this parameter.
    /// </summary>
    public string? SchemaSimilarTo { get; set; }

    /// <summary>
    /// Filter schema names NOT [similar to](https://www.postgresql.org/docs/current/functions-matching.html#FUNCTIONS-SIMILARTO-REGEXP) this parameter or `null` to ignore this parameter.
    /// </summary>
    public string? SchemaNotSimilarTo { get; set; }

    /// <summary>
    /// List of schema names to be included or  `null` to ignore this parameter.
    /// </summary>
    public string[]? IncludeSchemas { get; set; }

    /// <summary>
    /// List of schema names to be excluded or  `null` to ignore this parameter. 
    /// </summary>
    public string[]? ExcludeSchemas { get; set; }

    /// <summary>
    /// Filter names [similar to](https://www.postgresql.org/docs/current/functions-matching.html#FUNCTIONS-SIMILARTO-REGEXP) this parameter or `null` to ignore this parameter.
    /// </summary>
    public string? NameSimilarTo { get; set; }

    /// <summary>
    /// Filter names NOT [similar to](https://www.postgresql.org/docs/current/functions-matching.html#FUNCTIONS-SIMILARTO-REGEXP) this parameter or `null` to ignore this parameter.
    /// </summary>
    public string? NameNotSimilarTo { get; set; }

    /// <summary>
    /// List of names to be included or `null` to ignore this parameter.
    /// </summary>
    public string[]? IncludeNames { get; set; }

    /// <summary>
    /// List of names to be excluded or `null` to ignore this parameter.
    /// </summary>
    public string[]? ExcludeNames { get; set; }
    
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
