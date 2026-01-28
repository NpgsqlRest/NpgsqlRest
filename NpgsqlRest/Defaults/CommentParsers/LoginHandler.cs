namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: login | signin
    /// Syntax: login
    ///
    /// Description: This annotation will transform the routine into the authentication endpoint
    /// that performs the sign-in operation.
    ///
    /// Login endpoint expects a PostgreSQL command that will be executed to authenticate the user
    /// that follow this convention:
    /// - Must return at least one record when authentication is successful.
    ///   If no records are returned endpoint will return 401 Unauthorized.
    /// - If record is returned, the authentication is successful, if not set in StatusColumnName column otherwise.
    /// - All records will be added to user principal claim collection where column name is claim type
    ///   and column value is claim value, except for three special columns defined in StatusColumnName,
    ///   SchemeColumnName and MessageColumnName options.
    /// </summary>
    private static readonly string[] LoginKey = [
        "login",
        "signin",
    ];

    private static void HandleLogin(RoutineEndpoint endpoint, string description)
    {
        endpoint.Login = true;
        CommentLogger?.CommentSetLogin(description);
    }
}
