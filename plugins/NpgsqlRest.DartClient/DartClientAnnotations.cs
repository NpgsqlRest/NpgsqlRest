namespace NpgsqlRest.DartClient;

/// <summary>The custom parameters (key = value) the Dart client generator reads, as annotation reference data.</summary>
public static class DartClientAnnotations
{
    public static CommentAnnotation[] All =>
    [
        new()
        {
            Name = "dartclient",
            Aliases = ["dartclient"],
            Syntax = "dartclient = <true|false>",
            Description = "Set to false to leave this endpoint out of the generated Dart client. Accepted values: true, on, enabled, 1 and false, off, disabled, 0.",
            Owner = "dartclient",
            Form = AnnotationForm.KeyValue,
            ValueKind = AnnotationValueKind.Boolean,
            Default = "true",
            Example = "dartclient = false"
        },
        new()
        {
            Name = "dartclient_module",
            Aliases = ["dartclient_module"],
            Syntax = "dartclient_module = <name>",
            Description = "The module (file) this endpoint's Dart code is generated into; endpoints with the same module share a file. Without it, dartclient falls back to tsclient_module.",
            Owner = "dartclient",
            Form = AnnotationForm.KeyValue,
            ValueKind = AnnotationValueKind.Name,
            Example = "dartclient_module = users"
        },
        new()
        {
            Name = "dartclient_events",
            Aliases = ["dartclient_events"],
            Syntax = "dartclient_events = <true|false>",
            Description = "Set to false to leave the Server-Sent Events parameters out of this endpoint's generated function.",
            Owner = "dartclient",
            Form = AnnotationForm.KeyValue,
            ValueKind = AnnotationValueKind.Boolean,
            Default = "true",
            Example = "dartclient_events = false"
        },
        new()
        {
            Name = "dartclient_parse_url",
            Aliases = ["dartclient_parse_url"],
            Syntax = "dartclient_parse_url = <true|false>",
            Description = "Add (or remove) the parseUrl parameter, a callback that may change the request URL. Default: the IncludeParseUrlParam option.",
            Owner = "dartclient",
            Form = AnnotationForm.KeyValue,
            ValueKind = AnnotationValueKind.Boolean,
            Example = "dartclient_parse_url = true"
        },
        new()
        {
            Name = "dartclient_parse_request",
            Aliases = ["dartclient_parse_request"],
            Syntax = "dartclient_parse_request = <true|false>",
            Description = "Add (or remove) the parseRequest parameter, a callback that may change the request before it is sent. Default: the IncludeParseRequestParam option.",
            Owner = "dartclient",
            Form = AnnotationForm.KeyValue,
            ValueKind = AnnotationValueKind.Boolean,
            Example = "dartclient_parse_request = true"
        },
        new()
        {
            Name = "dartclient_status_code",
            Aliases = ["dartclient_status_code"],
            Syntax = "dartclient_status_code = <true|false>",
            Description = "Return the HTTP status code together with the response. Default: the IncludeStatusCode option.",
            Owner = "dartclient",
            Form = AnnotationForm.KeyValue,
            ValueKind = AnnotationValueKind.Boolean,
            Example = "dartclient_status_code = true"
        },
        new()
        {
            Name = "dartclient_export_url",
            Aliases = ["dartclient_export_url"],
            Syntax = "dartclient_export_url = <true|false>",
            Description = "Also export a function that builds this endpoint's URL. Default: the ExportUrls option.",
            Owner = "dartclient",
            Form = AnnotationForm.KeyValue,
            ValueKind = AnnotationValueKind.Boolean,
            Example = "dartclient_export_url = true"
        },
        new()
        {
            Name = "dartclient_url_only",
            Aliases = ["dartclient_url_only"],
            Syntax = "dartclient_url_only = <true|false>",
            Description = "Generate only the URL function and the request type for this endpoint, no fetch function.",
            Owner = "dartclient",
            Form = AnnotationForm.KeyValue,
            ValueKind = AnnotationValueKind.Boolean,
            Default = "false",
            Example = "dartclient_url_only = true"
        },
    ];
}
