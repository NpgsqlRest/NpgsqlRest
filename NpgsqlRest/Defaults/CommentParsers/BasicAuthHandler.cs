namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: basic_authentication | basic_auth
    /// Syntax: basic_authentication [[username] [password]]
    ///
    /// Description: Enable Basic Authentication for this endpoint.
    /// Optionally, set the expected password or username and password.
    /// If no username or password is set, default will be used from configuration.
    /// </summary>
    private static readonly string[] BasicAuthKey = [
        "basic_authentication",
        "basic_auth",
    ];

    private static void HandleBasicAuth(RoutineEndpoint endpoint, string[] words, int len, string description)
    {
        if (endpoint.BasicAuth is null)
        {
            endpoint.BasicAuth = new() { Enabled = true };
            CommentLogger?.BasicAuthEnabled(description);
        }

        if (len >= 3)
        {
            var username = words[1];
            var password = words[2];
            if (string.IsNullOrEmpty(username) is false && string.IsNullOrEmpty(password) is false)
            {
                endpoint.BasicAuth.Users[username] = password;
                CommentLogger?.BasicAuthUserAdded(description, username);
            }
        }
        else
        {
            Logger?.BasicAuthUserFailed(description);
        }
    }
}
