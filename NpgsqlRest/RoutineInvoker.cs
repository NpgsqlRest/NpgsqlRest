using System.Security.Claims;
using NpgsqlRest.HttpClientType;

namespace NpgsqlRest;

/// <summary>
/// Result of a programmatic routine invocation via <see cref="RoutineInvoker"/>.
/// </summary>
public readonly record struct RoutineInvokeResult(int StatusCode, string? Body, string? ContentType, bool IsSuccess);

/// <summary>
/// Public entry point for invoking an NpgsqlRest endpoint in-process (no network hop), running the
/// full endpoint pipeline against a synthetic request. This is the supported surface for plugins
/// (e.g. NpgsqlRest.Mcp's tools/call) and host code to execute routines — so plugins never need
/// access to core internals.
/// <para>
/// Available only after <c>UseNpgsqlRest</c> has built the endpoints (see <see cref="IsAvailable"/>).
/// Pass the <c>user</c> argument to run as a specific principal — execution-level authorization
/// (`authorize`) and claims-to-parameter binding then apply as for a real authenticated request.
/// </para>
/// </summary>
public static class RoutineInvoker
{
    /// <summary>True once endpoints have been built and internal invocation is wired up.</summary>
    public static bool IsAvailable => InternalRequestHandler.IsAvailable;

    /// <summary>
    /// Invoke an endpoint by HTTP method + path (path may include a query string; templated paths
    /// like <c>/api/x/{id}</c> are matched by passing a concrete path). Returns the rendered response.
    /// </summary>
    public static async Task<RoutineInvokeResult> InvokeAsync(
        string method,
        string path,
        IDictionary<string, string>? headers = null,
        string? body = null,
        string? contentType = null,
        ClaimsPrincipal? user = null,
        CancellationToken cancellationToken = default)
    {
        var headerDict = headers as Dictionary<string, string>
            ?? (headers is null ? null : new Dictionary<string, string>(headers));

        var response = await InternalRequestHandler.ExecuteAsync(
            method, path, headerDict, body, contentType, cancellationToken, user);

        return new RoutineInvokeResult(response.StatusCode, response.Body, response.ContentType, response.IsSuccess);
    }
}
