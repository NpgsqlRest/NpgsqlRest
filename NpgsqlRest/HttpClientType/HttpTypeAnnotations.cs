namespace NpgsqlRest.HttpClientType;

/// <summary>
/// What the comment of a composite type may say to make it an HTTP Custom Type (a type whose value is the
/// response of an HTTP call), as annotation reference data.
/// </summary>
public static class HttpTypeAnnotations
{
    private const AnnotationContext Ctx = AnnotationContext.TypeComment;

    public static CommentAnnotation[] All =>
    [
        new()
        {
            Name = "http_type_request",
            Syntax = "GET|POST|PUT|PATCH|DELETE <url>",
            Description = "The request line: the HTTP method and the URL to call, absolute, or starting with / for a call to an endpoint of this application. Only directives and # comment lines may come before it. Header lines follow it, then a blank line, then the request body. {name} placeholders in the URL, the headers and the body take the routine's parameter values. The response fills the type's fields by name: body, status_code, headers, content_type, success, error_message (the names are configurable in HttpClientOptions).",
            Owner = "http_type",
            Form = AnnotationForm.Line,
            Contexts = Ctx,
            Example = "GET https://api.example.com/users/{_id}"
        },
        new()
        {
            Name = "http_type_header",
            Syntax = "Header-Name: value",
            Description = "A request header, on the lines after the request line. A Content-Type header sets the body's content type (application/json when there is a body and no header).",
            Owner = "http_type",
            Form = AnnotationForm.Line,
            Contexts = Ctx,
            Example = "Authorization: Bearer {_token}"
        },
        new()
        {
            Name = "timeout",
            Aliases = ["timeout"],
            Syntax = "timeout <interval>",
            Description = "How long one attempt may take. A number is seconds; 10s, 500ms, 2 minutes and 00:00:10 are also read. On a timeout status_code is 408 and error_message says so.",
            Owner = "http_type",
            Form = AnnotationForm.KeywordOrKeyValue,
            Contexts = Ctx,
            ValueKind = AnnotationValueKind.Interval,
            Default = "30s",
            Example = "@timeout 10s"
        },
        new()
        {
            Name = "retry_delay",
            Aliases = ["retry_delay"],
            Syntax = "retry_delay <interval>, <interval>, ... [on <status>, <status>, ...]",
            Description = "Retry a failed call: one retry per delay, each waited before its retry. Without 'on', any response outside 200-299 is retried; with it, only the listed status codes. Timeouts and network errors are always retried.",
            Owner = "http_type",
            Form = AnnotationForm.KeywordOrKeyValue,
            Contexts = Ctx,
            ValueKind = AnnotationValueKind.List,
            Default = "no retries",
            Example = "@retry_delay 1s, 3s, 10s on 429, 503"
        },
        new()
        {
            Name = "cache",
            Aliases = ["cache"],
            Syntax = "cache [interval]",
            Description = "Keep the response in memory and answer identical calls from it, for the given time (without a time, until the application stops). GET requests only, and only responses with status 200-299.",
            Owner = "http_type",
            Form = AnnotationForm.KeywordOrKeyValue,
            Contexts = Ctx,
            ValueKind = AnnotationValueKind.Interval,
            Default = "off",
            Example = "@cache 5 minutes"
        },
    ];
}
