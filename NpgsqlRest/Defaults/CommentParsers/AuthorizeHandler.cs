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
            // Since 4.0.0 plain values are roles only; user names and ids need the user: / id: prefix.
            // A plain number or GUID was almost certainly meant as a user id - point that out.
            foreach (var value in endpoint.AuthorizeRoles)
            {
                if (long.TryParse(value, out _) || Guid.TryParse(value, out _))
                {
                    CommentLogger?.LogWarning(
                        "{description}: authorize value \"{value}\" is matched against the role claim only. To authorize a user id write \"{prefix}{value}\" (and \"{namePrefix}name\" for a user name).",
                        description, value, RoutineEndpoint.AuthorizeUserIdPrefix, value, RoutineEndpoint.AuthorizeUserNamePrefix);
                }
            }
        }
        else
        {
            CommentLogger?.CommentSetAuth(description);
        }
    }
}
