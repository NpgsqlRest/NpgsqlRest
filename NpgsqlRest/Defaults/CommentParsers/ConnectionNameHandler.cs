namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: connection | connection_name
    /// Syntax: connection [name]
    ///
    /// Description: Specify which named connection to use for this endpoint.
    /// </summary>
    private static readonly string[] ConnectionNameKey = [
        "connection",
        "connection_name",
    ];

    private static void HandleConnectionName(
        RoutineEndpoint endpoint,
        string[] wordsLower,
        string description)
    {
        var name = string.Join(Consts.Space, wordsLower[1..]);
        if (string.IsNullOrEmpty(name) is false)
        {
            if (Options.ConnectionStrings is null || Options.ConnectionStrings.ContainsKey(name) is false)
            {
                Logger?.CommentInvalidConnectionName(description, name);
            }
            endpoint.ConnectionName = name;
            CommentLogger?.CommentConnectionName(description, name);
        }
        else
        {
            Logger?.CommentEmptyConnectionName(description);
        }
    }
}
