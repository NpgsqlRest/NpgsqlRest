using System.Security.Cryptography;
using System.Text;
using static NpgsqlRestClient.Fido2.RequestParsers;

namespace NpgsqlRestClient.Fido2;

public static class AssertionValidator
{
    public static AssertionResult Validate(
        byte[] authenticatorData,
        byte[] clientDataJson,
        byte[] signature,
        byte[] publicKey,
        int algorithm,
        long storedSignCount,
        byte[] expectedChallenge,
        string[] expectedOrigins,
        string expectedRpId,
        bool requireUserVerification = false,
        bool validateSignCount = true)
    {
        // Step 1: Parse and validate clientDataJSON
        ClientDataParsed clientData;
        try
        {
            clientData = ParseClientData(clientDataJson);
        }
        catch
        {
            return AssertionResult.Fail(ValidationError.InvalidClientData);
        }

        if (clientData.Type == null || clientData.Challenge == null || clientData.Origin == null)
            return AssertionResult.Fail(ValidationError.InvalidClientData);

        // Step 2: Verify type is "webauthn.get"
        if (clientData.Type != "webauthn.get")
            return AssertionResult.Fail($"{ValidationError.InvalidType}: expected 'webauthn.get', got '{clientData.Type}'");

        // Step 3: Verify challenge matches
        var challengeBytes = AttestationValidator.Base64UrlDecode(clientData.Challenge);
        if (challengeBytes == null || !CryptographicOperations.FixedTimeEquals(challengeBytes, expectedChallenge))
            return AssertionResult.Fail(ValidationError.ChallengeMismatch);

        // Step 4: Verify origin
        if (!IsOriginAllowed(clientData.Origin, expectedOrigins))
            return AssertionResult.Fail($"{ValidationError.OriginMismatch}: {clientData.Origin}");

        // Step 5: Parse authenticator data
        if (authenticatorData.Length < 37)
            return AssertionResult.Fail(ValidationError.InvalidAuthenticatorData);

        var rpIdHash = authenticatorData[..32];
        var flags = authenticatorData[32];
        var signCount = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(authenticatorData.AsSpan(33, 4));

        var userPresent = (flags & AuthenticatorDataFlags.UP) != 0;
        var userVerified = (flags & AuthenticatorDataFlags.UV) != 0;

        // Step 6: Verify RP ID hash
        var expectedRpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedRpId));
        if (!CryptographicOperations.FixedTimeEquals(rpIdHash, expectedRpIdHash))
            return AssertionResult.Fail(ValidationError.RpIdHashMismatch);

        // Step 7: Verify user present flag
        if (!userPresent)
            return AssertionResult.Fail(ValidationError.UserNotPresent);

        // Step 8: Verify user verification if required
        if (requireUserVerification && !userVerified)
            return AssertionResult.Fail(ValidationError.UserVerificationRequired);

        // Step 9: Verify sign count (replay protection)
        // Skip if validateSignCount is false
        // If both counters are 0, the authenticator doesn't support counters
        if (validateSignCount && (signCount != 0 || storedSignCount != 0))
        {
            if (signCount <= storedSignCount)
                return AssertionResult.Fail(ValidationError.SignCountNotIncremented);
        }

        // Step 10: Verify signature
        // The signature is computed over: authenticatorData || SHA-256(clientDataJSON)
        var clientDataHash = SHA256.HashData(clientDataJson);
        var signedData = new byte[authenticatorData.Length + clientDataHash.Length];
        authenticatorData.CopyTo(signedData, 0);
        clientDataHash.CopyTo(signedData, authenticatorData.Length);

        bool signatureValid;
        try
        {
            signatureValid = VerifySignature(publicKey, signedData, signature);
        }
        catch
        {
            return AssertionResult.Fail(ValidationError.InvalidSignature);
        }

        if (!signatureValid)
            return AssertionResult.Fail(ValidationError.InvalidSignature);

        return AssertionResult.Success(
            newSignCount: signCount,
            userVerified: userVerified);
    }

    private static bool VerifySignature(byte[] publicKeyBytes, byte[] data, byte[] signature)
    {
        var credentialPublicKey = CredentialPublicKey.Decode(publicKeyBytes);
        return credentialPublicKey.Verify(data, signature);
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
}
