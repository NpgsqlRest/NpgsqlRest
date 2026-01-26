using System.Net;
using System.Text.Json;
using Npgsql;
using NpgsqlRest;
using static NpgsqlRestClient.Fido2.PasskeyHelpers;

namespace NpgsqlRestClient.Fido2;

/// <summary>
/// Handles the registration options endpoint for new users.
/// Returns WebAuthn PublicKeyCredentialCreationOptions for registering a new user with a passkey.
/// Does not require authentication - this is for new user registration.
/// </summary>
/// <remarks>
/// <para><b>Request:</b> POST with JSON body</para>
/// <code>
/// {
///   "userName": "user@example.com",      // Required: username/email for the new account
///   "displayName": "John Doe",           // Optional: user's display name
///   ...                                  // Any additional fields passed to DB function
/// }
/// </code>
///
/// <para><b>Success Response (200):</b></para>
/// <code>
/// {
///   "challenge": "base64url-encoded-challenge",
///   "challengeId": "uuid-for-server-verification",
///   "rp": { "id": "example.com", "name": "My App" },
///   "user": {
///     "id": "base64url-user-handle",
///     "name": "user@example.com",
///     "displayName": "John Doe"
///   },
///   "pubKeyCredParams": [
///     { "type": "public-key", "alg": -7 },
///     { "type": "public-key", "alg": -257 }
///   ],
///   "timeout": 300000,
///   "attestation": "none",
///   "authenticatorSelection": {
///     "residentKey": "preferred",
///     "userVerification": "preferred"
///   },
///   "excludeCredentials": [...],  // Optional: if user already has passkeys
///   "userContext": {...}          // Opaque JSON to pass back to register endpoint
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
///
/// <para><b>Error codes:</b></para>
/// <list type="bullet">
///   <item>invalid_request - Missing or invalid request body</item>
///   <item>database_error - Database query failed</item>
///   <item>registration_failed - Database returned non-200 status (e.g., user exists)</item>
/// </list>
/// </remarks>
public sealed class RegistrationOptionsEndpoint(PasskeyEndpointContext ctx)
{
    private const string LogRegistrationOptions = "PasskeyAuth.RegistrationOptions";

    private const string ErrorInvalidRequest = "invalid_request";
    private const string ErrorDatabaseError = "database_error";
    private const string ErrorRegistrationFailed = "registration_failed";

    public async Task InvokeAsync(HttpContext context)
    {
        var config = ctx.Config;

        string jsonParam;
        try
        {
            using var bodyReader = new StreamReader(context.Request.Body);
            var body = await bodyReader.ReadToEndAsync(context.RequestAborted);

            if (string.IsNullOrEmpty(body))
            {
                await WriteErrorResponseAsync(context, HttpStatusCode.BadRequest,
                    ErrorInvalidRequest, "Request body is required for standalone registration");
                return;
            }

            try
            {
                JsonDocument.Parse(body);
            }
            catch
            {
                await WriteErrorResponseAsync(context, HttpStatusCode.BadRequest,
                    ErrorInvalidRequest, "Invalid JSON in request body");
                return;
            }

            jsonParam = body;
        }
        catch
        {
            await WriteErrorResponseAsync(context, HttpStatusCode.BadRequest,
                ErrorInvalidRequest, "Failed to read request body");
            return;
        }

        await using var connection = await OpenConnectionAsync(ctx, context.RequestAborted);

        await ExecuteTransactionCommandAsync(connection, "BEGIN", context.RequestAborted);

        await using var command = connection.CreateCommand();
        command.CommandText = config.ChallengeRegistrationCommand;
        command.Parameters.Add(new NpgsqlParameter
        {
            Value = jsonParam,
            NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Json
        });

        CommandLogger.LogCommand(command, ctx.Logger, LogRegistrationOptions);

        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);

        if (!await reader.ReadAsync(context.RequestAborted))
        {
            await ExecuteTransactionCommandAsync(connection, "ROLLBACK", context.RequestAborted);
            await WriteErrorResponseAsync(context, HttpStatusCode.InternalServerError,
                ErrorDatabaseError, "Failed to get creation options from database");
            return;
        }

        var status = reader.GetInt32(reader.GetOrdinal(config.StatusColumnName));
        if (status != 200)
        {
            var errorMessage = reader.IsDBNull(reader.GetOrdinal(config.MessageColumnName))
                ? "Failed to create registration options"
                : reader.GetString(reader.GetOrdinal(config.MessageColumnName));
            await ExecuteTransactionCommandAsync(connection, "ROLLBACK", context.RequestAborted);
            await WriteErrorResponseAsync(context, (HttpStatusCode)status, ErrorRegistrationFailed, errorMessage);
            return;
        }

        var challenge = reader.GetString(reader.GetOrdinal(config.ChallengeColumnName));
        var userHandle = reader.GetString(reader.GetOrdinal(config.UserHandleColumnName));
        var userName = reader.GetString(reader.GetOrdinal(config.UserNameColumnName));
        var userDisplayName = reader.GetString(reader.GetOrdinal(config.UserDisplayNameColumnName));
        var challengeId = reader.GetValue(reader.GetOrdinal(config.ChallengeIdColumnName))?.ToString();

        string? excludeCredentialsJson = null;
        if (!reader.IsDBNull(reader.GetOrdinal(config.ExcludeCredentialsColumnName)))
        {
            excludeCredentialsJson = reader.GetString(reader.GetOrdinal(config.ExcludeCredentialsColumnName));
        }

        string? userContextJson = null;
        if (!reader.IsDBNull(reader.GetOrdinal(config.UserContextColumnName)))
        {
            userContextJson = reader.GetString(reader.GetOrdinal(config.UserContextColumnName));
        }

        var rpId = config.RelyingPartyId ?? context.Request.Host.Host;
        var rpName = config.RelyingPartyName ?? rpId;

        await ExecuteTransactionCommandAsync(connection, "COMMIT", context.RequestAborted);

        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = "application/json";

        await using var writer = new Utf8JsonWriter(context.Response.Body);
        writer.WriteStartObject();

        writer.WriteString("challenge", challenge);
        writer.WriteString("challengeId", challengeId);

        writer.WriteStartObject("rp");
        writer.WriteString("id", rpId);
        writer.WriteString("name", rpName);
        writer.WriteEndObject();

        writer.WriteStartObject("user");
        writer.WriteString("id", userHandle);
        writer.WriteString("name", userName);
        writer.WriteString("displayName", userDisplayName);
        writer.WriteEndObject();

        writer.WriteStartArray("pubKeyCredParams");
        writer.WriteStartObject();
        writer.WriteString("type", "public-key");
        writer.WriteNumber("alg", CoseAlgorithm.ES256);
        writer.WriteEndObject();
        writer.WriteStartObject();
        writer.WriteString("type", "public-key");
        writer.WriteNumber("alg", CoseAlgorithm.RS256);
        writer.WriteEndObject();
        writer.WriteEndArray();

        writer.WriteNumber("timeout", config.ChallengeTimeoutMinutes * 60 * 1000);
        writer.WriteString("attestation", config.AttestationConveyance);

        writer.WriteStartObject("authenticatorSelection");
        writer.WriteString("residentKey", config.ResidentKeyRequirement);
        writer.WriteString("userVerification", config.UserVerificationRequirement);
        writer.WriteEndObject();

        if (!string.IsNullOrEmpty(excludeCredentialsJson))
        {
            writer.WritePropertyName("excludeCredentials");
            writer.WriteRawValue(excludeCredentialsJson);
        }

        if (!string.IsNullOrEmpty(userContextJson))
        {
            writer.WritePropertyName("userContext");
            writer.WriteRawValue(userContextJson);
        }

        writer.WriteEndObject();
        await writer.FlushAsync(context.RequestAborted);
    }
}
