namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: allow_anonymous | anonymous | allow_anon | anon
    /// Syntax: allow_anonymous
    ///
    /// Description: Allow anonymous access with no authorization to this endpoint.
    /// </summary>
    private static readonly string[] AllowAnonymousKey = [
        "allow_anonymous",
        "anonymous",
        "allow_anon",
        "anon"
    ];

    private static void HandleAllowAnonymous(RoutineEndpoint endpoint, string description)
    {
        endpoint.RequiresAuthorization = false;
        Logger?.CommentSetAnon(description);
    }
}
