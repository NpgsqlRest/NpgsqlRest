namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: sse_subscribe | sse_events_subscribe
    /// Syntax: sse_subscribe
    ///         sse_subscribe [path]
    ///         sse_subscribe [path] on [info | notice | warning]
    ///
    /// Description: Expose an SSE subscribe URL for this routine — the subscribe half of <c>@sse</c>.
    /// The routine's body is NEVER executed when a client opens an EventSource against the URL; it
    /// only registers the path and (optionally) the notice level filter on outgoing events. Pair
    /// with <c>@sse_publish</c> routines (or other <c>@sse</c> routines) elsewhere to source the
    /// events that flow through this URL.
    /// </summary>
    private static readonly string[] SseEventsSubscribeKey = [
        "sse_subscribe",
        "sse_events_subscribe",
    ];

    private static void HandleSseEventsSubscribe(
        RoutineEndpoint endpoint,
        string[] wordsLower,
        string[] words,
        int len,
        string description)
    {
        // Subscribe-only: URL exposed but RAISE in this routine's body is NOT forwarded. Don't touch
        // SsePublishEnabled — leaving it false is the whole point of the decomposition.
        if (len == 1)
        {
            endpoint.SseEventsPath =
                (endpoint.SseEventNoticeLevel ?? Options.DefaultSseEventNoticeLevel).ToString().ToLowerInvariant();
            CommentLogger?.CommentSseStreamingPath(description, endpoint.SseEventsPath);
        }
        else
        {
            endpoint.SseEventsPath = wordsLower[1];
            if (len >= 4 && StrEquals(wordsLower[2], "on"))
            {
                if (Enum.TryParse<PostgresNoticeLevels>(words[3], true, out var parsedLevel))
                {
                    endpoint.SseEventNoticeLevel = parsedLevel;
                    CommentLogger?.CommentSseStreamingPathAndLevel(description, endpoint.SseEventsPath, endpoint.SseEventNoticeLevel.Value);
                }
                else
                {
                    Logger?.LogError("Could not recognize valid value for parameter key {key}. Valid values are: {values}. Provided value is {provided}.",
                        wordsLower[0], string.Join(", ", Enum.GetNames<PostgresNoticeLevels>()), description);
                }
            }
            else
            {
                CommentLogger?.CommentSseStreamingPath(description, endpoint.SseEventsPath);
            }
        }
    }
}
