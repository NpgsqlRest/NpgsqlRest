using System.Buffers;
using System.Net;
using System.Text.Json;
using Npgsql;
using NpgsqlRest;
using static NpgsqlRestClient.Fido2.PasskeyHelpers;
using static NpgsqlRestClient.Fido2.RequestParsers;

namespace NpgsqlRestClient.Fido2;

/// <summary>
/// Handles the login options endpoint.
/// Returns WebAuthn PublicKeyCredentialRequestOptions for passkey authentication.
/// </summary>
/// <remarks>
/// <para><b>Request:</b> POST with optional JSON body</para>
/// <code>
/// {
///   "userName": "user@example.com"  // Optional: filter credentials by user
/// }
/// </code>
///
/// <para><b>Success Response (200):</b></para>
/// <code>
/// {
///   "challenge": "base64url-encoded-challenge",
///   "challengeId": "uuid-for-server-verification",
///   "rpId": "example.com",
///   "timeout": 300000,
///   "userVerification": "preferred",
///   "allowCredentials": [           // Optional: present if userName provided
///     {
///       "type": "public-key",
///       "id": "base64url-credential-id",
///       "transports": ["internal", "hybrid"]
///     }
///   ]
/// }
/// </code>
///
/// <para><b>Error Response:</b></para>
/// <code>
/// {
///   "error": "error_code",
///   "errorDescription": "Human readable message"
/// }
/// </code>
/// </remarks>
public sealed class LoginOptionsEndpoint(PasskeyEndpointContext ctx)
{
    private const string LogLoginOptions = "PasskeyAuth.LoginOptions";

    private const string ErrorDatabaseError = "database_error";
    private const string ErrorLoginFailed = "login_failed";

    public async Task InvokeAsync(HttpContext context)
    {
        var config = ctx.Config;

        string? userName = null;
        string? bodyJson = null;
        try
        {
            var buffer = await ReadRequestBodyAsync(context);
            if (buffer.Length > 0)
            {
                bodyJson = System.Text.Encoding.UTF8.GetString(buffer.Span);
                var request = ParseAuthenticationOptionsRequest(buffer.Span);
                userName = request.UserName;
            }
        }
        catch
        {
        }

        await using var connection = await OpenConnectionAsync(ctx, context.RequestAborted);
        await ExecuteTransactionCommandAsync(connection, "BEGIN", context.RequestAborted);

        await using var command = connection.CreateCommand();
        command.CommandText = config.ChallengeAuthenticationCommand;

        var paramCount = command.CommandText.PgCountParams();
        if (paramCount >= 1)
        {
            command.Parameters.AddWithValue(userName ?? (object)DBNull.Value);
        }
        if (paramCount >= 2)
        {
            command.Parameters.Add(new NpgsqlParameter
            {
                Value = bodyJson ?? (object)DBNull.Value,
                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Json
            });
        }

        CommandLogger.LogCommand(command, ctx.Logger, LogLoginOptions);

        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);

        if (!await reader.ReadAsync(context.RequestAborted))
        {
            await ExecuteTransactionCommandAsync(connection, "ROLLBACK", context.RequestAborted);
            await WriteErrorResponseAsync(context, HttpStatusCode.InternalServerError,
                ErrorDatabaseError, "Failed to get login options from database");
            return;
        }

        var status = reader.GetInt32(reader.GetOrdinal(config.StatusColumnName));
        if (status != 200)
        {
            await ExecuteTransactionCommandAsync(connection, "ROLLBACK", context.RequestAborted);
            await WriteErrorResponseAsync(context, (HttpStatusCode)status,
                ErrorLoginFailed, "Failed to create login options");
            return;
        }

        var challenge = reader.GetString(reader.GetOrdinal(config.ChallengeColumnName));
        var challengeId = reader.GetValue(reader.GetOrdinal(config.ChallengeIdColumnName))?.ToString();

        string? allowCredentialsJson = null;
        if (!reader.IsDBNull(reader.GetOrdinal(config.AllowCredentialsColumnName)))
        {
            allowCredentialsJson = reader.GetString(reader.GetOrdinal(config.AllowCredentialsColumnName));
        }

        var rpId = config.RelyingPartyId ?? context.Request.Host.Host;

        await ExecuteTransactionCommandAsync(connection, "COMMIT", context.RequestAborted);

        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = "application/json";

        await using var writer = new Utf8JsonWriter(context.Response.Body);
        writer.WriteStartObject();

        writer.WriteString("challenge", challenge);
        writer.WriteString("challengeId", challengeId);
        writer.WriteString("rpId", rpId);
        writer.WriteNumber("timeout", config.ChallengeTimeoutMinutes * 60 * 1000);
        writer.WriteString("userVerification", config.UserVerificationRequirement);

        if (!string.IsNullOrEmpty(allowCredentialsJson))
        {
            writer.WritePropertyName("allowCredentials");
            writer.WriteRawValue(allowCredentialsJson);
        }

        writer.WriteEndObject();
        await writer.FlushAsync(context.RequestAborted);
    }
}
