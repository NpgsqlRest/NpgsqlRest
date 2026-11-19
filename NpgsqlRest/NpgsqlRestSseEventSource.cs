using Npgsql;
using NpgsqlRest.Defaults;

namespace NpgsqlRest;

public readonly record struct SseEvent(
    PostgresNotice Notice,
    RoutineEndpoint? Endpoint,
    string? ExecutionId);

public class NpgsqlRestSseEventSource(RequestDelegate next)
{
    public static readonly HashSet<string> Paths = [];
    /// <summary>
    /// Events buffered for one SSE subscriber that is not reading (a stalled or deliberately idle client). When the
    /// buffer is full the oldest pending event is dropped, so idle subscribers cannot grow server memory without limit.
    /// </summary>
    public const int MaxPendingEventsPerSubscriber = 10_000;

    public static readonly Broadcaster<SseEvent> Broadcaster = new(MaxPendingEventsPerSubscriber);

    /// <summary>
    /// Formats a message as one SSE event. Every line of the message gets its own <c>data:</c> line (the client joins
    /// them back with "\n"), so a line break in the message cannot start another field (<c>event:</c>, <c>id:</c>,
    /// <c>retry:</c>) or end the event early and forge a second one.
    /// </summary>
    internal static string FormatEventData(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return "data: \n\n";
        }
        if (message.AsSpan().IndexOfAny('\r', '\n') < 0)
        {
            return string.Concat("data: ", message, "\n\n");
        }
        var sb = new System.Text.StringBuilder(message.Length + 16);
        sb.Append("data: ");
        for (var i = 0; i < message.Length; i++)
        {
            var ch = message[i];
            if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r' && i + 1 < message.Length && message[i + 1] == '\n')
                {
                    i++;
                }
                sb.Append("\ndata: ");
                continue;
            }
            sb.Append(ch);
        }
        sb.Append("\n\n");
        return sb.ToString();
    }

    /// <summary>
    /// True if any endpoint in the build participates in SSE — either as a publisher (<c>@sse</c> /
    /// <c>@sse_publish</c>) or as a subscribe URL (<c>@sse</c> / <c>@sse_subscribe</c>). Used as a fast
    /// guard for the unbound-RAISE warning so projects that don't use SSE at all pay zero per-notice
    /// cost. Once any endpoint registers SSE usage, every other endpoint's notices are eligible to
    /// trigger the warning when their <c>RAISE</c> level matches the configured forwarding level.
    /// </summary>
    public static bool HasAnySseEndpoints { get; internal set; } = false;
    
    public async Task InvokeAsync(HttpContext context)
    {
        if (Paths.Contains(context.Request.Path) is false)
        {
            await next(context);
            return;
        }

        var executionId = context.Request.QueryString.HasValue ? context.Request.QueryString.Value[1..] : null;

        var cancellationToken = context.RequestAborted;
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate, max-age=0";
        context.Response.Headers.Connection = "keep-alive";

        if (Options.SseResponseHeaders.Count > 0)
        {
            foreach (var header in Options.SseResponseHeaders)
            {
                if (context.Response.Headers.ContainsKey(header.Key))
                {
                    context.Response.Headers[header.Key] = header.Value;
                }
                else
                {
                    context.Response.Headers.Append(header.Key, header.Value);
                }
            }
        }

        var connectionId = Guid.NewGuid();
        var reader = Broadcaster.Subscribe(connectionId);
        // Force the response headers + an initial body byte to flush immediately. SSE permits
        // comment-only lines (lines starting with ':'); browsers and EventSource clients ignore
        // them. Without an early flush, the GET stays "headers pending" until the first real event
        // — which prevents callers (notably integration tests) from sequencing "subscribe THEN
        // publish": their GET wouldn't return until an event had already been received, and the
        // server's await-foreach below blocks waiting for that same event. Writing a small comment
        // breaks the deadlock and acts as a connection-established ping.
        try
        {
            await context.Response.WriteAsync(": connected\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Broadcaster.Unsubscribe(connectionId);
            return;
        }
        try
        {
            await foreach (var noticeEvent in reader.ReadAllAsync(cancellationToken))
            {
                var endpoint = noticeEvent.Endpoint;
                var scope = noticeEvent.Endpoint?.SseEventsScope;
                var infoEventsRoles = endpoint?.SseEventsRoles;
                
                if (string.IsNullOrEmpty(executionId) is false && string.IsNullOrEmpty(noticeEvent.ExecutionId) is false)
                {
                    if (string.Equals(executionId, noticeEvent.ExecutionId, StringComparison.Ordinal) is false)
                    {
                        continue; // Skip events not matching the current execution ID
                    }
                }

                if (string.IsNullOrEmpty(noticeEvent.Notice.Hint) is false)
                {
                    string hint = noticeEvent.Notice.Hint;
                    var words = hint.SplitWords();
                    if (words is not null && words.Length > 0 && Enum.TryParse<SseEventsScope>(words[0], true, out var parsedScope))
                    {
                        scope = parsedScope;
                        if (scope == SseEventsScope.Authorize && words.Length > 1)
                        {
                            // Merge the hint roles into a per-event COPY. (They used to be added to the endpoint's
                            // shared SseEventsRoles set: hint roles accumulated permanently, so later events reached
                            // recipients meant only for earlier ones, and the set was mutated concurrently.)
                            var merged = infoEventsRoles is null
                                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                                : new HashSet<string>(infoEventsRoles, StringComparer.OrdinalIgnoreCase);
                            foreach (var word in words[1..])
                            {
                                if (string.IsNullOrWhiteSpace(word) is false)
                                {
                                    merged.Add(word);
                                }
                            }
                            infoEventsRoles = merged;
                        }
                    }
                    else
                    {
                        Logger?.LogError("Could not recognize valid value for parameter key {key}. Valid values are: {values}. Provided value is {provided}.",
                            words?[0], string.Join(", ", Enum.GetNames<SseEventsScope>()), hint);
                    }
                }

                // NOTE: deliberately NOT an `else if` - the scope checks below must run regardless of
                // whether the scope came from the endpoint annotation or was overridden by the RAISE hint.
                // (Chaining them as `else if` delivered hint-scoped events to EVERY subscriber.)
                if (scope == SseEventsScope.Matching)
                {
                    if (context.User?.Identity?.IsAuthenticated is false && 
                        (endpoint?.RequiresAuthorization is true || endpoint?.AuthorizeRoles is not null))
                    {
                        continue; // Skip events for unauthorized users
                    }

                    if (endpoint?.AuthorizeRoles is not null &&
                        RoutineEndpoint.MatchesAuthorizeList(endpoint.AuthorizeRoles, context.User, Options.AuthenticationOptions) is false)
                    {
                        continue;
                    }
                }
                else if (scope == SseEventsScope.Authorize)
                {
                    if (context.User?.Identity?.IsAuthenticated is false)
                    {
                        continue; // Skip events for unauthorized users
                    }

                    if (infoEventsRoles is not null &&
                        RoutineEndpoint.MatchesAuthorizeList(infoEventsRoles, context.User, Options.AuthenticationOptions) is false)
                    {
                        continue;
                    }
                }

                try
                {
                    await context.Response.WriteAsync(FormatEventData(noticeEvent.Notice.MessageText), cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "Failed to write notice event to response at path {path} (ExecutionId={executionId})", context.Request.Path, executionId);
                }
            }
        }
        finally
        {
            Broadcaster.Unsubscribe(connectionId);
        }
    }
}
