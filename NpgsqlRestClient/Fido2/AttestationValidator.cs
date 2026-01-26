using System.Security.Cryptography;
using System.Text;
using static NpgsqlRestClient.Fido2.RequestParsers;

namespace NpgsqlRestClient.Fido2;

public static class AttestationValidator
{
    public static AttestationResult Validate(
        byte[] attestationObject,
        byte[] clientDataJson,
        byte[] expectedChallenge,
        string[] expectedOrigins,
        string expectedRpId,
        bool requireUserVerification = false)
    {
        // Step 1: Parse and validate clientDataJSON
        ClientDataParsed clientData;
        try
        {
            clientData = ParseClientData(clientDataJson);
        }
        catch
        {
            return AttestationResult.Fail(ValidationError.InvalidClientData);
        }

        if (clientData.Type == null || clientData.Challenge == null || clientData.Origin == null)
            return AttestationResult.Fail(ValidationError.InvalidClientData);

        // Step 2: Verify type is "webauthn.create"
        if (clientData.Type != "webauthn.create")
            return AttestationResult.Fail($"{ValidationError.InvalidType}: expected 'webauthn.create', got '{clientData.Type}'");

        // Step 3: Verify challenge matches
        var challengeBytes = Base64UrlDecode(clientData.Challenge);
        if (challengeBytes == null || !CryptographicOperations.FixedTimeEquals(challengeBytes, expectedChallenge))
            return AttestationResult.Fail(ValidationError.ChallengeMismatch);

        // Step 4: Verify origin
        if (!IsOriginAllowed(clientData.Origin, expectedOrigins))
            return AttestationResult.Fail($"{ValidationError.OriginMismatch}: {clientData.Origin}");

        // Step 5: Parse attestation object
        var attestation = CborDecoder.DecodeAttestationObject(attestationObject);
        if (attestation == null)
            return AttestationResult.Fail(ValidationError.InvalidAttestationObject);

        // Step 6: Parse authenticator data
        var authData = AuthenticatorData.Parse(attestation.AuthData);
        if (authData == null)
            return AttestationResult.Fail(ValidationError.InvalidAuthenticatorData);

        // Step 7: Verify RP ID hash
        var expectedRpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedRpId));
        if (!CryptographicOperations.FixedTimeEquals(authData.RpIdHash, expectedRpIdHash))
            return AttestationResult.Fail(ValidationError.RpIdHashMismatch);

        // Step 8: Verify user present flag
        if (!authData.UserPresent)
            return AttestationResult.Fail(ValidationError.UserNotPresent);

        // Step 9: Verify user verification if required
        if (requireUserVerification && !authData.UserVerified)
            return AttestationResult.Fail(ValidationError.UserVerificationRequired);

        // Step 10: Verify attested credential data is present
        if (authData.AttestedCredentialData == null)
            return AttestationResult.Fail(ValidationError.MissingAttestedCredentialData);

        var credData = authData.AttestedCredentialData;

        // Step 11: Verify the algorithm is supported
        if (!CredentialPublicKey.IsSupportedAlgorithm(credData.Algorithm))
            return AttestationResult.Fail($"{ValidationError.UnsupportedAlgorithm}: {credData.Algorithm}");

        // Attestation validation is successful
        // Note: We're using "none" attestation format by default, so we don't verify the attestation statement.
        // For higher security requirements, you could verify packed, tpm, android-key, or other attestation formats.

        return AttestationResult.Success(
            credentialId: credData.CredentialId,
            publicKey: credData.PublicKeyBytes,
            algorithm: credData.Algorithm,
            signCount: authData.SignCount,
            backupEligible: authData.BackupEligible,
            backedUp: authData.BackedUp,
            aaguid: credData.Aaguid);
    }

    private static bool IsOriginAllowed(string origin, string[] allowedOrigins)
    {
        if (allowedOrigins == null || allowedOrigins.Length == 0)
        {
            // If no origins specified, accept any (not recommended for production)
            return true;
        }

        foreach (var allowed in allowedOrigins)
        {
            if (string.Equals(origin, allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static byte[]? Base64UrlDecode(string input)
    {
        if (string.IsNullOrEmpty(input))
            return null;

        try
        {
            // Replace base64url characters with base64 characters
            var output = input.Replace('-', '+').Replace('_', '/');

            // Add padding if necessary
            switch (output.Length % 4)
            {
                case 2:
                    output += "==";
                    break;
                case 3:
                    output += "=";
                    break;
            }

            return Convert.FromBase64String(output);
        }
        catch
        {
            return null;
        }
    }

    public static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
