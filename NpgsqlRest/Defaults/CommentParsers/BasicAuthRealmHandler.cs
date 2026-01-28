namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: basic_authentication_realm | basic_auth_realm | realm
    /// Syntax: basic_authentication_realm [realm]
    ///
    /// Description: Set Basic Authentication Realm for this endpoint.
    /// Note: basic authentication must be enabled for this to take effect.
    /// </summary>
    private static readonly string[] BasicAuthRealmKey = [
        "basic_authentication_realm",
        "basic_auth_realm",
        "realm",
    ];

    private static void HandleBasicAuthRealm(RoutineEndpoint endpoint, string[] words, string description)
    {
        if (endpoint.BasicAuth is null)
        {
            endpoint.BasicAuth = new() { Enabled = true };
            CommentLogger?.BasicAuthEnabled(description);
        }
        endpoint.BasicAuth.Realm = words[1];
        CommentLogger?.BasicAuthRealmSet(description, endpoint.BasicAuth.Realm);
    }
}
