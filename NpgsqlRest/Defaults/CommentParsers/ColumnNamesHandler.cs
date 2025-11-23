namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: columns | names | column_names
    /// Syntax: columns
    ///
    /// Description: Include column names in raw mode output.
    /// </summary>
    private static readonly string[] ColumnNamesKey = [
        "columns",
        "names",
        "column_names",
    ];

    private static void HandleColumnNames(
        RoutineEndpoint endpoint,
        string description)
    {
        endpoint.RawColumnNames = true;
        Logger?.CommentRawSetColumnNames(description);
    }
}
