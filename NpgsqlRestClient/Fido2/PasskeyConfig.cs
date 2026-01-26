namespace NpgsqlRestClient.Fido2;

public class PasskeyConfig
{
    public bool Enabled { get; set; }

    public bool EnableRegister { get; set; }

    public string? RelyingPartyId { get; set; }

    public string? RelyingPartyName { get; set; }

    public string[] RelyingPartyOrigins { get; set; } = [];

    public string? AddPasskeyOptionsPath { get; set; } = "/api/passkey/add/options";

    public string? RegistrationOptionsPath { get; set; } = "/api/passkey/register/options";

    public string? AddPasskeyPath { get; set; } = "/api/passkey/add";

    public string? RegistrationPath { get; set; } = "/api/passkey/register";

    public string? LoginOptionsPath { get; set; } = "/api/passkey/login/options";

    public string? LoginPath { get; set; } = "/api/passkey/login";

    public int ChallengeTimeoutMinutes { get; set; } = 5;

    public string UserVerificationRequirement { get; set; } = "preferred";

    public string ResidentKeyRequirement { get; set; } = "preferred";

    public string AttestationConveyance { get; set; } = "none";

    // GROUP 1: Challenge Commands (create challenges for all scenarios)

    public string ChallengeAddExistingUserCommand { get; set; } = "select * from passkey_challenge_add_existing($1,$2)";

    public string ChallengeRegistrationCommand { get; set; } = "select * from passkey_challenge_registration($1)";

    public string ChallengeAuthenticationCommand { get; set; } = "select * from passkey_challenge_authentication($1,$2)";

    // GROUP 2: Challenge Verify Command (used by ALL scenarios)

    public string ChallengeVerifyCommand { get; set; } = "select * from passkey_verify_challenge($1,$2)";

    public bool ValidateSignCount { get; set; } = true;

    // GROUP 3: Authentication Data Command (used only by authentication)

    public string AuthenticateDataCommand { get; set; } = "select * from passkey_authenticate_data($1)";

    // GROUP 4: Complete Commands (finalize each scenario)

    public string AddExistingUserCompleteCommand { get; set; } = "select * from passkey_add_existing_complete($1,$2,$3,$4,$5,$6,$7,$8)";

    public string RegistrationCompleteCommand { get; set; } = "select * from passkey_registration_complete($1,$2,$3,$4,$5,$6,$7,$8)";

    public string AuthenticateCompleteCommand { get; set; } = "select * from passkey_authenticate_complete($1,$2,$3,$4)";

    // Column name configuration for database responses

    public string StatusColumnName { get; set; } = "status";

    public string MessageColumnName { get; set; } = "message";

    public string ChallengeColumnName { get; set; } = "challenge";

    public string ChallengeIdColumnName { get; set; } = "challenge_id";

    public string UserNameColumnName { get; set; } = "user_name";

    public string UserDisplayNameColumnName { get; set; } = "user_display_name";

    public string UserHandleColumnName { get; set; } = "user_handle";

    public string ExcludeCredentialsColumnName { get; set; } = "exclude_credentials";

    public string AllowCredentialsColumnName { get; set; } = "allow_credentials";

    public string PublicKeyColumnName { get; set; } = "public_key";

    public string PublicKeyAlgorithmColumnName { get; set; } = "public_key_algorithm";

    public string SignCountColumnName { get; set; } = "sign_count";

    public string UserContextColumnName { get; set; } = "user_context";

    public string? ClientAnalyticsIpKey { get; set; } = "ip";
}
