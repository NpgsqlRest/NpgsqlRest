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
    public static readonly Broadcaster<SseEvent> Broadcaster = new();
    
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
                            // if info roles already exist, merge them with the new ones
                            infoEventsRoles ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var word in words[1..])
                            {
                                if (infoEventsRoles.Contains(word) is false && string.IsNullOrWhiteSpace(word) is false)
                                {
                                    infoEventsRoles.Add(word);
                                }
                            }
                        }
                    }
                    else
                    {
                        Logger?.LogError("Could not recognize valid value for parameter key {key}. Valid values are: {values}. Provided value is {provided}.",
                            words?[0], string.Join(", ", Enum.GetNames<SseEventsScope>()), hint);
                    }
                }
                
                else if (scope == SseEventsScope.Matching)
                {
                    if (context.User?.Identity?.IsAuthenticated is false && 
                        (endpoint?.RequiresAuthorization is true || endpoint?.AuthorizeRoles is not null))
                    {
                        continue; // Skip events for unauthorized users
                    }

                    if (endpoint?.AuthorizeRoles is not null)
                    {
                        bool ok = false;
                        foreach (var claim in context.User?.Claims ?? [])
                        {
                            if (string.Equals(claim.Type, Options.AuthenticationOptions.DefaultRoleClaimType, StringComparison.Ordinal))
                            {
                                if (endpoint?.AuthorizeRoles.Contains(claim.Value) is true)
                                {
                                    ok = true;
                                    break;
                                }
                            }
                        }
                        if (ok is false)
                        {
                            continue;
                        }
                    }
                }
                else if (scope == SseEventsScope.Authorize)
                {
                    if (context.User?.Identity?.IsAuthenticated is false)
                    {
                        continue; // Skip events for unauthorized users
                    }

                    if (infoEventsRoles is not null)
                    {
                        bool ok = false;
                        if (context.User?.Claims is not null)
                        {
                            foreach (var claim in context.User?.Claims!)
                            {
                                if (
                                    string.Equals(claim.Type, Options.AuthenticationOptions.DefaultUserIdClaimType, StringComparison.Ordinal) ||
                                    string.Equals(claim.Type, Options.AuthenticationOptions.DefaultNameClaimType, StringComparison.Ordinal) ||
                                    string.Equals(claim.Type, Options.AuthenticationOptions.DefaultRoleClaimType, StringComparison.Ordinal)
                                    )
                                {
                                    if (infoEventsRoles.Contains(claim.Value) is true)
                                    {
                                        ok = true;
                                        break;
                                    }
                                }
                            }
                        }
                        if (ok is false)
                        {
                            continue;
                        }
                    }
                }

                try
                {
                    await context.Response.WriteAsync($"data: {noticeEvent.Notice.MessageText}\n\n", cancellationToken);
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
