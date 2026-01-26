# Passkey Authentication Implementation Plan for NpgsqlRestClient

## Overview

This document details the implementation plan for adding WebAuthn/FIDO2 passkey authentication to NpgsqlRestClient, based on the patterns from Andrew Lock's article on .NET 10 passkey support.

**Implementation Principles:**
- All code organized in `NpgsqlRestClient/Fido2/` directory
- One class per file for easy review
- Comprehensive XML documentation
- Unit tests for all validation logic
- Database schema documented only (user implements manually)
- TypeScript helpers documented only (user copies as needed)

---

## Table of Contents

1. [Design Decisions](#design-decisions)
2. [New Files](#new-files)
3. [Modified Files](#modified-files)
4. [Database Schema Examples](#database-schema-examples)
5. [API Endpoints](#api-endpoints)
6. [TypeScript/JavaScript Client Helpers](#typescriptjavascript-client-helpers)
7. [Configuration Options](#configuration-options)
8. [Security Considerations](#security-considerations)
9. [Testing Strategy](#testing-strategy)

---

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| External FIDO2 library | No - build minimal in-house | AOT compatibility, fewer dependencies |
| Database approach | PostgreSQL stored procedures | Matches existing auth patterns |
| Auth modes | Both standalone AND secondary | Configurable via `AllowStandaloneRegistration` |
| Challenge storage | Database (configurable) | Users can customize (DB, cache, in-memory) |
| Token integration | Reuse existing JWT/Cookie auth | Seamless integration, no new token types |

---

## New Files

All new files will be placed in `NpgsqlRestClient/Fido2/` directory with one class per file.

### File Structure

```
NpgsqlRestClient/
└── Fido2/
    ├── PasskeyConfig.cs           # Configuration class
    ├── PasskeyDtos.cs             # All DTOs (request/response types)
    ├── PasskeyJsonContext.cs      # AOT-compatible JSON serialization
    ├── PasskeyAuth.cs             # Main middleware handler
    ├── AttestationValidator.cs    # Registration validation
    ├── AssertionValidator.cs      # Authentication validation
    ├── CborDecoder.cs             # Minimal CBOR decoder
    ├── CoseKey.cs                 # COSE key structure
    ├── AuthenticatorData.cs       # Authenticator data parsing
    └── ValidationResults.cs       # Result types for validators

NpgsqlRestTests/
└── Fido2/
    ├── CborDecoderTests.cs
    ├── AttestationValidatorTests.cs
    └── AssertionValidatorTests.cs
```

---

### 1. `NpgsqlRestClient/Fido2/PasskeyConfig.cs`

Configuration class for passkey authentication.

```csharp
namespace NpgsqlRestClient.Fido2;

/// <summary>
/// Configuration options for WebAuthn/FIDO2 passkey authentication.
/// </summary>
public class PasskeyConfig
{
    /// <summary>
    /// Gets or sets whether passkey authentication is enabled.
    /// Default: false
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the Relying Party ID (domain name).
    /// If null, auto-detected from the request host.
    /// Example: "example.com"
    /// </summary>
    public string? RelyingPartyId { get; set; }

    /// <summary>
    /// Gets or sets the human-readable Relying Party name.
    /// Default: "NpgsqlRest Application"
    /// </summary>
    public string RelyingPartyName { get; set; } = "NpgsqlRest Application";

    /// <summary>
    /// Gets or sets the allowed origins for origin validation.
    /// Example: ["https://example.com", "https://www.example.com"]
    /// </summary>
    public string[] RelyingPartyOrigins { get; set; } = [];

    /// <summary>
    /// Gets or sets whether standalone passkey registration is allowed.
    /// When true, users can register with passkey only (no password).
    /// When false, passkey registration requires existing authentication.
    /// Default: true
    /// </summary>
    public bool AllowStandaloneRegistration { get; set; } = true;

    /// <summary>
    /// Gets or sets the path for the registration options endpoint.
    /// Default: "/api/passkey/register/options"
    /// </summary>
    public string RegistrationOptionsPath { get; set; } = "/api/passkey/register/options";

    /// <summary>
    /// Gets or sets the path for the registration completion endpoint.
    /// Default: "/api/passkey/register"
    /// </summary>
    public string RegisterPath { get; set; } = "/api/passkey/register";

    /// <summary>
    /// Gets or sets the path for the authentication options endpoint.
    /// Default: "/api/passkey/login/options"
    /// </summary>
    public string AuthenticationOptionsPath { get; set; } = "/api/passkey/login/options";

    /// <summary>
    /// Gets or sets the path for the authentication completion endpoint.
    /// Default: "/api/passkey/login"
    /// </summary>
    public string AuthenticatePath { get; set; } = "/api/passkey/login";

    /// <summary>
    /// Gets or sets the challenge timeout in minutes.
    /// Default: 5
    /// </summary>
    public int ChallengeTimeoutMinutes { get; set; } = 5;

    /// <summary>
    /// Gets or sets the user verification requirement.
    /// Values: "preferred", "required", "discouraged"
    /// Default: "preferred"
    /// </summary>
    public string UserVerificationRequirement { get; set; } = "preferred";

    /// <summary>
    /// Gets or sets the resident key (discoverable credential) requirement.
    /// Values: "preferred", "required", "discouraged"
    /// Default: "preferred"
    /// </summary>
    public string ResidentKeyRequirement { get; set; } = "preferred";

    /// <summary>
    /// Gets or sets the attestation conveyance preference.
    /// Values: "none", "indirect", "direct", "enterprise"
    /// Default: "none"
    /// </summary>
    public string AttestationConveyance { get; set; } = "none";

    #region Database Commands

    /// <summary>
    /// SQL command to get credential creation options.
    /// Parameter: $1 = user_id (text)
    /// Expected columns: status, challenge, user_id, user_name, user_display_name, user_handle, exclude_credentials, challenge_id
    /// </summary>
    public string CredentialCreationOptionsCommand { get; set; } = "select * from passkey_creation_options($1)";

    /// <summary>
    /// SQL command to store a new passkey credential.
    /// Parameters: credential_id, user_id, user_handle, public_key, algorithm, transports, backup_eligible, device_name
    /// Expected columns: status, message
    /// </summary>
    public string CredentialStoreCommand { get; set; } = "select * from passkey_store($1,$2,$3,$4,$5,$6,$7,$8)";

    /// <summary>
    /// SQL command to get credential request options.
    /// Parameter: $1 = user_name (text, optional)
    /// Expected columns: status, challenge, allow_credentials, challenge_id
    /// </summary>
    public string CredentialRequestOptionsCommand { get; set; } = "select * from passkey_request_options($1)";

    /// <summary>
    /// SQL command to get a credential for verification.
    /// Parameter: $1 = credential_id (bytea)
    /// Expected columns: user_id, user_handle, public_key, public_key_algorithm, sign_count
    /// </summary>
    public string CredentialGetCommand { get; set; } = "select * from passkey_get_credential($1)";

    /// <summary>
    /// SQL command to update sign count after authentication.
    /// Parameters: $1 = credential_id, $2 = new_sign_count
    /// </summary>
    public string SignCountUpdateCommand { get; set; } = "select passkey_update_sign_count($1,$2)";

    /// <summary>
    /// SQL command to store a challenge temporarily.
    /// Parameters: $1 = challenge, $2 = user_id, $3 = operation
    /// Expected columns: challenge_id
    /// </summary>
    public string ChallengeStoreCommand { get; set; } = "select passkey_store_challenge($1,$2,$3)";

    /// <summary>
    /// SQL command to verify and consume a challenge.
    /// Parameters: $1 = challenge_id, $2 = operation
    /// Returns: challenge bytes (bytea)
    /// </summary>
    public string ChallengeVerifyCommand { get; set; } = "select * from passkey_verify_challenge($1,$2)";

    /// <summary>
    /// SQL command to get user claims after successful authentication.
    /// Parameter: $1 = user_id
    /// Expected columns: status, user_id, user_name, user_roles (and any additional claims)
    /// </summary>
    public string LoginCommand { get; set; } = "select * from passkey_login($1)";

    #endregion
}
```

---

### 2. `NpgsqlRestClient/Fido2/PasskeyDtos.cs`

All DTOs for passkey requests and responses.

```csharp
using System.Text.Json.Serialization;

namespace NpgsqlRestClient.Fido2;

#region Error and Success Responses

/// <summary>
/// Error response for passkey operations.
/// </summary>
public class PasskeyErrorResponse
{
    /// <summary>
    /// Gets or sets the error code/type.
    /// </summary>
    [JsonPropertyName("error")]
    public string Error { get; set; } = null!;

    /// <summary>
    /// Gets or sets the detailed error description.
    /// </summary>
    [JsonPropertyName("errorDescription")]
    public string? ErrorDescription { get; set; }
}

/// <summary>
/// Success response for passkey registration.
/// </summary>
public class PasskeySuccessResponse
{
    /// <summary>
    /// Gets or sets whether the operation succeeded.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the registered credential ID (base64url encoded).
    /// </summary>
    [JsonPropertyName("credentialId")]
    public string? CredentialId { get; set; }
}

#endregion

#region WebAuthn Options Responses

/// <summary>
/// WebAuthn PublicKeyCredentialCreationOptions response for registration.
/// </summary>
public class PasskeyCreationOptionsResponse
{
    /// <summary>
    /// Gets or sets the challenge (base64url encoded).
    /// </summary>
    [JsonPropertyName("challenge")]
    public string Challenge { get; set; } = null!;

    /// <summary>
    /// Gets or sets the Relying Party information.
    /// </summary>
    [JsonPropertyName("rp")]
    public RelyingPartyInfo Rp { get; set; } = null!;

    /// <summary>
    /// Gets or sets the user information.
    /// </summary>
    [JsonPropertyName("user")]
    public UserInfo User { get; set; } = null!;

    /// <summary>
    /// Gets or sets the supported public key credential parameters.
    /// </summary>
    [JsonPropertyName("pubKeyCredParams")]
    public List<PubKeyCredParam> PubKeyCredParams { get; set; } = [];

    /// <summary>
    /// Gets or sets the timeout in milliseconds.
    /// Default: 60000 (60 seconds)
    /// </summary>
    [JsonPropertyName("timeout")]
    public int Timeout { get; set; } = 60000;

    /// <summary>
    /// Gets or sets the attestation conveyance preference.
    /// </summary>
    [JsonPropertyName("attestation")]
    public string Attestation { get; set; } = "none";

    /// <summary>
    /// Gets or sets the authenticator selection criteria.
    /// </summary>
    [JsonPropertyName("authenticatorSelection")]
    public AuthenticatorSelection? AuthenticatorSelection { get; set; }

    /// <summary>
    /// Gets or sets credentials to exclude (already registered).
    /// </summary>
    [JsonPropertyName("excludeCredentials")]
    public List<CredentialDescriptor>? ExcludeCredentials { get; set; }

    /// <summary>
    /// Gets or sets the server-side challenge ID for verification.
    /// </summary>
    [JsonPropertyName("challengeId")]
    public string ChallengeId { get; set; } = null!;
}

/// <summary>
/// WebAuthn PublicKeyCredentialRequestOptions response for authentication.
/// </summary>
public class PasskeyRequestOptionsResponse
{
    /// <summary>
    /// Gets or sets the challenge (base64url encoded).
    /// </summary>
    [JsonPropertyName("challenge")]
    public string Challenge { get; set; } = null!;

    /// <summary>
    /// Gets or sets the Relying Party ID.
    /// </summary>
    [JsonPropertyName("rpId")]
    public string RpId { get; set; } = null!;

    /// <summary>
    /// Gets or sets the timeout in milliseconds.
    /// </summary>
    [JsonPropertyName("timeout")]
    public int Timeout { get; set; } = 60000;

    /// <summary>
    /// Gets or sets the user verification requirement.
    /// </summary>
    [JsonPropertyName("userVerification")]
    public string UserVerification { get; set; } = "preferred";

    /// <summary>
    /// Gets or sets the allowed credentials.
    /// </summary>
    [JsonPropertyName("allowCredentials")]
    public List<CredentialDescriptor>? AllowCredentials { get; set; }

    /// <summary>
    /// Gets or sets the server-side challenge ID for verification.
    /// </summary>
    [JsonPropertyName("challengeId")]
    public string ChallengeId { get; set; } = null!;
}

#endregion

#region Supporting Types

/// <summary>
/// Relying Party information for WebAuthn.
/// </summary>
public class RelyingPartyInfo
{
    /// <summary>
    /// Gets or sets the RP ID (domain).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    /// <summary>
    /// Gets or sets the human-readable RP name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;
}

/// <summary>
/// User information for WebAuthn registration.
/// </summary>
public class UserInfo
{
    /// <summary>
    /// Gets or sets the user handle (base64url encoded).
    /// Must be unique per user and should not contain PII.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    /// <summary>
    /// Gets or sets the username (e.g., email).
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = null!;
}

/// <summary>
/// Public key credential parameter.
/// </summary>
public class PubKeyCredParam
{
    /// <summary>
    /// Gets or sets the credential type. Always "public-key".
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "public-key";

    /// <summary>
    /// Gets or sets the COSE algorithm identifier.
    /// -7 = ES256 (ECDSA with P-256 and SHA-256)
    /// -257 = RS256 (RSASSA-PKCS1-v1_5 with SHA-256)
    /// </summary>
    [JsonPropertyName("alg")]
    public int Alg { get; set; }
}

/// <summary>
/// Authenticator selection criteria.
/// </summary>
public class AuthenticatorSelection
{
    /// <summary>
    /// Gets or sets the resident key requirement.
    /// Values: "discouraged", "preferred", "required"
    /// </summary>
    [JsonPropertyName("residentKey")]
    public string? ResidentKey { get; set; }

    /// <summary>
    /// Gets or sets the user verification requirement.
    /// Values: "discouraged", "preferred", "required"
    /// </summary>
    [JsonPropertyName("userVerification")]
    public string? UserVerification { get; set; }

    /// <summary>
    /// Gets or sets the authenticator attachment preference.
    /// Values: "platform", "cross-platform"
    /// </summary>
    [JsonPropertyName("authenticatorAttachment")]
    public string? AuthenticatorAttachment { get; set; }
}

/// <summary>
/// Credential descriptor for exclude/allow lists.
/// </summary>
public class CredentialDescriptor
{
    /// <summary>
    /// Gets or sets the credential type. Always "public-key".
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "public-key";

    /// <summary>
    /// Gets or sets the credential ID (base64url encoded).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    /// <summary>
    /// Gets or sets the authenticator transports.
    /// Values: "usb", "nfc", "ble", "internal", "hybrid"
    /// </summary>
    [JsonPropertyName("transports")]
    public List<string>? Transports { get; set; }
}

#endregion

#region Request Types

/// <summary>
/// Request body for completing passkey registration.
/// </summary>
public class PasskeyRegistrationRequest
{
    /// <summary>
    /// Gets or sets the challenge ID from the options response.
    /// </summary>
    [JsonPropertyName("challengeId")]
    public string ChallengeId { get; set; } = null!;

    /// <summary>
    /// Gets or sets the credential ID (base64url encoded).
    /// </summary>
    [JsonPropertyName("credentialId")]
    public string CredentialId { get; set; } = null!;

    /// <summary>
    /// Gets or sets the attestation object (base64url encoded).
    /// </summary>
    [JsonPropertyName("attestationObject")]
    public string AttestationObject { get; set; } = null!;

    /// <summary>
    /// Gets or sets the client data JSON (base64url encoded).
    /// </summary>
    [JsonPropertyName("clientDataJSON")]
    public string ClientDataJSON { get; set; } = null!;

    /// <summary>
    /// Gets or sets the authenticator transports.
    /// </summary>
    [JsonPropertyName("transports")]
    public List<string>? Transports { get; set; }

    /// <summary>
    /// Gets or sets the user-friendly device name.
    /// </summary>
    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; set; }

    /// <summary>
    /// Gets or sets the username (for standalone registration).
    /// </summary>
    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    /// <summary>
    /// Gets or sets the display name (for standalone registration).
    /// </summary>
    [JsonPropertyName("userDisplayName")]
    public string? UserDisplayName { get; set; }
}

/// <summary>
/// Request body for completing passkey authentication.
/// </summary>
public class PasskeyAuthenticationRequest
{
    /// <summary>
    /// Gets or sets the challenge ID from the options response.
    /// </summary>
    [JsonPropertyName("challengeId")]
    public string ChallengeId { get; set; } = null!;

    /// <summary>
    /// Gets or sets the credential ID (base64url encoded).
    /// </summary>
    [JsonPropertyName("credentialId")]
    public string CredentialId { get; set; } = null!;

    /// <summary>
    /// Gets or sets the authenticator data (base64url encoded).
    /// </summary>
    [JsonPropertyName("authenticatorData")]
    public string AuthenticatorData { get; set; } = null!;

    /// <summary>
    /// Gets or sets the client data JSON (base64url encoded).
    /// </summary>
    [JsonPropertyName("clientDataJSON")]
    public string ClientDataJSON { get; set; } = null!;

    /// <summary>
    /// Gets or sets the signature (base64url encoded).
    /// </summary>
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = null!;

    /// <summary>
    /// Gets or sets the user handle (base64url encoded, for discoverable credentials).
    /// </summary>
    [JsonPropertyName("userHandle")]
    public string? UserHandle { get; set; }
}

/// <summary>
/// Request body for getting authentication options.
/// </summary>
public class PasskeyAuthenticationOptionsRequest
{
    /// <summary>
    /// Gets or sets the optional username to filter allowed credentials.
    /// </summary>
    [JsonPropertyName("userName")]
    public string? UserName { get; set; }
}

#endregion
```

---

### 3. `NpgsqlRestClient/Fido2/PasskeyJsonContext.cs`

AOT-compatible JSON serialization context.

```csharp
using System.Text.Json.Serialization;

namespace NpgsqlRestClient.Fido2;

/// <summary>
/// AOT-compatible JSON serialization context for passkey types.
/// </summary>
[JsonSerializable(typeof(PasskeyCreationOptionsResponse))]
[JsonSerializable(typeof(PasskeyRequestOptionsResponse))]
[JsonSerializable(typeof(PasskeyRegistrationRequest))]
[JsonSerializable(typeof(PasskeyAuthenticationRequest))]
[JsonSerializable(typeof(PasskeyAuthenticationOptionsRequest))]
[JsonSerializable(typeof(PasskeyErrorResponse))]
[JsonSerializable(typeof(PasskeySuccessResponse))]
[JsonSerializable(typeof(RelyingPartyInfo))]
[JsonSerializable(typeof(UserInfo))]
[JsonSerializable(typeof(PubKeyCredParam))]
[JsonSerializable(typeof(AuthenticatorSelection))]
[JsonSerializable(typeof(CredentialDescriptor))]
[JsonSerializable(typeof(List<PubKeyCredParam>))]
[JsonSerializable(typeof(List<CredentialDescriptor>))]
[JsonSerializable(typeof(ClientData))]
internal partial class PasskeyJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Parsed clientDataJSON from WebAuthn response.
/// </summary>
internal class ClientData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = null!;

    [JsonPropertyName("challenge")]
    public string Challenge { get; set; } = null!;

    [JsonPropertyName("origin")]
    public string Origin { get; set; } = null!;

    [JsonPropertyName("crossOrigin")]
    public bool? CrossOrigin { get; set; }
}
```

---

### 4. `NpgsqlRestClient/Fido2/PasskeyAuth.cs` (Main Handler)

Main passkey authentication handler following the patterns from `ExternalAuth.cs` and `JwtAuth.cs`.

```csharp
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlRest;

namespace NpgsqlRestClient;

// AOT-compatible JSON serialization context
[JsonSerializable(typeof(PasskeyCreationOptionsResponse))]
[JsonSerializable(typeof(PasskeyRequestOptionsResponse))]
[JsonSerializable(typeof(PasskeyRegistrationRequest))]
[JsonSerializable(typeof(PasskeyAuthenticationRequest))]
[JsonSerializable(typeof(PasskeyErrorResponse))]
[JsonSerializable(typeof(PasskeySuccessResponse))]
internal partial class PasskeyJsonContext : JsonSerializerContext { }

#region DTOs

public class PasskeyErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = null!;

    [JsonPropertyName("errorDescription")]
    public string? ErrorDescription { get; set; }
}

public class PasskeySuccessResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("credentialId")]
    public string? CredentialId { get; set; }
}

public class PasskeyCreationOptionsResponse
{
    [JsonPropertyName("challenge")]
    public string Challenge { get; set; } = null!;

    [JsonPropertyName("rp")]
    public RelyingPartyInfo Rp { get; set; } = null!;

    [JsonPropertyName("user")]
    public UserInfo User { get; set; } = null!;

    [JsonPropertyName("pubKeyCredParams")]
    public List<PubKeyCredParam> PubKeyCredParams { get; set; } = [];

    [JsonPropertyName("timeout")]
    public int Timeout { get; set; } = 60000;

    [JsonPropertyName("attestation")]
    public string Attestation { get; set; } = "none";

    [JsonPropertyName("authenticatorSelection")]
    public AuthenticatorSelection? AuthenticatorSelection { get; set; }

    [JsonPropertyName("excludeCredentials")]
    public List<CredentialDescriptor>? ExcludeCredentials { get; set; }

    [JsonPropertyName("challengeId")]
    public string ChallengeId { get; set; } = null!;
}

public class PasskeyRequestOptionsResponse
{
    [JsonPropertyName("challenge")]
    public string Challenge { get; set; } = null!;

    [JsonPropertyName("rpId")]
    public string RpId { get; set; } = null!;

    [JsonPropertyName("timeout")]
    public int Timeout { get; set; } = 60000;

    [JsonPropertyName("userVerification")]
    public string UserVerification { get; set; } = "preferred";

    [JsonPropertyName("allowCredentials")]
    public List<CredentialDescriptor>? AllowCredentials { get; set; }

    [JsonPropertyName("challengeId")]
    public string ChallengeId { get; set; } = null!;
}

public class RelyingPartyInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;
}

public class UserInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = null!;
}

public class PubKeyCredParam
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "public-key";

    [JsonPropertyName("alg")]
    public int Alg { get; set; }
}

public class AuthenticatorSelection
{
    [JsonPropertyName("residentKey")]
    public string? ResidentKey { get; set; }

    [JsonPropertyName("userVerification")]
    public string? UserVerification { get; set; }

    [JsonPropertyName("authenticatorAttachment")]
    public string? AuthenticatorAttachment { get; set; }
}

public class CredentialDescriptor
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "public-key";

    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("transports")]
    public List<string>? Transports { get; set; }
}

public class PasskeyRegistrationRequest
{
    [JsonPropertyName("challengeId")]
    public string ChallengeId { get; set; } = null!;

    [JsonPropertyName("credentialId")]
    public string CredentialId { get; set; } = null!;

    [JsonPropertyName("attestationObject")]
    public string AttestationObject { get; set; } = null!;

    [JsonPropertyName("clientDataJSON")]
    public string ClientDataJSON { get; set; } = null!;

    [JsonPropertyName("transports")]
    public List<string>? Transports { get; set; }

    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; set; }

    // For standalone registration
    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    [JsonPropertyName("userDisplayName")]
    public string? UserDisplayName { get; set; }
}

public class PasskeyAuthenticationRequest
{
    [JsonPropertyName("challengeId")]
    public string ChallengeId { get; set; } = null!;

    [JsonPropertyName("credentialId")]
    public string CredentialId { get; set; } = null!;

    [JsonPropertyName("authenticatorData")]
    public string AuthenticatorData { get; set; } = null!;

    [JsonPropertyName("clientDataJSON")]
    public string ClientDataJSON { get; set; } = null!;

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = null!;

    [JsonPropertyName("userHandle")]
    public string? UserHandle { get; set; }
}

#endregion

#region Configuration

public class PasskeyConfig
{
    public bool Enabled { get; set; }
    public string? RelyingPartyId { get; set; }
    public string RelyingPartyName { get; set; } = "NpgsqlRest Application";
    public string[] RelyingPartyOrigins { get; set; } = [];
    public bool AllowStandaloneRegistration { get; set; } = true;
    public string RegistrationOptionsPath { get; set; } = "/api/passkey/register/options";
    public string RegisterPath { get; set; } = "/api/passkey/register";
    public string AuthenticationOptionsPath { get; set; } = "/api/passkey/login/options";
    public string AuthenticatePath { get; set; } = "/api/passkey/login";
    public int ChallengeTimeoutMinutes { get; set; } = 5;
    public string UserVerificationRequirement { get; set; } = "preferred";
    public string ResidentKeyRequirement { get; set; } = "preferred";
    public string AttestationConveyance { get; set; } = "none";

    // Database commands
    public string CredentialCreationOptionsCommand { get; set; } = "select * from passkey_creation_options($1)";
    public string CredentialStoreCommand { get; set; } = "select * from passkey_store($1,$2,$3,$4,$5,$6,$7,$8)";
    public string CredentialRequestOptionsCommand { get; set; } = "select * from passkey_request_options($1)";
    public string CredentialGetCommand { get; set; } = "select * from passkey_get_credential($1)";
    public string SignCountUpdateCommand { get; set; } = "select passkey_update_sign_count($1,$2)";
    public string ChallengeStoreCommand { get; set; } = "select passkey_store_challenge($1,$2,$3)";
    public string ChallengeVerifyCommand { get; set; } = "select * from passkey_verify_challenge($1,$2)";
    public string LoginCommand { get; set; } = "select * from passkey_login($1)";
}

#endregion

#region Main Handler

public class PasskeyAuth
{
    private static ILogger? Logger;

    public PasskeyAuth(
        PasskeyConfig? config,
        string connectionString,
        WebApplication app,
        NpgsqlRestOptions options,
        RetryStrategy? retryStrategy,
        PostgresConnectionNoticeLoggingMode loggingMode)
    {
        if (config?.Enabled != true)
        {
            return;
        }

        Logger = app.Services.GetService<ILoggerFactory>()?.CreateLogger<PasskeyAuth>();

        // Register middleware for all passkey endpoints
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value;

            if (path == null)
            {
                await next(context);
                return;
            }

            // Registration options endpoint
            if (path.Equals(config.RegistrationOptionsPath, StringComparison.OrdinalIgnoreCase) &&
                HttpMethods.IsPost(context.Request.Method))
            {
                await HandleRegistrationOptionsAsync(context, config, connectionString, options, retryStrategy, loggingMode);
                return;
            }

            // Registration completion endpoint
            if (path.Equals(config.RegisterPath, StringComparison.OrdinalIgnoreCase) &&
                HttpMethods.IsPost(context.Request.Method))
            {
                await HandleRegistrationAsync(context, config, connectionString, options, retryStrategy, loggingMode);
                return;
            }

            // Authentication options endpoint
            if (path.Equals(config.AuthenticationOptionsPath, StringComparison.OrdinalIgnoreCase) &&
                HttpMethods.IsPost(context.Request.Method))
            {
                await HandleAuthenticationOptionsAsync(context, config, connectionString, options, retryStrategy, loggingMode);
                return;
            }

            // Authentication completion endpoint
            if (path.Equals(config.AuthenticatePath, StringComparison.OrdinalIgnoreCase) &&
                HttpMethods.IsPost(context.Request.Method))
            {
                await HandleAuthenticationAsync(context, config, connectionString, options, retryStrategy, loggingMode);
                return;
            }

            await next(context);
        });

        Logger?.LogDebug("Passkey authentication endpoints registered: {RegistrationOptions}, {Register}, {AuthOptions}, {Auth}",
            config.RegistrationOptionsPath, config.RegisterPath,
            config.AuthenticationOptionsPath, config.AuthenticatePath);
    }

    private static async Task HandleRegistrationOptionsAsync(
        HttpContext context,
        PasskeyConfig config,
        string connectionString,
        NpgsqlRestOptions options,
        RetryStrategy? retryStrategy,
        PostgresConnectionNoticeLoggingMode loggingMode)
    {
        // Check authentication if standalone registration is not allowed
        if (!config.AllowStandaloneRegistration && !context.User.Identity?.IsAuthenticated == true)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(
                new PasskeyErrorResponse { Error = "Authentication required" },
                PasskeyJsonContext.Default.PasskeyErrorResponse));
            return;
        }

        // Get user ID from claims or request body
        string? userId = context.User.FindFirst("user_id")?.Value;

        // Call database to get creation options
        // ... implementation details
    }

    private static async Task HandleRegistrationAsync(
        HttpContext context,
        PasskeyConfig config,
        string connectionString,
        NpgsqlRestOptions options,
        RetryStrategy? retryStrategy,
        PostgresConnectionNoticeLoggingMode loggingMode)
    {
        // 1. Parse request body
        // 2. Verify challenge
        // 3. Validate attestation using WebAuthnValidator
        // 4. Store credential in database
        // 5. Return success response
    }

    private static async Task HandleAuthenticationOptionsAsync(
        HttpContext context,
        PasskeyConfig config,
        string connectionString,
        NpgsqlRestOptions options,
        RetryStrategy? retryStrategy,
        PostgresConnectionNoticeLoggingMode loggingMode)
    {
        // 1. Parse optional username from request
        // 2. Call database to get request options
        // 3. Return WebAuthn request options
    }

    private static async Task HandleAuthenticationAsync(
        HttpContext context,
        PasskeyConfig config,
        string connectionString,
        NpgsqlRestOptions options,
        RetryStrategy? retryStrategy,
        PostgresConnectionNoticeLoggingMode loggingMode)
    {
        // 1. Parse request body
        // 2. Verify challenge
        // 3. Get credential from database
        // 4. Validate assertion using WebAuthnValidator
        // 5. Update sign count
        // 6. Call login command to get user claims
        // 7. Generate JWT tokens or sign in with cookies
    }
}

#endregion
```

---

### 2. `NpgsqlRestClient/Fido2/WebAuthnValidator.cs` (~300 lines)

WebAuthn attestation and assertion validation.

```csharp
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NpgsqlRestClient.Fido2;

/// <summary>
/// Validates WebAuthn attestation responses (registration)
/// </summary>
public static class AttestationValidator
{
    /// <summary>
    /// Validates an attestation response from the browser
    /// </summary>
    public static AttestationResult Validate(
        byte[] attestationObject,
        byte[] clientDataJson,
        byte[] expectedChallenge,
        string expectedOrigin,
        string expectedRpId)
    {
        // 1. Parse clientDataJSON
        var clientData = JsonSerializer.Deserialize<ClientData>(clientDataJson);
        if (clientData == null)
            return AttestationResult.Fail("Invalid clientDataJSON");

        // 2. Verify type is "webauthn.create"
        if (clientData.Type != "webauthn.create")
            return AttestationResult.Fail($"Invalid type: {clientData.Type}");

        // 3. Verify challenge matches
        var challengeBytes = Base64UrlDecode(clientData.Challenge);
        if (!challengeBytes.SequenceEqual(expectedChallenge))
            return AttestationResult.Fail("Challenge mismatch");

        // 4. Verify origin
        if (clientData.Origin != expectedOrigin)
            return AttestationResult.Fail($"Origin mismatch: {clientData.Origin}");

        // 5. Parse attestation object (CBOR)
        var attestation = CborDecoder.DecodeAttestationObject(attestationObject);
        if (attestation == null)
            return AttestationResult.Fail("Invalid attestation object");

        // 6. Parse authenticator data
        var authData = ParseAuthenticatorData(attestation.AuthData);
        if (authData == null)
            return AttestationResult.Fail("Invalid authenticator data");

        // 7. Verify RP ID hash
        var expectedRpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedRpId));
        if (!authData.RpIdHash.SequenceEqual(expectedRpIdHash))
            return AttestationResult.Fail("RP ID hash mismatch");

        // 8. Verify user present flag
        if (!authData.UserPresent)
            return AttestationResult.Fail("User not present");

        // 9. Extract credential data
        if (authData.AttestedCredentialData == null)
            return AttestationResult.Fail("No attested credential data");

        return AttestationResult.Success(
            authData.AttestedCredentialData.CredentialId,
            authData.AttestedCredentialData.PublicKey,
            authData.AttestedCredentialData.Algorithm,
            authData.SignCount,
            authData.BackupEligible,
            authData.BackedUp);
    }

    private static AuthenticatorData? ParseAuthenticatorData(byte[] authData)
    {
        if (authData.Length < 37)
            return null;

        var rpIdHash = authData[..32];
        var flags = authData[32];
        var signCount = BinaryPrimitives.ReadUInt32BigEndian(authData.AsSpan(33, 4));

        var userPresent = (flags & 0x01) != 0;
        var userVerified = (flags & 0x04) != 0;
        var backupEligible = (flags & 0x08) != 0;
        var backedUp = (flags & 0x10) != 0;
        var hasAttestedCredentialData = (flags & 0x40) != 0;
        var hasExtensions = (flags & 0x80) != 0;

        AttestedCredentialData? credentialData = null;

        if (hasAttestedCredentialData && authData.Length > 37)
        {
            credentialData = ParseAttestedCredentialData(authData.AsSpan(37));
        }

        return new AuthenticatorData
        {
            RpIdHash = rpIdHash,
            UserPresent = userPresent,
            UserVerified = userVerified,
            BackupEligible = backupEligible,
            BackedUp = backedUp,
            SignCount = signCount,
            AttestedCredentialData = credentialData
        };
    }

    private static AttestedCredentialData? ParseAttestedCredentialData(ReadOnlySpan<byte> data)
    {
        if (data.Length < 18) // 16 (aaguid) + 2 (length)
            return null;

        var aaguid = data[..16].ToArray();
        var credentialIdLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(16, 2));

        if (data.Length < 18 + credentialIdLength)
            return null;

        var credentialId = data.Slice(18, credentialIdLength).ToArray();
        var publicKeyBytes = data[(18 + credentialIdLength)..].ToArray();

        // Parse COSE key to get algorithm
        var coseKey = CborDecoder.DecodeCoseKey(publicKeyBytes);

        return new AttestedCredentialData
        {
            Aaguid = aaguid,
            CredentialId = credentialId,
            PublicKey = publicKeyBytes,
            Algorithm = coseKey?.Algorithm ?? 0
        };
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }
        return Convert.FromBase64String(output);
    }
}

/// <summary>
/// Validates WebAuthn assertion responses (authentication)
/// </summary>
public static class AssertionValidator
{
    /// <summary>
    /// Validates an assertion response from the browser
    /// </summary>
    public static AssertionResult Validate(
        byte[] authenticatorData,
        byte[] clientDataJson,
        byte[] signature,
        byte[] publicKey,
        int algorithm,
        long storedSignCount,
        byte[] expectedChallenge,
        string expectedOrigin,
        string expectedRpId)
    {
        // 1. Parse clientDataJSON
        var clientData = JsonSerializer.Deserialize<ClientData>(clientDataJson);
        if (clientData == null)
            return AssertionResult.Fail("Invalid clientDataJSON");

        // 2. Verify type is "webauthn.get"
        if (clientData.Type != "webauthn.get")
            return AssertionResult.Fail($"Invalid type: {clientData.Type}");

        // 3. Verify challenge
        var challengeBytes = Base64UrlDecode(clientData.Challenge);
        if (!challengeBytes.SequenceEqual(expectedChallenge))
            return AssertionResult.Fail("Challenge mismatch");

        // 4. Verify origin
        if (clientData.Origin != expectedOrigin)
            return AssertionResult.Fail($"Origin mismatch: {clientData.Origin}");

        // 5. Parse authenticator data
        if (authenticatorData.Length < 37)
            return AssertionResult.Fail("Invalid authenticator data");

        var rpIdHash = authenticatorData[..32];
        var flags = authenticatorData[32];
        var signCount = BinaryPrimitives.ReadUInt32BigEndian(authenticatorData.AsSpan(33, 4));

        // 6. Verify RP ID hash
        var expectedRpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedRpId));
        if (!rpIdHash.SequenceEqual(expectedRpIdHash))
            return AssertionResult.Fail("RP ID hash mismatch");

        // 7. Verify user present
        var userPresent = (flags & 0x01) != 0;
        if (!userPresent)
            return AssertionResult.Fail("User not present");

        // 8. Verify sign count (replay protection)
        if (signCount != 0 || storedSignCount != 0)
        {
            if (signCount <= storedSignCount)
                return AssertionResult.Fail("Sign count not incremented - possible cloned authenticator");
        }

        // 9. Verify signature
        var clientDataHash = SHA256.HashData(clientDataJson);
        var signedData = new byte[authenticatorData.Length + clientDataHash.Length];
        authenticatorData.CopyTo(signedData, 0);
        clientDataHash.CopyTo(signedData, authenticatorData.Length);

        bool signatureValid = algorithm switch
        {
            -7 => VerifyES256(publicKey, signedData, signature),    // ES256
            -257 => VerifyRS256(publicKey, signedData, signature),  // RS256
            _ => false
        };

        if (!signatureValid)
            return AssertionResult.Fail("Invalid signature");

        return AssertionResult.Success(signCount);
    }

    private static bool VerifyES256(byte[] cosePublicKey, byte[] data, byte[] signature)
    {
        var coseKey = CborDecoder.DecodeCoseKey(cosePublicKey);
        if (coseKey == null || coseKey.X == null || coseKey.Y == null)
            return false;

        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = coseKey.X, Y = coseKey.Y }
        });

        // Convert from ASN.1 DER to raw r||s format if needed
        var sig = signature.Length > 64 ? ConvertDerToRaw(signature) : signature;

        return ecdsa.VerifyData(data, sig, HashAlgorithmName.SHA256);
    }

    private static bool VerifyRS256(byte[] cosePublicKey, byte[] data, byte[] signature)
    {
        var coseKey = CborDecoder.DecodeCoseKey(cosePublicKey);
        if (coseKey == null || coseKey.N == null || coseKey.E == null)
            return false;

        using var rsa = RSA.Create(new RSAParameters
        {
            Modulus = coseKey.N,
            Exponent = coseKey.E
        });

        return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    private static byte[] ConvertDerToRaw(byte[] derSignature)
    {
        // Parse ASN.1 DER signature and extract r and s values
        // Returns 64-byte raw format (32 bytes r + 32 bytes s)
        // ... implementation
        return derSignature; // Simplified
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }
        return Convert.FromBase64String(output);
    }
}

#region Data Types

public class ClientData
{
    public string Type { get; set; } = null!;
    public string Challenge { get; set; } = null!;
    public string Origin { get; set; } = null!;
    public bool? CrossOrigin { get; set; }
}

public class AuthenticatorData
{
    public byte[] RpIdHash { get; set; } = null!;
    public bool UserPresent { get; set; }
    public bool UserVerified { get; set; }
    public bool BackupEligible { get; set; }
    public bool BackedUp { get; set; }
    public uint SignCount { get; set; }
    public AttestedCredentialData? AttestedCredentialData { get; set; }
}

public class AttestedCredentialData
{
    public byte[] Aaguid { get; set; } = null!;
    public byte[] CredentialId { get; set; } = null!;
    public byte[] PublicKey { get; set; } = null!;
    public int Algorithm { get; set; }
}

public class AttestationResult
{
    public bool IsValid { get; set; }
    public string? Error { get; set; }
    public byte[]? CredentialId { get; set; }
    public byte[]? PublicKey { get; set; }
    public int Algorithm { get; set; }
    public uint SignCount { get; set; }
    public bool BackupEligible { get; set; }
    public bool BackedUp { get; set; }

    public static AttestationResult Success(byte[] credentialId, byte[] publicKey, int algorithm, uint signCount, bool backupEligible, bool backedUp)
        => new() { IsValid = true, CredentialId = credentialId, PublicKey = publicKey, Algorithm = algorithm, SignCount = signCount, BackupEligible = backupEligible, BackedUp = backedUp };

    public static AttestationResult Fail(string error)
        => new() { IsValid = false, Error = error };
}

public class AssertionResult
{
    public bool IsValid { get; set; }
    public string? Error { get; set; }
    public uint NewSignCount { get; set; }

    public static AssertionResult Success(uint newSignCount)
        => new() { IsValid = true, NewSignCount = newSignCount };

    public static AssertionResult Fail(string error)
        => new() { IsValid = false, Error = error };
}

#endregion
```

---

### 3. `NpgsqlRestClient/Fido2/CborDecoder.cs` (~150 lines)

Minimal CBOR decoder for WebAuthn attestation objects.

```csharp
namespace NpgsqlRestClient.Fido2;

/// <summary>
/// Minimal CBOR decoder for WebAuthn attestation objects.
/// Only implements what's needed for WebAuthn.
/// </summary>
public static class CborDecoder
{
    public static AttestationObject? DecodeAttestationObject(byte[] data)
    {
        try
        {
            var reader = new CborReader(data);
            var map = reader.ReadMap();

            return new AttestationObject
            {
                Fmt = map.TryGetValue("fmt", out var fmt) ? (string)fmt : "none",
                AuthData = map.TryGetValue("authData", out var authData) ? (byte[])authData : [],
                AttStmt = map.TryGetValue("attStmt", out var attStmt) ? attStmt : null
            };
        }
        catch
        {
            return null;
        }
    }

    public static CoseKey? DecodeCoseKey(byte[] data)
    {
        try
        {
            var reader = new CborReader(data);
            var map = reader.ReadMap();

            var key = new CoseKey();

            if (map.TryGetValue(1, out var kty))
                key.Kty = Convert.ToInt32(kty);

            if (map.TryGetValue(3, out var alg))
                key.Algorithm = Convert.ToInt32(alg);

            // EC2 key (kty = 2)
            if (key.Kty == 2)
            {
                if (map.TryGetValue(-1, out var crv))
                    key.Crv = Convert.ToInt32(crv);
                if (map.TryGetValue(-2, out var x))
                    key.X = (byte[])x;
                if (map.TryGetValue(-3, out var y))
                    key.Y = (byte[])y;
            }
            // RSA key (kty = 3)
            else if (key.Kty == 3)
            {
                if (map.TryGetValue(-1, out var n))
                    key.N = (byte[])n;
                if (map.TryGetValue(-2, out var e))
                    key.E = (byte[])e;
            }

            return key;
        }
        catch
        {
            return null;
        }
    }

    private class CborReader
    {
        private readonly byte[] _data;
        private int _position;

        public CborReader(byte[] data)
        {
            _data = data;
            _position = 0;
        }

        public Dictionary<object, object> ReadMap()
        {
            var result = new Dictionary<object, object>();
            var header = _data[_position++];
            var majorType = (header >> 5) & 0x07;

            if (majorType != 5) // Map
                throw new InvalidOperationException("Expected map");

            var count = ReadLength(header & 0x1F);

            for (var i = 0; i < count; i++)
            {
                var key = ReadValue();
                var value = ReadValue();
                result[key] = value;
            }

            return result;
        }

        private object ReadValue()
        {
            var header = _data[_position++];
            var majorType = (header >> 5) & 0x07;
            var additionalInfo = header & 0x1F;

            return majorType switch
            {
                0 => ReadLength(additionalInfo),              // Unsigned integer
                1 => -1 - (long)ReadLength(additionalInfo),   // Negative integer
                2 => ReadByteString(additionalInfo),          // Byte string
                3 => ReadTextString(additionalInfo),          // Text string
                5 => ReadMapInternal(additionalInfo),         // Map
                _ => throw new NotSupportedException($"CBOR major type {majorType}")
            };
        }

        private ulong ReadLength(int additionalInfo)
        {
            return additionalInfo switch
            {
                < 24 => (ulong)additionalInfo,
                24 => _data[_position++],
                25 => (ulong)((_data[_position++] << 8) | _data[_position++]),
                26 => (ulong)((_data[_position++] << 24) | (_data[_position++] << 16) | (_data[_position++] << 8) | _data[_position++]),
                _ => throw new NotSupportedException()
            };
        }

        private byte[] ReadByteString(int additionalInfo)
        {
            var length = (int)ReadLength(additionalInfo);
            var result = _data.AsSpan(_position, length).ToArray();
            _position += length;
            return result;
        }

        private string ReadTextString(int additionalInfo)
        {
            var bytes = ReadByteString(additionalInfo);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        private Dictionary<object, object> ReadMapInternal(int additionalInfo)
        {
            var count = (int)ReadLength(additionalInfo);
            var result = new Dictionary<object, object>();

            for (var i = 0; i < count; i++)
            {
                var key = ReadValue();
                var value = ReadValue();
                result[key] = value;
            }

            return result;
        }
    }
}

public class AttestationObject
{
    public string Fmt { get; set; } = "none";
    public byte[] AuthData { get; set; } = [];
    public object? AttStmt { get; set; }
}

public class CoseKey
{
    public int Kty { get; set; }        // Key type: 2=EC2, 3=RSA
    public int Algorithm { get; set; }  // -7=ES256, -257=RS256
    public int Crv { get; set; }        // Curve: 1=P-256
    public byte[]? X { get; set; }      // EC X coordinate
    public byte[]? Y { get; set; }      // EC Y coordinate
    public byte[]? N { get; set; }      // RSA modulus
    public byte[]? E { get; set; }      // RSA exponent
}
```

---

## Modified Files

### 1. `NpgsqlRestClient/ConfigDefaults.cs`

Add PasskeyAuth section to `GetAuthDefaults()` method:

```csharp
// In GetAuthDefaults() method, add after External auth section:

["PasskeyAuth"] = new JsonObject
{
    ["Enabled"] = false,
    ["RelyingPartyId"] = null,
    ["RelyingPartyName"] = "NpgsqlRest Application",
    ["RelyingPartyOrigins"] = new JsonArray(),
    ["AllowStandaloneRegistration"] = true,
    ["RegistrationOptionsPath"] = "/api/passkey/register/options",
    ["RegisterPath"] = "/api/passkey/register",
    ["AuthenticationOptionsPath"] = "/api/passkey/login/options",
    ["AuthenticatePath"] = "/api/passkey/login",
    ["ChallengeTimeoutMinutes"] = 5,
    ["UserVerificationRequirement"] = "preferred",
    ["ResidentKeyRequirement"] = "preferred",
    ["AttestationConveyance"] = "none",
    ["CredentialCreationOptionsCommand"] = "select * from passkey_creation_options($1)",
    ["CredentialStoreCommand"] = "select * from passkey_store($1,$2,$3,$4,$5,$6,$7,$8)",
    ["CredentialRequestOptionsCommand"] = "select * from passkey_request_options($1)",
    ["CredentialGetCommand"] = "select * from passkey_get_credential($1)",
    ["SignCountUpdateCommand"] = "select passkey_update_sign_count($1,$2)",
    ["ChallengeStoreCommand"] = "select passkey_store_challenge($1,$2,$3)",
    ["ChallengeVerifyCommand"] = "select * from passkey_verify_challenge($1,$2)",
    ["LoginCommand"] = "select * from passkey_login($1)"
}
```

**Location**: After line 214 (after the External auth `JsonObject` closing brace)

---

### 2. `NpgsqlRestClient/Builder.cs`

Add `PasskeyConfig` property and `BuildPasskeyAuthentication()` method:

```csharp
// Add property (around line 44, after ExternalAuthConfig):
public PasskeyConfig? PasskeyConfig { get; private set; } = null;

// Add method (after BuildAuthentication() method, around line 710):
public void BuildPasskeyAuthentication()
{
    var authCfg = _config.Cfg.GetSection("Auth");
    var passkeyCfg = authCfg?.GetSection("PasskeyAuth");

    if (_config.Exists(passkeyCfg) is false || _config.GetConfigBool("Enabled", passkeyCfg) is false)
    {
        return;
    }

    PasskeyConfig = new PasskeyConfig
    {
        Enabled = true,
        RelyingPartyId = _config.GetConfigStr("RelyingPartyId", passkeyCfg),
        RelyingPartyName = _config.GetConfigStr("RelyingPartyName", passkeyCfg) ?? "NpgsqlRest Application",
        RelyingPartyOrigins = _config.GetConfigArray("RelyingPartyOrigins", passkeyCfg)?.Select(x => x?.ToString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToArray() ?? [],
        AllowStandaloneRegistration = _config.GetConfigBool("AllowStandaloneRegistration", passkeyCfg, true),
        RegistrationOptionsPath = _config.GetConfigStr("RegistrationOptionsPath", passkeyCfg) ?? "/api/passkey/register/options",
        RegisterPath = _config.GetConfigStr("RegisterPath", passkeyCfg) ?? "/api/passkey/register",
        AuthenticationOptionsPath = _config.GetConfigStr("AuthenticationOptionsPath", passkeyCfg) ?? "/api/passkey/login/options",
        AuthenticatePath = _config.GetConfigStr("AuthenticatePath", passkeyCfg) ?? "/api/passkey/login",
        ChallengeTimeoutMinutes = _config.GetConfigInt("ChallengeTimeoutMinutes", passkeyCfg) ?? 5,
        UserVerificationRequirement = _config.GetConfigStr("UserVerificationRequirement", passkeyCfg) ?? "preferred",
        ResidentKeyRequirement = _config.GetConfigStr("ResidentKeyRequirement", passkeyCfg) ?? "preferred",
        AttestationConveyance = _config.GetConfigStr("AttestationConveyance", passkeyCfg) ?? "none",
        CredentialCreationOptionsCommand = _config.GetConfigStr("CredentialCreationOptionsCommand", passkeyCfg) ?? "select * from passkey_creation_options($1)",
        CredentialStoreCommand = _config.GetConfigStr("CredentialStoreCommand", passkeyCfg) ?? "select * from passkey_store($1,$2,$3,$4,$5,$6,$7,$8)",
        CredentialRequestOptionsCommand = _config.GetConfigStr("CredentialRequestOptionsCommand", passkeyCfg) ?? "select * from passkey_request_options($1)",
        CredentialGetCommand = _config.GetConfigStr("CredentialGetCommand", passkeyCfg) ?? "select * from passkey_get_credential($1)",
        SignCountUpdateCommand = _config.GetConfigStr("SignCountUpdateCommand", passkeyCfg) ?? "select passkey_update_sign_count($1,$2)",
        ChallengeStoreCommand = _config.GetConfigStr("ChallengeStoreCommand", passkeyCfg) ?? "select passkey_store_challenge($1,$2,$3)",
        ChallengeVerifyCommand = _config.GetConfigStr("ChallengeVerifyCommand", passkeyCfg) ?? "select * from passkey_verify_challenge($1,$2)",
        LoginCommand = _config.GetConfigStr("LoginCommand", passkeyCfg) ?? "select * from passkey_login($1)"
    };

    Logger?.LogDebug("Passkey authentication configured with RP ID: {RpId}", PasskeyConfig.RelyingPartyId ?? "(auto-detect from request)");
}
```

---

### 3. `NpgsqlRestClient/App.cs`

Wire up PasskeyAuth middleware:

```csharp
// Add after ExternalAuth initialization (search for "new ExternalAuth"):

if (builder.PasskeyConfig?.Enabled == true)
{
    new PasskeyAuth(
        builder.PasskeyConfig,
        connectionString,
        app,
        npgsqlRestOptions,
        cmdRetryStrategy,
        loggingMode);
}
```

Also add call to `BuildPasskeyAuthentication()` (search for `builder.BuildAuthentication()`):

```csharp
builder.BuildAuthentication();
builder.BuildPasskeyAuthentication();  // Add this line
```

---

## TypeScript/JavaScript Client Helpers

The following TypeScript helper functions are provided for reference. Users should copy and adapt them to their frontend framework.

```typescript
// Generated passkey helpers (when passkey endpoints detected)

/**
 * Check if WebAuthn/Passkey is supported in this browser
 */
export function isPasskeySupported(): boolean {
    return typeof window !== 'undefined' &&
           window.PublicKeyCredential !== undefined &&
           typeof window.PublicKeyCredential === 'function';
}

/**
 * Check if conditional mediation (autofill) is supported
 */
export async function isConditionalMediationSupported(): Promise<boolean> {
    if (!isPasskeySupported()) return false;
    return typeof PublicKeyCredential.isConditionalMediationAvailable === 'function' &&
           await PublicKeyCredential.isConditionalMediationAvailable();
}

/**
 * Register a new passkey for the current user
 */
export async function registerPasskey(deviceName?: string): Promise<{
    status: number;
    response?: { success: boolean; credentialId: string };
    error?: { error: string; errorDescription?: string };
}> {
    // 1. Get creation options from server
    const optionsResponse = await fetch(baseUrl + '/api/passkey/register/options', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include'
    });

    if (!optionsResponse.ok) {
        return { status: optionsResponse.status, error: await optionsResponse.json() };
    }

    const options = await optionsResponse.json();

    // 2. Convert options for WebAuthn API
    const publicKeyCredentialCreationOptions: PublicKeyCredentialCreationOptions = {
        challenge: base64UrlToBuffer(options.challenge),
        rp: options.rp,
        user: {
            id: base64UrlToBuffer(options.user.id),
            name: options.user.name,
            displayName: options.user.displayName
        },
        pubKeyCredParams: options.pubKeyCredParams,
        authenticatorSelection: options.authenticatorSelection,
        timeout: options.timeout,
        attestation: options.attestation as AttestationConveyancePreference,
        excludeCredentials: options.excludeCredentials?.map((c: any) => ({
            type: c.type,
            id: base64UrlToBuffer(c.id),
            transports: c.transports
        }))
    };

    // 3. Create credential
    const credential = await navigator.credentials.create({
        publicKey: publicKeyCredentialCreationOptions
    }) as PublicKeyCredential;

    if (!credential) {
        return { status: 400, error: { error: 'Credential creation cancelled' } };
    }

    const attestationResponse = credential.response as AuthenticatorAttestationResponse;

    // 4. Submit to server
    const registerResponse = await fetch(baseUrl + '/api/passkey/register', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
            challengeId: options.challengeId,
            credentialId: bufferToBase64Url(credential.rawId),
            attestationObject: bufferToBase64Url(attestationResponse.attestationObject),
            clientDataJSON: bufferToBase64Url(attestationResponse.clientDataJSON),
            transports: attestationResponse.getTransports?.() ?? [],
            deviceName
        })
    });

    if (!registerResponse.ok) {
        return { status: registerResponse.status, error: await registerResponse.json() };
    }

    return { status: 200, response: await registerResponse.json() };
}

/**
 * Authenticate using a passkey
 */
export async function authenticateWithPasskey(userName?: string): Promise<{
    status: number;
    response?: { accessToken: string; refreshToken: string; tokenType: string; expiresIn: number };
    error?: { error: string; errorDescription?: string };
}> {
    // 1. Get authentication options
    const optionsResponse = await fetch(baseUrl + '/api/passkey/login/options', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ userName })
    });

    if (!optionsResponse.ok) {
        return { status: optionsResponse.status, error: await optionsResponse.json() };
    }

    const options = await optionsResponse.json();

    // 2. Convert options for WebAuthn API
    const publicKeyCredentialRequestOptions: PublicKeyCredentialRequestOptions = {
        challenge: base64UrlToBuffer(options.challenge),
        rpId: options.rpId,
        timeout: options.timeout,
        userVerification: options.userVerification,
        allowCredentials: options.allowCredentials?.map((c: any) => ({
            type: c.type,
            id: base64UrlToBuffer(c.id),
            transports: c.transports
        }))
    };

    // 3. Get credential
    const credential = await navigator.credentials.get({
        publicKey: publicKeyCredentialRequestOptions
    }) as PublicKeyCredential;

    if (!credential) {
        return { status: 400, error: { error: 'Authentication cancelled' } };
    }

    const assertionResponse = credential.response as AuthenticatorAssertionResponse;

    // 4. Submit to server
    const authResponse = await fetch(baseUrl + '/api/passkey/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            challengeId: options.challengeId,
            credentialId: bufferToBase64Url(credential.rawId),
            authenticatorData: bufferToBase64Url(assertionResponse.authenticatorData),
            clientDataJSON: bufferToBase64Url(assertionResponse.clientDataJSON),
            signature: bufferToBase64Url(assertionResponse.signature),
            userHandle: assertionResponse.userHandle ? bufferToBase64Url(assertionResponse.userHandle) : null
        })
    });

    if (!authResponse.ok) {
        return { status: authResponse.status, error: await authResponse.json() };
    }

    return { status: 200, response: await authResponse.json() };
}

// Helper functions
function base64UrlToBuffer(base64url: string): ArrayBuffer {
    const base64 = base64url.replace(/-/g, '+').replace(/_/g, '/');
    const pad = base64.length % 4 === 0 ? '' : '='.repeat(4 - (base64.length % 4));
    const binary = atob(base64 + pad);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
    }
    return bytes.buffer;
}

function bufferToBase64Url(buffer: ArrayBuffer): string {
    const bytes = new Uint8Array(buffer);
    let binary = '';
    for (let i = 0; i < bytes.length; i++) {
        binary += String.fromCharCode(bytes[i]);
    }
    return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
}
```

---

## Database Schema Examples

Users will create their own schema. Here are examples to include in documentation:

### Tables

```sql
-- Passkey credentials table
CREATE TABLE passkeys (
    credential_id bytea PRIMARY KEY,
    user_id text NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    user_handle bytea NOT NULL,
    public_key bytea NOT NULL,
    public_key_algorithm int NOT NULL,  -- -7=ES256, -257=RS256
    sign_count bigint NOT NULL DEFAULT 0,
    transports text[] DEFAULT '{}',
    is_backup_eligible boolean DEFAULT false,
    is_backed_up boolean DEFAULT false,
    device_name text,
    created_at timestamptz NOT NULL DEFAULT now(),
    last_used_at timestamptz
);

CREATE INDEX idx_passkeys_user_id ON passkeys(user_id);
CREATE INDEX idx_passkeys_user_handle ON passkeys(user_handle);

-- Passkey challenges table (short-lived)
CREATE TABLE passkey_challenges (
    challenge_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    challenge bytea NOT NULL,
    user_id text,
    operation text NOT NULL,  -- 'registration' or 'authentication'
    expires_at timestamptz NOT NULL
);

CREATE INDEX idx_passkey_challenges_expires ON passkey_challenges(expires_at);
```

### Functions

```sql
-- Get creation options for passkey registration
CREATE OR REPLACE FUNCTION passkey_creation_options(_user_id text)
RETURNS TABLE (
    status int,
    challenge text,
    user_id text,
    user_name text,
    user_display_name text,
    user_handle text,
    exclude_credentials jsonb,
    challenge_id uuid
) LANGUAGE plpgsql AS $$
DECLARE
    v_challenge bytea;
    v_challenge_id uuid;
    v_user_handle bytea;
    v_user_name text;
    v_user_display_name text;
    v_exclude jsonb;
BEGIN
    -- Get user info
    SELECT u.name, u.display_name, u.passkey_user_handle
    INTO v_user_name, v_user_display_name, v_user_handle
    FROM users u WHERE u.id = _user_id;

    IF NOT FOUND THEN
        RETURN QUERY SELECT 404, NULL, NULL, NULL, NULL, NULL, NULL, NULL::uuid;
        RETURN;
    END IF;

    -- Generate user handle if not exists
    IF v_user_handle IS NULL THEN
        v_user_handle := gen_random_bytes(32);
        UPDATE users SET passkey_user_handle = v_user_handle WHERE id = _user_id;
    END IF;

    -- Generate challenge
    v_challenge := gen_random_bytes(32);

    -- Store challenge
    INSERT INTO passkey_challenges (challenge, user_id, operation, expires_at)
    VALUES (v_challenge, _user_id, 'registration', now() + interval '5 minutes')
    RETURNING passkey_challenges.challenge_id INTO v_challenge_id;

    -- Get existing credentials to exclude
    SELECT COALESCE(jsonb_agg(jsonb_build_object(
        'type', 'public-key',
        'id', encode(p.credential_id, 'base64'),
        'transports', p.transports
    )), '[]'::jsonb)
    INTO v_exclude
    FROM passkeys p WHERE p.user_id = _user_id;

    RETURN QUERY SELECT
        200,
        encode(v_challenge, 'base64'),
        _user_id,
        v_user_name,
        v_user_display_name,
        encode(v_user_handle, 'base64'),
        v_exclude,
        v_challenge_id;
END;
$$;

-- Store a new passkey credential
CREATE OR REPLACE FUNCTION passkey_store(
    _credential_id bytea,
    _user_id text,
    _user_handle bytea,
    _public_key bytea,
    _algorithm int,
    _transports text[],
    _backup_eligible boolean,
    _device_name text
) RETURNS TABLE (status int, message text) LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO passkeys (
        credential_id, user_id, user_handle, public_key, public_key_algorithm,
        transports, is_backup_eligible, device_name
    ) VALUES (
        _credential_id, _user_id, _user_handle, _public_key, _algorithm,
        _transports, _backup_eligible, _device_name
    );

    RETURN QUERY SELECT 200, 'Passkey registered successfully'::text;
EXCEPTION WHEN unique_violation THEN
    RETURN QUERY SELECT 409, 'Credential already registered'::text;
END;
$$;

-- Get request options for passkey authentication
CREATE OR REPLACE FUNCTION passkey_request_options(_user_name text DEFAULT NULL)
RETURNS TABLE (
    status int,
    challenge text,
    allow_credentials jsonb,
    challenge_id uuid
) LANGUAGE plpgsql AS $$
DECLARE
    v_challenge bytea;
    v_challenge_id uuid;
    v_allow jsonb;
BEGIN
    -- Generate challenge
    v_challenge := gen_random_bytes(32);

    -- Store challenge
    INSERT INTO passkey_challenges (challenge, operation, expires_at)
    VALUES (v_challenge, 'authentication', now() + interval '5 minutes')
    RETURNING passkey_challenges.challenge_id INTO v_challenge_id;

    -- Get allowed credentials if username provided
    IF _user_name IS NOT NULL THEN
        SELECT COALESCE(jsonb_agg(jsonb_build_object(
            'type', 'public-key',
            'id', encode(p.credential_id, 'base64'),
            'transports', p.transports
        )), '[]'::jsonb)
        INTO v_allow
        FROM passkeys p
        JOIN users u ON p.user_id = u.id
        WHERE u.name = _user_name;
    ELSE
        v_allow := '[]'::jsonb;
    END IF;

    RETURN QUERY SELECT 200, encode(v_challenge, 'base64'), v_allow, v_challenge_id;
END;
$$;

-- Get credential for verification
CREATE OR REPLACE FUNCTION passkey_get_credential(_credential_id bytea)
RETURNS TABLE (
    user_id text,
    user_handle bytea,
    public_key bytea,
    public_key_algorithm int,
    sign_count bigint
) LANGUAGE sql AS $$
    SELECT p.user_id, p.user_handle, p.public_key, p.public_key_algorithm, p.sign_count
    FROM passkeys p
    WHERE p.credential_id = _credential_id;
$$;

-- Update sign count after successful authentication
CREATE OR REPLACE FUNCTION passkey_update_sign_count(_credential_id bytea, _new_sign_count bigint)
RETURNS void LANGUAGE sql AS $$
    UPDATE passkeys
    SET sign_count = _new_sign_count, last_used_at = now()
    WHERE credential_id = _credential_id;
$$;

-- Verify and consume challenge
CREATE OR REPLACE FUNCTION passkey_verify_challenge(_challenge_id uuid, _operation text)
RETURNS bytea LANGUAGE plpgsql AS $$
DECLARE
    v_challenge bytea;
BEGIN
    DELETE FROM passkey_challenges
    WHERE challenge_id = _challenge_id
      AND operation = _operation
      AND expires_at > now()
    RETURNING challenge INTO v_challenge;

    RETURN v_challenge;
END;
$$;

-- Get user claims after successful passkey authentication
CREATE OR REPLACE FUNCTION passkey_login(_user_id text)
RETURNS TABLE (
    status int,
    user_id text,
    user_name text,
    user_roles text
) LANGUAGE sql AS $$
    SELECT 200, u.id, u.name, array_to_string(u.roles, ',')
    FROM users u
    WHERE u.id = _user_id;
$$;

-- Cleanup expired challenges (run periodically)
CREATE OR REPLACE FUNCTION passkey_cleanup_challenges()
RETURNS int LANGUAGE sql AS $$
    WITH deleted AS (
        DELETE FROM passkey_challenges WHERE expires_at < now()
        RETURNING 1
    )
    SELECT count(*)::int FROM deleted;
$$;
```

---

## API Endpoints

### POST `/api/passkey/register/options`

Get WebAuthn creation options for registering a new passkey.

**Authentication**: Required if `AllowStandaloneRegistration` is `false`

**Request Body** (for standalone registration):
```json
{
    "userName": "john@example.com",
    "userDisplayName": "John Doe"
}
```

**Response**:
```json
{
    "challenge": "base64url-encoded-challenge",
    "rp": {
        "id": "example.com",
        "name": "My Application"
    },
    "user": {
        "id": "base64url-encoded-user-handle",
        "name": "john@example.com",
        "displayName": "John Doe"
    },
    "pubKeyCredParams": [
        { "type": "public-key", "alg": -7 },
        { "type": "public-key", "alg": -257 }
    ],
    "timeout": 60000,
    "attestation": "none",
    "authenticatorSelection": {
        "residentKey": "preferred",
        "userVerification": "preferred"
    },
    "excludeCredentials": [],
    "challengeId": "uuid-for-verification"
}
```

---

### POST `/api/passkey/register`

Complete passkey registration with attestation response.

**Request Body**:
```json
{
    "challengeId": "uuid-from-options",
    "credentialId": "base64url-encoded-credential-id",
    "attestationObject": "base64url-encoded-attestation-object",
    "clientDataJSON": "base64url-encoded-client-data",
    "transports": ["internal", "hybrid"],
    "deviceName": "MacBook Pro Touch ID"
}
```

**Response**:
```json
{
    "success": true,
    "credentialId": "base64url-encoded-credential-id"
}
```

---

### POST `/api/passkey/login/options`

Get WebAuthn request options for authentication.

**Request Body** (optional):
```json
{
    "userName": "john@example.com"
}
```

**Response**:
```json
{
    "challenge": "base64url-encoded-challenge",
    "rpId": "example.com",
    "timeout": 60000,
    "userVerification": "preferred",
    "allowCredentials": [
        {
            "type": "public-key",
            "id": "base64url-encoded-credential-id",
            "transports": ["internal", "hybrid"]
        }
    ],
    "challengeId": "uuid-for-verification"
}
```

---

### POST `/api/passkey/login`

Complete passkey authentication with assertion response.

**Request Body**:
```json
{
    "challengeId": "uuid-from-options",
    "credentialId": "base64url-encoded-credential-id",
    "authenticatorData": "base64url-encoded-authenticator-data",
    "clientDataJSON": "base64url-encoded-client-data",
    "signature": "base64url-encoded-signature",
    "userHandle": "base64url-encoded-user-handle"
}
```

**Response** (with JWT auth enabled):
```json
{
    "accessToken": "eyJ...",
    "refreshToken": "eyJ...",
    "tokenType": "Bearer",
    "expiresIn": 3600,
    "refreshExpiresIn": 604800
}
```

---

## Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Enabled` | bool | `false` | Enable passkey authentication |
| `RelyingPartyId` | string | `null` | RP ID (domain). Auto-detected from request if null |
| `RelyingPartyName` | string | `"NpgsqlRest Application"` | Human-readable RP name |
| `RelyingPartyOrigins` | string[] | `[]` | Allowed origins for validation |
| `AllowStandaloneRegistration` | bool | `true` | Allow registration without existing auth |
| `RegistrationOptionsPath` | string | `"/api/passkey/register/options"` | Registration options endpoint |
| `RegisterPath` | string | `"/api/passkey/register"` | Registration completion endpoint |
| `AuthenticationOptionsPath` | string | `"/api/passkey/login/options"` | Authentication options endpoint |
| `AuthenticatePath` | string | `"/api/passkey/login"` | Authentication completion endpoint |
| `ChallengeTimeoutMinutes` | int | `5` | Challenge expiration time |
| `UserVerificationRequirement` | string | `"preferred"` | UV requirement: preferred/required/discouraged |
| `ResidentKeyRequirement` | string | `"preferred"` | Resident key: preferred/required/discouraged |
| `AttestationConveyance` | string | `"none"` | Attestation: none/indirect/direct |

---

## Security Considerations

1. **Challenge Storage**: Challenges are stored in database with TTL and consumed after use
2. **Replay Protection**: Sign count validation prevents credential cloning attacks
3. **Origin Validation**: Strict origin checking against configured `RelyingPartyOrigins`
4. **HTTPS Requirement**: Log warning if HTTPS is not enabled in production
5. **User Verification**: Configurable UV requirement for different security levels
6. **Algorithm Support**: ES256 and RS256 (COSE -7 and -257)

---

## Testing Strategy

### Manual Testing

1. Enable passkey auth in `appsettings.json`
2. Create database tables and functions
3. Register user and add passkey
4. Sign out and authenticate with passkey
5. Verify JWT tokens returned

### Unit Tests

- CBOR decoding tests
- Attestation parsing tests
- ES256/RS256 signature verification
- Sign count validation
- Origin verification

### AOT Compatibility

- Build with `dotnet publish -c Release -p:PublishAot=true`
- Verify JSON serialization uses source generators
- No reflection-based operations

---

## Example Configurations

### Secondary Auth Only

```json
{
    "Auth": {
        "JwtAuth": true,
        "JwtSecret": "your-32-char-secret-key-here!!!",
        "PasskeyAuth": {
            "Enabled": true,
            "RelyingPartyId": "example.com",
            "RelyingPartyName": "My Application",
            "RelyingPartyOrigins": ["https://example.com"],
            "AllowStandaloneRegistration": false
        }
    }
}
```

### Standalone Passwordless

```json
{
    "Auth": {
        "JwtAuth": true,
        "JwtSecret": "your-32-char-secret-key-here!!!",
        "PasskeyAuth": {
            "Enabled": true,
            "RelyingPartyId": "example.com",
            "RelyingPartyName": "My Application",
            "RelyingPartyOrigins": ["https://example.com"],
            "AllowStandaloneRegistration": true,
            "ResidentKeyRequirement": "required",
            "UserVerificationRequirement": "required"
        }
    }
}
```
