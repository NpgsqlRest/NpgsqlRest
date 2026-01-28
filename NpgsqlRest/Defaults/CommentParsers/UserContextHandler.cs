namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: user_context
    /// Syntax: user_context
    ///
    /// Description: Enable user context for this endpoint.
    /// </summary>
    private static readonly string[] UserContextKey = [
        "user_context"
    ];

    private static void HandleUserContext(
        RoutineEndpoint endpoint,
        string description)
    {
        endpoint.UserContext = true;
        CommentLogger?.CommentUserContext(description);
    }
}
