namespace NpgsqlRestClient.Fido2;

public static class PasskeyErrorCode
{
    public const string InvalidRequest = "invalid_request";
    public const string AuthenticationRequired = "authentication_required";
    public const string ChallengeInvalid = "challenge_invalid";
    public const string AttestationInvalid = "attestation_invalid";
    public const string AssertionInvalid = "assertion_invalid";
    public const string DatabaseError = "database_error";
    public const string StoreFailed = "store_failed";
    public const string AuthenticationFailed = "authentication_failed";
}
