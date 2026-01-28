using System.Net;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlRest;
using NpgsqlRest.Auth;
using static NpgsqlRestClient.Fido2.PasskeyHelpers;
using static NpgsqlRestClient.Fido2.RequestParsers;

namespace NpgsqlRestClient.Fido2;

/// <summary>
/// Handles the login completion endpoint.
/// Validates the assertion and returns JWT tokens or signs in the user.
/// </summary>
/// <remarks>
/// <para><b>Request:</b> POST with JSON body</para>
/// <code>
/// {
///   "challengeId": "uuid-from-options-response",    // Required
///   "credentialId": "base64url-credential-id",      // Required
///   "authenticatorData": "base64url-auth-data",     // Required
///   "clientDataJSON": "base64url-client-data",      // Required
///   "signature": "base64url-signature",             // Required
///   "userHandle": "base64url-user-handle",          // Optional: returned for discoverable credentials
///   "analyticsData": {                              // Optional: client-side analytics
///     "screenWidth": 1920,
///     "screenHeight": 1080,
///     "userAgent": "Mozilla/5.0..."
///   }
/// }
/// </code>
///
/// <para><b>Success Response (200):</b> Delegated to LoginHandler - returns JWT tokens or cookie</para>
/// <code>
/// {
///   "accessToken": "jwt-access-token",
///   "refreshToken": "jwt-refresh-token",
///   "expiresIn": 3600
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
///   <item>invalid_request - Missing required fields or invalid JSON/base64url</item>
///   <item>database_error - Failed to retrieve login data</item>
///   <item>login_failed - Challenge or credential lookup failed</item>
///   <item>assertion_invalid - WebAuthn signature validation failed</item>
/// </list>
/// </remarks>
public sealed class LoginEndpoint(PasskeyEndpointContext ctx)
{
    private const string ChallengeType = "authentication";
    private const string LogChallengeVerify = "PasskeyAuth.ChallengeVerify";
    private const string LogLoginData = "PasskeyAuth.LoginData";
    private const string LogLoginComplete = "PasskeyAuth.LoginComplete";
    private const string TracePath = "PasskeyAuth.HandleLoginAsync";

    public async Task InvokeAsync(HttpContext context)
    {
        var config = ctx.Config;

        AuthenticationRequest request;
        try
        {
            var buffer = await ReadRequestBodyAsync(context);
            request = ParseAuthenticationRequest(buffer.Span);
        }
        catch
        {
            await WriteErrorResponseAsync(context, HttpStatusCode.BadRequest,
                PasskeyErrorCode.InvalidRequest, "Invalid request body");
            return;
        }

        if (string.IsNullOrEmpty(request.ChallengeId) ||
            string.IsNullOrEmpty(request.CredentialId) ||
            string.IsNullOrEmpty(request.AuthenticatorData) ||
            string.IsNullOrEmpty(request.ClientDataJSON) ||
            string.IsNullOrEmpty(request.Signature))
        {
            await WriteErrorResponseAsync(context, HttpStatusCode.BadRequest,
                PasskeyErrorCode.InvalidRequest, "Missing required fields");
            return;
        }

        var credentialId = AttestationValidator.Base64UrlDecode(request.CredentialId);
        var authenticatorData = AttestationValidator.Base64UrlDecode(request.AuthenticatorData);
        var clientDataJson = AttestationValidator.Base64UrlDecode(request.ClientDataJSON);
        var signature = AttestationValidator.Base64UrlDecode(request.Signature);
        
        if (credentialId == null || authenticatorData == null || clientDataJson == null || signature == null)
        {
            await WriteErrorResponseAsync(context, HttpStatusCode.BadRequest,
                PasskeyErrorCode.InvalidRequest, "Invalid base64url encoding");
            return;
        }

        await using var connection = await OpenConnectionAsync(ctx, context.RequestAborted);
        await ExecuteTransactionCommandAsync(connection, "BEGIN", context.RequestAborted);

        var rpId = config.RelyingPartyId ?? context.Request.Host.Host;
        var origins = config.RelyingPartyOrigins.Length > 0
            ? config.RelyingPartyOrigins
            : [GetOriginFromRequest(context.Request)];

        var requireUv = config.UserVerificationRequirement == "required";

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

        var challengeResult = await verifyCommand.ExecuteScalarWithRetryAsync(ctx.RetryStrategy, context.RequestAborted, ctx.Logger);
        if (challengeResult == null || challengeResult == DBNull.Value)
        {
            await ExecuteTransactionCommandAsync(connection, "ROLLBACK", context.RequestAborted);
            await WriteErrorResponseAsync(context, HttpStatusCode.BadRequest,
                PasskeyErrorCode.ChallengeInvalid, ValidationError.ChallengeNotFound);
            return;
        }

        var expectedChallenge = (byte[])challengeResult;

        await using var dataCommand = connection.CreateCommand();
        dataCommand.CommandText = config.AuthenticateDataCommand;
        dataCommand.Parameters.AddWithValue(credentialId);

        CommandLogger.LogCommand(dataCommand, ctx.Logger, LogLoginData);

        int status;
        string? errorMessage = null;
        byte[]? publicKey = null;
        int algorithm = 0;
        long storedSignCount = 0;
        string? userContext = null;

        await using (var dataReader = await dataCommand.ExecuteReaderWithRetryAsync(ctx.RetryStrategy, context.RequestAborted, ctx.Logger))
        {
            if (!await dataReader.ReadAsync(context.RequestAborted))
            {
                await dataReader.CloseAsync();
                await ExecuteTransactionCommandAsync(connection, "ROLLBACK", context.RequestAborted);
                await WriteErrorResponseAsync(context, HttpStatusCode.InternalServerError,
                    PasskeyErrorCode.DatabaseError, "Failed to get login data from database");
                return;
            }

            status = dataReader.GetInt32(dataReader.GetOrdinal(config.StatusColumnName));
            if (status != 200)
            {
                errorMessage = dataReader.IsDBNull(dataReader.GetOrdinal(config.MessageColumnName))
                    ? "Login failed"
                    : dataReader.GetString(dataReader.GetOrdinal(config.MessageColumnName));
                await dataReader.CloseAsync();
                await ExecuteTransactionCommandAsync(connection, "ROLLBACK", context.RequestAborted);
                await WriteErrorResponseAsync(context, (HttpStatusCode)status, PasskeyErrorCode.AuthenticationFailed, "Authentication failed");
                return;
            }

            publicKey = (byte[])dataReader[config.PublicKeyColumnName];
            algorithm = dataReader.GetInt32(dataReader.GetOrdinal(config.PublicKeyAlgorithmColumnName));
            storedSignCount = config.ValidateSignCount
                ? dataReader.GetInt64(dataReader.GetOrdinal(config.SignCountColumnName))
                : 0L;

            if (!dataReader.IsDBNull(dataReader.GetOrdinal(config.UserContextColumnName)))
            {
                userContext = dataReader.GetString(dataReader.GetOrdinal(config.UserContextColumnName));
            }
        }

        var result = AssertionValidator.Validate(
            authenticatorData,
            clientDataJson,
            signature,
            publicKey,
            algorithm,
            storedSignCount,
            expectedChallenge,
            origins,
            rpId,
            requireUv,
            config.ValidateSignCount);

        if (!result.IsValid)
        {
            ctx.Logger?.LogWarning("Assertion validation failed: {Error}", result.Error);
            await ExecuteTransactionCommandAsync(connection, "ROLLBACK", context.RequestAborted);
            await WriteErrorResponseAsync(context, HttpStatusCode.Unauthorized,
                PasskeyErrorCode.AssertionInvalid, result.Error ?? "Assertion validation failed");
            return;
        }

        await using var completeCommand = connection.CreateCommand();
        completeCommand.CommandText = config.CompleteAuthenticateCommand;

        var paramCount = completeCommand.CommandText.PgCountParams();
        if (paramCount >= 1)
        {
            completeCommand.Parameters.AddWithValue(credentialId);
        }
        if (paramCount >= 2)
        {
            completeCommand.Parameters.AddWithValue(config.ValidateSignCount ? result.NewSignCount : 0L);
        }
        if (paramCount >= 3)
        {
            completeCommand.Parameters.Add(new NpgsqlParameter
            {
                Value = userContext ?? (object)DBNull.Value,
                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Json
            });
        }
        if (paramCount >= 4)
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
                    completeCommand.Parameters.Add(new NpgsqlParameter
                    {
                        Value = analyticsData?.ToJsonString() ?? (object)DBNull.Value,
                        NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Json
                    });
                }
                catch
                {
                    completeCommand.Parameters.Add(new NpgsqlParameter
                    {
                        Value = DBNull.Value,
                        NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Json
                    });
                }
            }
            else
            {
                completeCommand.Parameters.Add(new NpgsqlParameter
                {
                    Value = DBNull.Value,
                    NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Json
                });
            }
        }

        CommandLogger.LogCommand(completeCommand, ctx.Logger, LogLoginComplete);

        await ExecuteTransactionCommandAsync(connection, "COMMIT", context.RequestAborted);

        await LoginHandler.HandleAsync(
            completeCommand,
            context,
            ctx.RetryStrategy,
            tracePath: TracePath,
            performHashVerification: false,
            assignUserPrincipalToContext: false);
    }
}
