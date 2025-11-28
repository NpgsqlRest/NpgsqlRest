namespace NpgsqlRest;

public class ErrorHandlingOptions
{
    public string? DefaultErrorCodePolicy { get; set; } = "Default";
    
    public ErrorCodeMappingOptions? TimeoutErrorMapping { get; set; } = new()
    {
        StatusCode = 504,
        Title = "Command execution timed out"
    };

    public Dictionary<string, Dictionary<string, ErrorCodeMappingOptions>> ErrorCodePolicies { get; set; } = new()
    {
        ["Default"] = new()
        {
            { "42501", new() { StatusCode = 403, Title = "Insufficient Privilege" } },
            { "57014", new() { StatusCode = 205, Title = "Cancelled" } },
            { "P0001", new() { StatusCode = 400 } },
            { "P0004", new() { StatusCode = 400 } },
        }
    };
}
