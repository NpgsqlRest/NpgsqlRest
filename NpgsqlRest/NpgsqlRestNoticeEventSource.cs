using Microsoft.AspNetCore.Routing;
using Npgsql;
using NpgsqlRest.Defaults;

namespace NpgsqlRest;

public readonly record struct NoticeEvent(
    PostgresNotice Notice,
    RoutineEndpoint? Endpoint,
    string? ExecutionId);

public class NpgsqlRestNoticeEventSource(RequestDelegate next, ILogger? logger)
{
    public static readonly HashSet<string> Paths = [];
    public static readonly Broadcaster<NoticeEvent> Broadcaster = new();
    
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

        if (NpgsqlRestOptions.Options.CustomServerSentEventsResponseHeaders.Count > 0)
        {
            foreach (var header in NpgsqlRestOptions.Options.CustomServerSentEventsResponseHeaders)
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
                var scope = noticeEvent.Endpoint?.InfoEventsScope;
                var infoEventsRoles = endpoint?.InfoEventsRoles;

                if (string.IsNullOrEmpty(noticeEvent.Notice.Hint) is false)
                {
                    string hint = noticeEvent.Notice.Hint;
                    var words = hint.SplitWords();
                    if (words is not null && words.Length > 0 && Enum.TryParse<InfoEventsScope>(words[0], true, out var parsedScope))
                    {
                        scope = parsedScope;
                        if (scope == InfoEventsScope.Authorize && words.Length > 1)
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
                        logger?.LogError("Could not recognize valid value for parameter key {key}. Valid values are: {values}. Provided value is {provided}.",
                            words?[0], string.Join(", ", Enum.GetNames<InfoEventsScope>()), hint);
                    }
                }

                if (scope == InfoEventsScope.Self)
                {
                    if (string.Equals(noticeEvent.ExecutionId, executionId, StringComparison.Ordinal) is false)
                    {
                        continue; // Skip events not matching the current execution ID
                    }
                }
                else if (scope == InfoEventsScope.Matching)
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
                            if (string.Equals(claim.Type, NpgsqlRestOptions.Options.AuthenticationOptions.DefaultRoleClaimType, StringComparison.Ordinal))
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
                else if (scope == InfoEventsScope.Authorize)
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
                                    string.Equals(claim.Type, NpgsqlRestOptions.Options.AuthenticationOptions.DefaultUserIdClaimType, StringComparison.Ordinal) ||
                                    string.Equals(claim.Type, NpgsqlRestOptions.Options.AuthenticationOptions.DefaultNameClaimType, StringComparison.Ordinal) ||
                                    string.Equals(claim.Type, NpgsqlRestOptions.Options.AuthenticationOptions.DefaultRoleClaimType, StringComparison.Ordinal)
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
                    logger?.LogError(ex, "Failed to write notice event to response at path {path} (ExecutionId={executionId})", context.Request.Path, executionId);
                }
            }
        }
        finally
        {
            Broadcaster.Unsubscribe(connectionId);
        }
    }
}
