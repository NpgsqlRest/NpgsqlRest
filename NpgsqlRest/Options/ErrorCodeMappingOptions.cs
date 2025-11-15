namespace NpgsqlRest;

public class ErrorCodeMappingOptions
{
    /// <summary>
    /// HTTP Status Code 
    /// </summary>
    public int StatusCode { get; set; } = 500;
    
    /// <summary>
    /// Optional title field in response JSON. When null, actual error message is used.
    /// </summary>
    public string? Title { get; set; } = null;
    
    /// <summary>
    /// Details: Optional details field in response JSON. When null, PostgreSQL Error Code is used.
    /// </summary>
    public string? Details { get; set; } = null;
    
    /// <summary>
    /// Optional types field in response JSON. A URI reference [RFC3986] that identifies the problem type. St to null to use default. Or RemoveTypeUrl to true to disable.
    /// </summary>
    public string? Type { get; set; } = null;

    public override string ToString()
    {
        List<string> parts = new(4);
        if (Title != null)
        {
            parts.Add($"Title: {Title}");
        }
        if (Details != null)
        {
            parts.Add($"Details: {Details}");
        }
        if (Type != null)
        {
            parts.Add($"Type: {Type}");
        }
        parts.Add($"StatusCode: {StatusCode}");
        return string.Join(", ", parts);
    }
}
