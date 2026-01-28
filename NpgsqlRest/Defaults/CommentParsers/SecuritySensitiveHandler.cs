namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: sensitive | security | security_sensitive
    /// Syntax: sensitive
    ///
    /// Description: Marks the endpoint as security sensitive which will obfuscate
    /// any parameters before sending it to log.
    /// </summary>
    private static readonly string[] SecuritySensitiveKey = [
        "sensitive",
        "security",
        "security_sensitive",
    ];

    private static void HandleSecuritySensitive(RoutineEndpoint endpoint, string description)
    {
        endpoint.SecuritySensitive = true;
        CommentLogger?.CommentSecuritySensitive(description);
    }
}
