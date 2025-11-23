namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: sse_level | sse_events_level
    /// Syntax: sse_level [info | notice | warning]
    ///
    /// Description: Set the PostgreSQL notice level for Server-Sent Events.
    /// </summary>
    private static readonly string[] SseEventsLevelKey = [
        "sse_level",
        "sse_events_level",
    ];

    private static void HandleSseEventsLevel(
        RoutineEndpoint endpoint,
        string[] wordsLower,
        string[] words,
        string line,
        string description)
    {
        if (Enum.TryParse<PostgresNoticeLevels>(words[1], true, out var parsedLevel))
        {
            endpoint.SseEventNoticeLevel = parsedLevel;
            Logger?.CommentSseStreamingLevel(description, endpoint.SseEventNoticeLevel.Value);
        }
        else
        {
            Logger?.LogError("Could not recognize valid value for parameter key {key}. Valid values are: {values}. Provided value is {provided}.",
                wordsLower[0], string.Join(", ", Enum.GetNames<PostgresNoticeLevels>()), line);
        }
    }
}
