namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: raw | raw_mode | raw_results
    /// Syntax: raw
    ///
    /// Description: Return raw results without JSON formatting.
    /// </summary>
    private static readonly string[] RawKey = [
        "raw",
        "raw_mode",
        "raw_results",
    ];

    private static void HandleRaw(
        RoutineEndpoint endpoint,
        string description)
    {
        Logger?.CommentSetRawMode(description);
        endpoint.Raw = true;
    }
}
