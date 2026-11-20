namespace NpgsqlRest.TsClient;

/// <summary>The custom parameters (key = value) the TypeScript client generator reads, as annotation reference data.</summary>
public static class TsClientAnnotations
{
    public static CommentAnnotation[] All =>
    [
        new()
        {
            Name = "tsclient",
            Aliases = ["tsclient"],
            Syntax = "tsclient = <true|false>",
            Description = "Set to false to leave this endpoint out of the generated TypeScript client. Accepted values: true, on, enabled, 1 and false, off, disabled, 0.",
            Owner = "tsclient",
            Form = AnnotationForm.KeyValue,
            ValueKind = AnnotationValueKind.Boolean,
            Default = "true",
            Example = "tsclient = false"
        },
        new()
        {
            Name = "tsclient_module",
            Aliases = ["tsclient_module"],
            Syntax = "tsclient_module = <name>",
            Description = "The module (file) this endpoint's TypeScript code is generated into; endpoints with the same module share a file. SQL file endpoints get their directory name by default.",
            Owner = "tsclient",
            Form = AnnotationForm.KeyValue,
            ValueKind = AnnotationValueKind.Name,
            Example = "tsclient_module = users"
        },
        new()
        {
            Name = "tsclient_events",
            Aliases = ["tsclient_events"],
            Syntax = "tsclient_events = <true|false>",
            Description = "Set to false to leave the Server-Sent Events parameters out of this endpoint's generated function.",
            Owner = "tsclient",
            Form = AnnotationForm.KeyValue,
            ValueKind = AnnotationValueKind.Boolean,
            Default = "true",
            Example = "tsclient_events = false"
        },
        new()
        {
            Name = "tsclient_parse_url",
            Aliases = ["tsclient_parse_url"],
            Syntax = "tsclient_parse_url = <true|false>",
            Description = "Add (or remove) the parseUrl parameter, a callback that may change the request URL. Default: the IncludeParseUrlParam option.",
            Owner = "tsclient",
            Form = AnnotationForm.KeyValue,
            ValueKind = AnnotationValueKind.Boolean,
            Example = "tsclient_parse_url = true"
        },
        new()
        {
            Name = "tsclient_parse_request",
            Aliases = ["tsclient_parse_request"],
            Syntax = "tsclient_parse_request = <true|false>",
            Description = "Add (or remove) the parseRequest parameter, a callback that may change the request before it is sent. Default: the IncludeParseRequestParam option.",
            Owner = "tsclient",
            Form = AnnotationForm.KeyValue,
            ValueKind = AnnotationValueKind.Boolean,
            Example = "tsclient_parse_request = true"
        },
        new()
        {
            Name = "tsclient_status_code",
            Aliases = ["tsclient_status_code"],
            Syntax = "tsclient_status_code = <true|false>",
            Description = "Return the HTTP status code together with the response. Default: the IncludeStatusCode option.",
            Owner = "tsclient",
            Form = AnnotationForm.KeyValue,
            ValueKind = AnnotationValueKind.Boolean,
            Example = "tsclient_status_code = true"
        },
        new()
        {
            Name = "tsclient_export_url",
            Aliases = ["tsclient_export_url"],
            Syntax = "tsclient_export_url = <true|false>",
            Description = "Also export a function that builds this endpoint's URL. Default: the ExportUrls option.",
            Owner = "tsclient",
            Form = AnnotationForm.KeyValue,
            ValueKind = AnnotationValueKind.Boolean,
            Example = "tsclient_export_url = true"
        },
        new()
        {
            Name = "tsclient_url_only",
            Aliases = ["tsclient_url_only"],
            Syntax = "tsclient_url_only = <true|false>",
            Description = "Generate only the URL function and the request type for this endpoint, no fetch function.",
            Owner = "tsclient",
            Form = AnnotationForm.KeyValue,
            ValueKind = AnnotationValueKind.Boolean,
            Default = "false",
            Example = "tsclient_url_only = true"
        },
        new()
        {
            Name = "tsclient_hooks",
            Aliases = ["tsclient_hooks"],
            Syntax = "tsclient_hooks = <true|false>",
            Description = "Set to false to leave this endpoint out of the generated React Query hooks.",
            Owner = "tsclient",
            Form = AnnotationForm.KeyValue,
            ValueKind = AnnotationValueKind.Boolean,
            Default = "true",
            Example = "tsclient_hooks = false"
        },
    ];
}
