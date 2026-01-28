using System.Net;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlRest;
using static NpgsqlRestClient.Fido2.PasskeyHelpers;
using static NpgsqlRestClient.Fido2.RequestParsers;

namespace NpgsqlRestClient.Fido2;

/// <summary>
/// Handles the standalone registration completion endpoint for new users.
/// Validates the attestation and creates a new user with the credential.
/// Does not require authentication - this is for new user registration.
/// </summary>
/// <remarks>
/// <para><b>Request:</b> POST with JSON body</para>
/// <code>
/// {
///   "challengeId": "uuid-from-options-response",    // Required
///   "credentialId": "base64url-credential-id",      // Required
///   "attestationObject": "base64url-attestation",   // Required
///   "clientDataJSON": "base64url-client-data",      // Required
///   "transports": ["internal", "hybrid"],           // Optional: authenticator transports
///   "userContext": {...},                           // Required: opaque JSON from options endpoint (no user id)
///   "analyticsData": {...}                          // Optional: client-side analytics
/// }
/// </code>
///
/// <para><b>Success Response (200):</b></para>
/// <code>
/// {
///   "success": true,
///   "credentialId": "base64url-credential-id"
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
///   <item>invalid_request - Missing required fields or invalid JSON</item>
///   <item>challenge_invalid - Challenge not found or expired</item>
///   <item>attestation_invalid - WebAuthn attestation validation failed</item>
///   <item>store_failed - Database rejected the credential (e.g., username exists)</item>
/// </list>
/// </remarks>
public sealed class RegistrationEndpoint(PasskeyEndpointContext ctx)
{
    private const string ChallengeType = "registration";
    private const string LogChallengeVerify = "PasskeyAuth.ChallengeVerify";
    private const string LogCredentialStore = "PasskeyAuth.CredentialStore";

    public async Task InvokeAsync(HttpContext context)
    {
        var config = ctx.Config;

        RegistrationRequest request;
        try
        {
            var buffer = await ReadRequestBodyAsync(context);
            request = ParseRegistrationRequest(buffer.Span);
        }
        catch
        {
            await WriteErrorResponseAsync(context, HttpStatusCode.BadRequest,
                PasskeyErrorCode.InvalidRequest, "Invalid request body");
            return;
        }

        if (string.IsNullOrEmpty(request.ChallengeId) ||
            string.IsNullOrEmpty(request.CredentialId) ||
            string.IsNullOrEmpty(request.AttestationObject) ||
            string.IsNullOrEmpty(request.ClientDataJSON) ||
            string.IsNullOrEmpty(request.UserContext))
        {
            await WriteErrorResponseAsync(context, HttpStatusCode.BadRequest,
                PasskeyErrorCode.InvalidRequest, "Missing required fields");
            return;
        }

        await using var connection = await OpenConnectionAsync(ctx, context.RequestAborted);

        await ExecuteTransactionCommandAsync(connection, "BEGIN", context.RequestAborted);

        await using var verifyCommand = connection.CreateCommand();
        verifyCommand.CommandText = config.VerifyChallengeCommand;
        if (long.TryParse(request.ChallengeId, out var challengeIdLong))
        {
            verifyCommand.Parameters.AddWithValue(challengeIdLong);
        }
        else if (Guid.TryParse(request.ChallengeId, out var challengeIdGuid))
        {
            verifyCommand.Parameters.AddWithValue(challengeIdGuid);
        }
        else
        {
            verifyCommand.Parameters.AddWithValue(request.ChallengeId);
        }
        verifyCommand.Parameters.AddWithValue(ChallengeType);

        CommandLogger.LogCommand(verifyCommand, ctx.Logger, LogChallengeVerify);

        var challengeResult = await verifyCommand.ExecuteScalarAsync(context.RequestAborted);
        if (challengeResult == null || challengeResult == DBNull.Value)
        {
            await ExecuteTransactionCommandAsync(connection, "ROLLBACK", context.RequestAborted);
            await WriteErrorResponseAsync(context, HttpStatusCode.BadRequest,
                PasskeyErrorCode.ChallengeInvalid, ValidationError.ChallengeNotFound);
            return;
        }

        var expectedChallenge = (byte[])challengeResult;

        var attestationObject = AttestationValidator.Base64UrlDecode(request.AttestationObject);
        var clientDataJson = AttestationValidator.Base64UrlDecode(request.ClientDataJSON);

        if (attestationObject == null || clientDataJson == null)
        {
            await ExecuteTransactionCommandAsync(connection, "ROLLBACK", context.RequestAborted);
            await WriteErrorResponseAsync(context, HttpStatusCode.BadRequest,
                PasskeyErrorCode.InvalidRequest, "Invalid base64url encoding");
            return;
        }

        var rpId = config.RelyingPartyId ?? context.Request.Host.Host;
        var origins = config.RelyingPartyOrigins.Length > 0
            ? config.RelyingPartyOrigins
            : [GetOriginFromRequest(context.Request)];

        var requireUv = config.UserVerificationRequirement == "required";

        var result = AttestationValidator.Validate(
            attestationObject,
            clientDataJson,
            expectedChallenge,
            origins,
            rpId,
            requireUv);

        if (!result.IsValid)
        {
            ctx.Logger?.LogWarning("Attestation validation failed: {Error}", result.Error);
            await ExecuteTransactionCommandAsync(connection, "ROLLBACK", context.RequestAborted);
            await WriteErrorResponseAsync(context, HttpStatusCode.BadRequest,
                PasskeyErrorCode.AttestationInvalid, result.Error ?? "Attestation validation failed");
            return;
        }

        // Extract userHandle from userContext (injected by RegistrationOptionsEndpoint)
        byte[] userHandle = [];
        try
        {
            var userContext = JsonNode.Parse(request.UserContext);
            var userHandleBase64 = userContext?["userHandle"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(userHandleBase64))
            {
                userHandle = AttestationValidator.Base64UrlDecode(userHandleBase64)
                    ?? Convert.FromBase64String(userHandleBase64);
            }
        }
        catch
        {
            // userHandle will remain empty if parsing fails
        }

        await using var storeCommand = connection.CreateCommand();
        storeCommand.CommandText = config.CompleteRegistrationCommand;

        var paramCount = storeCommand.CommandText.PgCountParams();
        if (paramCount >= 1)
        {
            storeCommand.Parameters.AddWithValue(result.CredentialId!);
        }
        if (paramCount >= 2)
        {
            storeCommand.Parameters.AddWithValue(userHandle);
        }
        if (paramCount >= 3)
        {
            storeCommand.Parameters.AddWithValue(result.PublicKey!);
        }
        if (paramCount >= 4)
        {
            storeCommand.Parameters.AddWithValue(result.Algorithm);
        }
        if (paramCount >= 5)
        {
            storeCommand.Parameters.AddWithValue(request.Transports ?? Array.Empty<string>());
        }
        if (paramCount >= 6)
        {
            storeCommand.Parameters.AddWithValue(result.BackupEligible);
        }
        if (paramCount >= 7)
        {
            storeCommand.Parameters.Add(new NpgsqlParameter
            {
                Value = request.UserContext,
                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Json
            });
        }

        if (paramCount >= 8)
        {
            if (!string.IsNullOrEmpty(request.AnalyticsData))
            {
                try
                {
                    var analyticsData = JsonNode.Parse(request.AnalyticsData);
                    if (analyticsData != null && !string.IsNullOrEmpty(config.ClientAnalyticsIpKey))
                    {
                        analyticsData[config.ClientAnalyticsIpKey] = context.Request.GetClientIpAddress();
                    }
                    storeCommand.Parameters.Add(new NpgsqlParameter
                    {
                        Value = analyticsData?.ToJsonString() ?? (object)DBNull.Value,
                        NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Json
                    });
                }
                catch
                {
                    storeCommand.Parameters.Add(new NpgsqlParameter
                    {
                        Value = DBNull.Value,
                        NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Json
                    });
                }
            }
            else
            {
                storeCommand.Parameters.Add(new NpgsqlParameter
                {
                    Value = DBNull.Value,
                    NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Json
                });
            }
        }

        CommandLogger.LogCommand(storeCommand, ctx.Logger, LogCredentialStore);

        int storeStatus = 200;
        string storeMessage = "Passkey registered successfully";

        await using (var storeReader = await storeCommand.ExecuteReaderAsync(context.RequestAborted))
        {
            if (await storeReader.ReadAsync(context.RequestAborted))
            {
                storeStatus = storeReader.GetInt32(storeReader.GetOrdinal(config.StatusColumnName));
                if (!storeReader.IsDBNull(storeReader.GetOrdinal(config.MessageColumnName)))
                {
                    storeMessage = storeReader.GetString(storeReader.GetOrdinal(config.MessageColumnName));
                }
            }
        }

        if (storeStatus != 200)
        {
            await ExecuteTransactionCommandAsync(connection, "ROLLBACK", context.RequestAborted);
            await WriteErrorResponseAsync(context, (HttpStatusCode)storeStatus,
                PasskeyErrorCode.StoreFailed, storeMessage);
            return;
        }

        await ExecuteTransactionCommandAsync(connection, "COMMIT", context.RequestAborted);

        await WriteSuccessResponseAsync(context, AttestationValidator.Base64UrlEncode(result.CredentialId!));
    }
}
