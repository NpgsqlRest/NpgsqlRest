namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: sse_scope | sse_events_scope
    /// Syntax: sse_scope [matching | authorize | all]
    ///         sse_scope authorize [role_or_user1, role_or_user2, role_or_user3 [, ...]]
    ///
    /// Description: Set the scope of Server-Sent Events distribution.
    /// </summary>
    private static readonly string[] SseEventsStreamingScopeKey = [
        "sse_scope",
        "sse_events_scope",
    ];

    private static void HandleSseEventsScope(
        RoutineEndpoint endpoint,
        string[] wordsLower,
        string line,
        string description)
    {
        if (wordsLower.Length > 1 && Enum.TryParse<SseEventsScope>(wordsLower[1], true, out var parsedScope))
        {
            endpoint.SseEventsScope = parsedScope;
            if (parsedScope == SseEventsScope.Authorize && wordsLower.Length > 2)
            {
                endpoint.SseEventsRoles ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var word in wordsLower[2..])
                {
                    if (string.IsNullOrWhiteSpace(word) is false)
                    {
                        endpoint.SseEventsRoles.Add(word);
                    }
                }
                Logger?.CommentSseStreamingScopeRoles(description, endpoint.SseEventsRoles);
            }
            else
            {
                Logger?.CommentSseStreamingScope(description, endpoint.SseEventsScope);
            }
        }
        else
        {
            Logger?.LogError("Could not recognize valid value for parameter key {key}. Valid values are: {values}. Provided value is {provided}.",
                wordsLower[0], string.Join(", ", Enum.GetNames<SseEventsScope>()), line);
        }
    }
}
