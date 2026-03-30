namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: single | single_record | single_result
    /// Syntax: single
    ///
    /// Description: Return only the first row as a JSON object instead of a JSON array.
    /// If the query returns multiple rows, only the first row is returned.
    /// </summary>
    private static readonly string[] SingleKey = [
        "single",
        "single_record",
        "single_result",
    ];

    private static void HandleSingle(
        RoutineEndpoint endpoint,
        string description)
    {
        CommentLogger?.CommentSetSingleRecord(description);
        endpoint.ReturnSingleRecord = true;
    }
}
