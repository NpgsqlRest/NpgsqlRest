namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: sse | sse_path | sse_events_path
    /// Syntax: sse
    ///         sse [path]
    ///         sse [path] on [info | notice | warning]
    ///
    /// Description: Enable Server-Sent Events for PostgreSQL notices on this path.
    /// </summary>
    private static readonly string[] SseEventsStreamingPathKey = [
        "sse",
        "sse_path",
        "sse_events_path",
    ];

    private static void HandleSseEventsPath(
        RoutineEndpoint endpoint,
        string[] wordsLower,
        string[] words,
        int len,
        string description)
    {
        if (len == 1)
        {
            endpoint.SseEventsPath =
                (endpoint.SseEventNoticeLevel ?? Options.DefaultSseEventNoticeLevel).ToString();
            Logger?.CommentSseStreamingPath(description, endpoint.SseEventsPath);
        }
        else
        {
            endpoint.SseEventsPath = wordsLower[1];
            if (len >= 4 && StrEquals(wordsLower[2], "on"))
            {
                if (Enum.TryParse<PostgresNoticeLevels>(words[3], true, out var parsedLevel))
                {
                    endpoint.SseEventNoticeLevel = parsedLevel;
                    Logger?.CommentSseStreamingPathAndLevel(description, endpoint.SseEventsPath, endpoint.SseEventNoticeLevel.Value);
                }
                else
                {
                    Logger?.LogError("Could not recognize valid value for parameter key {key}. Valid values are: {values}. Provided value is {provided}.",
                        wordsLower[0], string.Join(", ", Enum.GetNames<PostgresNoticeLevels>()), description);
                }
            }
            else
            {
                Logger?.CommentSseStreamingPath(description, endpoint.SseEventsPath);
            }
        }
    }
}
