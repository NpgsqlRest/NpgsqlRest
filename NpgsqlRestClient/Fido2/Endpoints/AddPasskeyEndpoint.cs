using System.Net;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlRest;
using static NpgsqlRestClient.Fido2.PasskeyHelpers;
using static NpgsqlRestClient.Fido2.RequestParsers;

namespace NpgsqlRestClient.Fido2;

/// <summary>
/// Handles the add passkey completion endpoint for authenticated users.
/// Validates the attestation and adds the credential to an existing user account.
/// Requires authentication - user must be logged in.
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
///   "userContext": {...},                           // Required: opaque JSON from options endpoint (contains user id)
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
///   <item>authentication_required - User is not authenticated</item>
///   <item>invalid_request - Missing required fields or invalid JSON</item>
///   <item>challenge_invalid - Challenge not found or expired</item>
///   <item>attestation_invalid - WebAuthn attestation validation failed</item>
///   <item>store_failed - Database rejected the credential</item>
/// </list>
/// </remarks>
public sealed class AddPasskeyEndpoint(PasskeyEndpointContext ctx)
{
    private const string ChallengeType = "registration";
    private const string LogChallengeVerify = "PasskeyAuth.ChallengeVerify";
    private const string LogCredentialStore = "PasskeyAuth.CredentialStore";

    private const string ErrorAuthenticationRequired = "authentication_required";
    private const string ErrorInvalidRequest = "invalid_request";
    private const string ErrorChallengeInvalid = "challenge_invalid";
    private const string ErrorAttestationInvalid = "attestation_invalid";
    private const string ErrorStoreFailed = "store_failed";

    public async Task InvokeAsync(HttpContext context)
    {
        var config = ctx.Config;

        if (context.User.Identity?.IsAuthenticated is not true)
        {
            await WriteErrorResponseAsync(context, HttpStatusCode.Unauthorized,
                ErrorAuthenticationRequired, "Authentication required to add a passkey");
            return;
        }

        RegistrationRequest request;
        try
        {
            var buffer = await ReadRequestBodyAsync(context);
            request = ParseRegistrationRequest(buffer.Span);
        }
        catch
        {
            await WriteErrorResponseAsync(context, HttpStatusCode.BadRequest,
                ErrorInvalidRequest, "Invalid request body");
            return;
        }

        if (string.IsNullOrEmpty(request.ChallengeId) ||
            string.IsNullOrEmpty(request.CredentialId) ||
            string.IsNullOrEmpty(request.AttestationObject) ||
            string.IsNullOrEmpty(request.ClientDataJSON) ||
            string.IsNullOrEmpty(request.UserContext))
        {
            await WriteErrorResponseAsync(context, HttpStatusCode.BadRequest,
                ErrorInvalidRequest, "Missing required fields");
            return;
        }

        await using var connection = await OpenConnectionAsync(ctx, context.RequestAborted);

        await ExecuteTransactionCommandAsync(connection, "BEGIN", context.RequestAborted);

        await using var verifyCommand = connection.CreateCommand();
        verifyCommand.CommandText = config.ChallengeVerifyCommand;
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
                ErrorChallengeInvalid, ValidationError.ChallengeNotFound);
            return;
        }

        var expectedChallenge = (byte[])challengeResult;

        var attestationObject = AttestationValidator.Base64UrlDecode(request.AttestationObject);
        var clientDataJson = AttestationValidator.Base64UrlDecode(request.ClientDataJSON);

        if (attestationObject == null || clientDataJson == null)
        {
            await ExecuteTransactionCommandAsync(connection, "ROLLBACK", context.RequestAborted);
            await WriteErrorResponseAsync(context, HttpStatusCode.BadRequest,
                ErrorInvalidRequest, "Invalid base64url encoding");
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
                ErrorAttestationInvalid, result.Error ?? "Attestation validation failed");
            return;
        }

        await using var storeCommand = connection.CreateCommand();
        storeCommand.CommandText = config.AddExistingUserCompleteCommand;

        var paramCount = storeCommand.CommandText.PgCountParams();
        if (paramCount >= 1)
        {
            storeCommand.Parameters.AddWithValue(result.CredentialId!);
        }
        if (paramCount >= 2)
        {
            storeCommand.Parameters.AddWithValue(result.UserHandle ?? []);
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

        await using var storeReader = await storeCommand.ExecuteReaderAsync(context.RequestAborted);

        int storeStatus = 200;
        string storeMessage = "Passkey added successfully";

        if (await storeReader.ReadAsync(context.RequestAborted))
        {
            storeStatus = storeReader.GetInt32(storeReader.GetOrdinal(config.StatusColumnName));
            if (!storeReader.IsDBNull(storeReader.GetOrdinal(config.MessageColumnName)))
            {
                storeMessage = storeReader.GetString(storeReader.GetOrdinal(config.MessageColumnName));
            }
        }

        if (storeStatus != 200)
        {
            await ExecuteTransactionCommandAsync(connection, "ROLLBACK", context.RequestAborted);
            await WriteErrorResponseAsync(context, (HttpStatusCode)storeStatus,
                ErrorStoreFailed, storeMessage);
            return;
        }

        await ExecuteTransactionCommandAsync(connection, "COMMIT", context.RequestAborted);

        await WriteSuccessResponseAsync(context, AttestationValidator.Base64UrlEncode(result.CredentialId!));
    }
}
