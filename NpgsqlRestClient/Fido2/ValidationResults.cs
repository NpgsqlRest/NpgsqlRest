namespace NpgsqlRestClient.Fido2;

public class AttestationResult
{
    public bool IsValid { get; init; }

    public string? Error { get; init; }

    public byte[]? CredentialId { get; init; }

    public byte[]? UserHandle { get; init; }

    public byte[]? PublicKey { get; init; }

    public int Algorithm { get; init; }

    public uint SignCount { get; init; }

    public bool BackupEligible { get; init; }

    public bool BackedUp { get; init; }

    public byte[]? Aaguid { get; init; }

    public static AttestationResult Success(
        byte[] credentialId,
        byte[] publicKey,
        int algorithm,
        uint signCount,
        bool backupEligible,
        bool backedUp,
        byte[]? aaguid = null,
        byte[]? userHandle = null) => new()
    {
        IsValid = true,
        CredentialId = credentialId,
        PublicKey = publicKey,
        Algorithm = algorithm,
        SignCount = signCount,
        BackupEligible = backupEligible,
        BackedUp = backedUp,
        Aaguid = aaguid,
        UserHandle = userHandle
    };

    public static AttestationResult Fail(string error) => new()
    {
        IsValid = false,
        Error = error
    };
}

public class AssertionResult
{
    public bool IsValid { get; init; }

    public string? Error { get; init; }

    public uint NewSignCount { get; init; }

    public string? UserId { get; init; }

    public byte[]? UserHandle { get; init; }

    public bool UserVerified { get; init; }

    public static AssertionResult Success(
        uint newSignCount,
        string? userId = null,
        byte[]? userHandle = null,
        bool userVerified = false) => new()
    {
        IsValid = true,
        NewSignCount = newSignCount,
        UserId = userId,
        UserHandle = userHandle,
        UserVerified = userVerified
    };

    public static AssertionResult Fail(string error) => new()
    {
        IsValid = false,
        Error = error
    };
}

public static class ValidationError
{
    public const string ChallengeMismatch = "Challenge mismatch";

    public const string OriginMismatch = "Origin mismatch";

    public const string RpIdHashMismatch = "RP ID hash mismatch";

    public const string UserNotPresent = "User not present";

    public const string UserVerificationRequired = "User verification required but not performed";

    public const string InvalidType = "Invalid type in clientDataJSON";

    public const string InvalidClientData = "Invalid clientDataJSON";

    public const string InvalidAttestationObject = "Invalid attestation object";

    public const string InvalidAuthenticatorData = "Invalid authenticator data";

    public const string MissingAttestedCredentialData = "Missing attested credential data";

    public const string InvalidSignature = "Invalid signature";

    public const string SignCountNotIncremented = "Sign count not incremented - possible cloned authenticator";

    public const string CredentialNotFound = "Credential not found";

    public const string UnsupportedAlgorithm = "Unsupported algorithm";

    public const string ChallengeExpired = "Challenge expired";

    public const string ChallengeNotFound = "Challenge not found";
}
