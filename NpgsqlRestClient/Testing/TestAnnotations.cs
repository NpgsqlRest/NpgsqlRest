using NpgsqlRest;

namespace NpgsqlRestClient.Testing;

/// <summary>What a SQL test file (npgsqlrest --test) may say in its comments, as annotation reference data.</summary>
public static class TestAnnotations
{
    private const AnnotationContext Ctx = AnnotationContext.TestFile;

    public static CommentAnnotation[] All =>
    [
        new()
        {
            Name = "setup",
            Aliases = ["setup"],
            Syntax = "-- @setup <step> [, <step> ...]",
            Description = "Run these steps (named in TestRunner:Steps) before the file. In the file's header only: the comment lines at its top. An unknown step is an error and the file does not run.",
            Owner = "test", Contexts = Ctx, ValueKind = AnnotationValueKind.List,
            Example = "-- @setup seed_users"
        },
        new()
        {
            Name = "teardown",
            Aliases = ["teardown"],
            Syntax = "-- @teardown <step> [, <step> ...]",
            Description = "Run these steps (named in TestRunner:Steps) after the file, whether it passed or not. In the file's header only.",
            Owner = "test", Contexts = Ctx, ValueKind = AnnotationValueKind.List,
            Example = "-- @teardown remove_users"
        },
        new()
        {
            Name = "connection",
            Aliases = ["connection"],
            Syntax = "-- @connection <name>",
            Description = "Run the file's SQL and its endpoint calls on this connection (a name in ConnectionStrings) instead of the run's test connection. In the file's header only.",
            Owner = "test", Contexts = Ctx, ValueKind = AnnotationValueKind.Name,
            Example = "-- @connection reporting"
        },
        new()
        {
            Name = "tag",
            Aliases = ["tag"],
            Syntax = "-- @tag <name> [, <name> ...]",
            Description = "Tags for this file, which TestRunner:Tag and TestRunner:ExcludeTag select or leave out. In the file's header only.",
            Owner = "test", Contexts = Ctx, ValueKind = AnnotationValueKind.List,
            Example = "-- @tag smoke, auth"
        },
        new()
        {
            Name = "include",
            Aliases = ["\\i", "\\ir"],
            Syntax = "\\i <path> | \\ir <path>",
            Description = "Include another file here, as if its text were pasted: \\i reads the path from the working directory, \\ir from the including file's directory. On a line of its own, between statements. The rest of the line is the path.",
            Owner = "test", Contexts = Ctx, Form = AnnotationForm.Line, ValueKind = AnnotationValueKind.Path,
            Example = "\\ir fixtures/users.sql"
        },
        new()
        {
            Name = "test_request",
            Syntax = "/* GET|POST|PUT|DELETE /path[?query] ... */",
            Description = "A block comment that starts with a request line calls that endpoint in-process. Header lines and # @claim / # @response directives follow the request line, then a blank line, then the body. The response lands in a temporary table (_response, or _response_1, _response_2 ... when the file has several calls) with the columns status, body, content_type, headers and is_success, which the SQL after it asserts on.",
            Owner = "test", Contexts = Ctx, Form = AnnotationForm.Line,
            Example = "/* GET /api/users?id=1 */"
        },
        new()
        {
            Name = "claim",
            Aliases = ["claim"],
            Syntax = "# @claim <name>=<value>",
            Description = "Inside a request block: the call is made by a signed-in user with this claim. Repeat it for more claims. Without any, the call is anonymous.",
            Owner = "test", Contexts = Ctx, ValueKind = AnnotationValueKind.Text,
            Example = "# @claim user_id=42"
        },
        new()
        {
            Name = "response",
            Aliases = ["response"],
            Syntax = "# @response <table_name>",
            Description = "Inside a request block: the name of the temporary table that receives this call's response.",
            Owner = "test", Contexts = Ctx, ValueKind = AnnotationValueKind.Name,
            Example = "# @response created"
        },
    ];
}
