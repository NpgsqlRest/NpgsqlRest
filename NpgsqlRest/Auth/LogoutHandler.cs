using Microsoft.AspNetCore.Authentication;
using Npgsql;

namespace NpgsqlRest.Auth;

public static class LogoutHandler
{
    public static async Task HandleAsync(NpgsqlCommand command, RoutineEndpoint endpoint, HttpContext context, CancellationToken cancellationToken = default)
    {
        var path = string.Concat(endpoint.Method.ToString(), " ", endpoint.Path);
        command.LogCommand(path);

        if (endpoint.Routine.IsVoid)
        {
            await command.ExecuteNonQueryWithRetryAsync(
                endpoint.RetryStrategy,
                cancellationToken,
                errorCodePolicy: endpoint.ErrorCodePolicy ?? NpgsqlRestOptions.Options.ErrorHandlingOptions.DefaultErrorCodePolicy);
            await SignOutAsync(context, null);
            await context.Response.CompleteAsync();
            return;
        }

        List<string> schemes = new(5);
        await using NpgsqlDataReader reader = await command.ExecuteReaderWithRetryAsync(
            endpoint.RetryStrategy,
            cancellationToken,
            errorCodePolicy: endpoint.ErrorCodePolicy ?? NpgsqlRestOptions.Options.ErrorHandlingOptions.DefaultErrorCodePolicy);

        while (await reader!.ReadAsync(cancellationToken))
        {
            for (int i = 0; i < reader?.FieldCount; i++)
            {
                if (await reader.IsDBNullAsync(i, cancellationToken) is true)
                {
                    continue;
                }

                var descriptor = endpoint.Routine.ColumnsTypeDescriptor[i];
                if (descriptor.IsArray)
                {
                    object[]? values = reader?.GetValue(i) as object[];
                    for (int j = 0; j < values?.Length; j++)
                    {
                        var value = values[j]?.ToString();
                        if (value is not null)
                        {
                            schemes.Add(value);
                        }
                    }
                }
                else
                {
                    string? value = reader?.GetValue(i)?.ToString();
                    if (value is not null)
                    {
                        schemes.Add(value);
                    }
                }
            }
        }
        await SignOutAsync(context, schemes.Count == 0 ? null : schemes);
        await context.Response.CompleteAsync();
    }

    /// <summary>
    /// Signs out of the given schemes, or of the default sign-out scheme when none are given.
    /// A scheme whose handler cannot sign out is skipped: a JWT bearer scheme keeps no session on the server
    /// (the client discards its tokens), and asking it to sign out throws ("The authentication handler registered
    /// for scheme 'jwt' is 'JwtBearerHandler' which cannot be used for SignOutAsync"), which answered 500 to a
    /// logout request that carried a JWT. With no schemes given, the scheme that authenticated the request is the
    /// one looked at, because the default scheme may be a policy scheme that forwards to it.
    /// </summary>
    private static async Task SignOutAsync(HttpContext context, List<string>? schemes)
    {
        var handlers = context.RequestServices.GetService<IAuthenticationHandlerProvider>();
        if (schemes is null)
        {
            if (handlers is not null)
            {
                var authenticatedScheme = (await context.AuthenticateAsync()).Ticket?.AuthenticationScheme;
                // A request that is not signed in (no token, or an expired one) has no scheme of its own: what
                // would be asked to sign out is the default sign-out scheme. In an application whose only scheme
                // is JWT that is the bearer handler again, and the logout answered 500.
                authenticatedScheme ??= context.RequestServices.GetService<IAuthenticationSchemeProvider>() is { } provider
                    ? (await provider.GetDefaultSignOutSchemeAsync())?.Name
                    : null;
                if (authenticatedScheme is not null && await CanSignOutAsync(handlers, context, authenticatedScheme) is false)
                {
                    return;
                }
            }
            await context.SignOutAsync();
            return;
        }
        foreach (var scheme in schemes)
        {
            if (handlers is not null && await CanSignOutAsync(handlers, context, scheme) is false)
            {
                continue;
            }
            await context.SignOutAsync(scheme);
        }
    }

    // False only when the scheme has a handler and that handler is not a sign-out handler. An unknown scheme
    // name is left to SignOutAsync, which reports it.
    private static async Task<bool> CanSignOutAsync(IAuthenticationHandlerProvider handlers, HttpContext context, string scheme)
    {
        var handler = await handlers.GetHandlerAsync(context, scheme);
        return handler is null or IAuthenticationSignOutHandler;
    }
}