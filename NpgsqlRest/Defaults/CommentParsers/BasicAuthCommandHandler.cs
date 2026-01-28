namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: basic_authentication_command | basic_auth_command | challenge_command
    /// Syntax: basic_authentication_command [command]
    ///
    /// Description: Set Basic Authentication challenge command for this endpoint.
    /// Note: basic authentication must be enabled for this to take effect.
    ///
    /// This command will always be executed regardless if endpoint has set password and username in configuration.
    /// This command will always perform validation if it is present.
    /// Same rules will apply when using login command and it can return valid claims if necessary.
    ///
    /// It takes 4 optional parameters:
    /// - $1: Unnamed, positional and optional parameter containing username from basic authentication header.
    /// - $2: Unnamed, positional and optional parameter containing password from basic authentication header.
    /// - $3: Unnamed, positional and optional parameter containing basic authentication realm.
    /// - $4: Unnamed, positional and optional parameter containing endpoint path.
    /// </summary>
    private static readonly string[] BasicAuthCommandKey = [
        "basic_authentication_command",
        "basic_auth_command",
        "challenge_command",
    ];

    private static void HandleBasicAuthCommand(RoutineEndpoint endpoint, string[] words, string line, string description)
    {
        if (endpoint.BasicAuth is null)
        {
            endpoint.BasicAuth = new() { Enabled = true };
            CommentLogger?.BasicAuthEnabled(description);
        }
        endpoint.BasicAuth.ChallengeCommand = line[(words[0].Length + 1)..];
        CommentLogger?.BasicAuthChallengeCommandSet(description, endpoint.BasicAuth.ChallengeCommand);
    }
}
