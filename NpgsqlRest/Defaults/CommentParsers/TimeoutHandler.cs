namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: command_timeout | timeout
    /// Syntax: command_timeout [seconds]
    ///
    /// Description: Set the command execution timeout in seconds.
    /// Value uses PostgreSQL interval format (e.g., '30 seconds' or '30s', '1 minute' or '1min').
    /// </summary>
    private static readonly string[] TimeoutKey = [
        "command_timeout",
        "timeout"
    ];

    private static void HandleTimeout(RoutineEndpoint endpoint, string[] wordsLower, string description)
    {
        var parsedInterval = Parser.ParsePostgresInterval(wordsLower[1]);
        if (parsedInterval is null)
        {
            Logger?.InvalidTimeoutComment(wordsLower[1], description, endpoint.CommandTimeout);
        }
        else if (endpoint.CommandTimeout != parsedInterval)
        {
            CommentLogger?.CommentSetTimeout(description, parsedInterval);
        }
        endpoint.CommandTimeout = parsedInterval;
    }
}
