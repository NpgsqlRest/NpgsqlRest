namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: authorize | authorized | requires_authorization
    /// Syntax: authorize
    ///         authorize [role1, role2, role3 [, ...]]
    ///
    /// Description: Require authorization for this endpoint.
    /// - If the user is not authorized and authorization is required,
    ///   the endpoint will return the status code 401 Unauthorized.
    /// - If the user is authorized but not in any of the roles required by the authorization,
    ///   the endpoint will return the status code 403 Forbidden.
    /// </summary>
    private static readonly string[] AuthorizeKey = [
        "authorize",
        "authorized",
        "requires_authorization",
    ];

    private static void HandleAuthorize(RoutineEndpoint endpoint, string[] wordsLower, string description)
    {
        endpoint.RequiresAuthorization = true;
        if (wordsLower.Length > 1)
        {
            endpoint.AuthorizeRoles = [.. wordsLower[1..]];
            CommentLogger?.CommentSetAuthRoles(description, endpoint.AuthorizeRoles);
        }
        else
        {
            CommentLogger?.CommentSetAuth(description);
        }
    }
}
