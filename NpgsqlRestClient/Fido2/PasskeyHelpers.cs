using System.Net;
using System.Text.Json;
using Npgsql;
using NpgsqlRest;

namespace NpgsqlRestClient.Fido2;

internal static class PasskeyHelpers
{
    public static async Task WriteErrorResponseAsync(
        HttpContext context,
        HttpStatusCode statusCode,
        string error,
        string? errorDescription)
    {
        await Results.Problem(
            type: error,
            statusCode: (int)statusCode,
            title: errorDescription).ExecuteAsync(context);
    }

    public static async Task WriteSuccessResponseAsync(
        HttpContext context,
        string? credentialId)
    {
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = "application/json";

        await using var writer = new Utf8JsonWriter(context.Response.Body);
        writer.WriteStartObject();
        writer.WriteBoolean("success", true);
        if (credentialId != null)
        {
            writer.WriteString("credentialId", credentialId);
        }
        writer.WriteEndObject();
        await writer.FlushAsync(context.RequestAborted);
    }

    public static string GetOriginFromRequest(HttpRequest request)
    {
        var scheme = request.Scheme;
        var host = request.Host.Host;
        var port = request.Host.Port;

        // Standard ports don't need to be included
        if ((scheme == "https" && port == 443) ||
            (scheme == "http" && port == 80) ||
            port == null)
        {
            return $"{scheme}://{host}";
        }

        return $"{scheme}://{host}:{port}";
    }

    public static async Task<NpgsqlConnection> OpenConnectionAsync(
        PasskeyEndpointContext ctx,
        CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(ctx.ConnectionString);

        if (ctx.Options.LogConnectionNoticeEvents)
        {
            connection.Notice += (sender, args) =>
            {
                NpgsqlRestLogger.LogConnectionNotice(args.Notice, ctx.LoggingMode);
            };
        }

        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public static async Task ExecuteTransactionCommandAsync(
        NpgsqlConnection connection,
        string command,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = command;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<ReadOnlyMemory<byte>> ReadRequestBodyAsync(
        HttpContext context)
    {
        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms, context.RequestAborted);
        return ms.ToArray();
    }
}
