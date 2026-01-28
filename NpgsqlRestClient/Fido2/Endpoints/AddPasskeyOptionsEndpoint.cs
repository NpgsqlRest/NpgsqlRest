using System.Net;
using System.Text.Json;
using Npgsql;
using NpgsqlRest;
using static NpgsqlRestClient.Fido2.PasskeyHelpers;

namespace NpgsqlRestClient.Fido2;

/// <summary>
/// Handles the add passkey options endpoint for authenticated users.
/// Returns WebAuthn PublicKeyCredentialCreationOptions for adding a passkey to an existing account.
/// Requires authentication - user must be logged in.
/// </summary>
/// <remarks>
/// <para><b>Request:</b> POST with no body (uses authenticated user's claims)</para>
/// <para>Requires: Authentication (JWT Bearer token or cookie)</para>
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
///   "excludeCredentials": [...],  // Optional: user's existing passkeys
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
///   <item>authentication_required - User is not authenticated</item>
///   <item>database_error - Database query failed</item>
///   <item>registration_failed - Database returned non-200 status</item>
/// </list>
/// </remarks>
public sealed class AddPasskeyOptionsEndpoint(PasskeyEndpointContext ctx)
{
    private const string LogAddPasskeyOptions = "PasskeyAuth.AddPasskeyOptions";

    public async Task InvokeAsync(HttpContext context)
    {
        var config = ctx.Config;
        var options = ctx.Options;

        if (context.User.Identity?.IsAuthenticated is not true)
        {
            await WriteErrorResponseAsync(context, HttpStatusCode.Unauthorized,
                PasskeyErrorCode.AuthenticationRequired, "Authentication required to add a passkey");
            return;
        }

        var claimsParam = context.User.BuildClaimsDictionary(options.AuthenticationOptions).GetUserClaimsDbParam();

        string? bodyParam = null;
        if (context.Request.ContentLength > 0)
        {
            using var bodyReader = new StreamReader(context.Request.Body);
            bodyParam = await bodyReader.ReadToEndAsync(context.RequestAborted);
        }

        await using var connection = await OpenConnectionAsync(ctx, context.RequestAborted);

        await ExecuteTransactionCommandAsync(connection, "BEGIN", context.RequestAborted);

        await using var command = connection.CreateCommand();
        command.CommandText = config.ChallengeAddExistingUserCommand;

        var paramCount = command.CommandText.PgCountParams();
        if (paramCount >= 1)
        {
            command.Parameters.Add(new NpgsqlParameter
            {
                Value = claimsParam,
                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Json
            });
        }
        if (paramCount >= 2)
        {
            command.Parameters.Add(new NpgsqlParameter
            {
                Value = bodyParam ?? (object)DBNull.Value,
                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Json
            });
        }

        CommandLogger.LogCommand(command, ctx.Logger, LogAddPasskeyOptions);

        int status;
        string? errorMessage = null;
        string? challenge = null;
        string? userHandle = null;
        string? userName = null;
        string? userDisplayName = null;
        string? challengeId = null;
        string? excludeCredentialsJson = null;
        string? userContextJson = null;

        await using (var reader = await command.ExecuteReaderWithRetryAsync(ctx.RetryStrategy, context.RequestAborted, ctx.Logger))
        {
            if (!await reader.ReadAsync(context.RequestAborted))
            {
                await reader.CloseAsync();
                await ExecuteTransactionCommandAsync(connection, "ROLLBACK", context.RequestAborted);
                await WriteErrorResponseAsync(context, HttpStatusCode.InternalServerError,
                    PasskeyErrorCode.DatabaseError, "Failed to get creation options from database");
                return;
            }

            status = reader.GetInt32(reader.GetOrdinal(config.StatusColumnName));
            if (status != 200)
            {
                errorMessage = reader.IsDBNull(reader.GetOrdinal(config.MessageColumnName))
                    ? "Failed to create registration options"
                    : reader.GetString(reader.GetOrdinal(config.MessageColumnName));
                await reader.CloseAsync();
                await ExecuteTransactionCommandAsync(connection, "ROLLBACK", context.RequestAborted);
                await WriteErrorResponseAsync(context, (HttpStatusCode)status, PasskeyErrorCode.StoreFailed, errorMessage);
                return;
            }

            challenge = reader.GetString(reader.GetOrdinal(config.ChallengeColumnName));
            userHandle = reader.GetString(reader.GetOrdinal(config.UserHandleColumnName));
            userName = reader.GetString(reader.GetOrdinal(config.UserNameColumnName));
            userDisplayName = reader.GetString(reader.GetOrdinal(config.UserDisplayNameColumnName));
            challengeId = reader.GetValue(reader.GetOrdinal(config.ChallengeIdColumnName))?.ToString();

            if (!reader.IsDBNull(reader.GetOrdinal(config.ExcludeCredentialsColumnName)))
            {
                excludeCredentialsJson = reader.GetString(reader.GetOrdinal(config.ExcludeCredentialsColumnName));
            }

            if (!reader.IsDBNull(reader.GetOrdinal(config.UserContextColumnName)))
            {
                userContextJson = reader.GetString(reader.GetOrdinal(config.UserContextColumnName));
            }
        }

        var rpId = config.RelyingPartyId ?? context.Request.Host.Host;
        var rpName = config.RelyingPartyName ?? rpId;

        await ExecuteTransactionCommandAsync(connection, "COMMIT", context.RequestAborted);

        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = "application/json";

        await using (var writer = new Utf8JsonWriter(context.Response.Body))
        {
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

            // Inject userHandle into userContext so it's passed back to the completion endpoint
            writer.WritePropertyName("userContext");
            if (!string.IsNullOrEmpty(userContextJson))
            {
                using var doc = JsonDocument.Parse(userContextJson);
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    prop.WriteTo(writer);
                }
                writer.WriteString("userHandle", userHandle);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteStartObject();
                writer.WriteString("userHandle", userHandle);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            await writer.FlushAsync(context.RequestAborted);
        }
    }
}
