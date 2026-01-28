namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: logout | signout
    /// Syntax: logout
    ///
    /// Description: This annotation will transform the routine into the endpoint that performs
    /// the logout or the sign-out operation.
    ///
    /// Logout endpoint expects a PostgreSQL command that performs the logout or the sign-out operation.
    /// If the routine doesn't return any data, the default authorization scheme is signed out.
    /// Any values returned will be interpreted as scheme names (converted to string) to sign out.
    /// </summary>
    private static readonly string[] LogoutKey = [
        "logout",
        "signout",
    ];

    private static void HandleLogout(RoutineEndpoint endpoint, string description)
    {
        endpoint.Logout = true;
        CommentLogger?.CommentSetLogout(description);
    }
}
