namespace NpgsqlRest.HttpClientType;

public class HttpTypeDefinition
{
    public string Method { get; set; } = default!;
    public string Url { get; set; } = default!;
    public Dictionary<string, string>? Headers { get; set; }
    public string? Body { get; set; }
    public string? ContentType { get; set; }
    public TimeSpan? Timeout { get; set; }
    public bool NeedsParsing { get; set; }
}