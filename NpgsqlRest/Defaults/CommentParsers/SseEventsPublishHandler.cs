namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: sse_publish | sse_events_publish
    /// Syntax: sse_publish
    ///         sse_publish on [info | notice | warning]
    ///
    /// Description: Forward this routine's <c>RAISE</c> statements to the SSE broadcaster — the
    /// publish half of <c>@sse</c>. Does NOT expose a subscribe URL on this routine's path; pair
    /// with an <c>@sse_subscribe</c> routine elsewhere (or rely on existing subscribers connecting
    /// via other subscribe URLs sharing the broadcaster).
    /// </summary>
    private static readonly string[] SseEventsPublishKey = [
        "sse_publish",
        "sse_events_publish",
    ];

    private static void HandleSseEventsPublish(
        RoutineEndpoint endpoint,
        string[] wordsLower,
        string[] words,
        int len,
        string description)
    {
        endpoint.SsePublishEnabled = true;

        if (len >= 3 && StrEquals(wordsLower[1], "on"))
        {
            if (Enum.TryParse<PostgresNoticeLevels>(words[2], true, out var parsedLevel))
            {
                endpoint.SseEventNoticeLevel = parsedLevel;
                CommentLogger?.CommentSseStreamingLevel(description, endpoint.SseEventNoticeLevel.Value);
            }
            else
            {
                Logger?.LogError("Could not recognize valid value for parameter key {key}. Valid values are: {values}. Provided value is {provided}.",
                    wordsLower[0], string.Join(", ", Enum.GetNames<PostgresNoticeLevels>()), description);
            }
        }
    }
}
