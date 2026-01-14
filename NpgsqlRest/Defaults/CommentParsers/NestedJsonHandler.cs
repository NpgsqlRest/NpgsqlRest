namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: nested | nested_json | nested_composite
    /// Syntax: nested
    ///
    /// Description: Serialize composite type columns as nested JSON objects instead of flattening them.
    /// For example, a column "req" of type "my_request(id int, name text)" becomes {"req": {"id": 1, "name": "test"}}
    /// instead of the default flat structure {"id": 1, "name": "test"}.
    /// </summary>
    private static readonly string[] NestedJsonKey = [
        "nested",
        "nested_json",
        "nested_composite",
    ];

    private static void HandleNestedJson(
        RoutineEndpoint endpoint,
        string description)
    {
        Logger?.CommentSetNestedJson(description);
        endpoint.NestedJsonForCompositeTypes = true;
    }
}
