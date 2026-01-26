# Passkey Authentication Flow Analysis

This document analyzes the `HandleAuthenticationAsync` method in [PasskeyAuth.cs](../NpgsqlRestClient/Fido2/PasskeyAuth.cs) to understand the database calls and identify potential optimizations.

## Current Flow Overview

The `HandleAuthenticationAsync` method handles the passkey login completion. When a user authenticates with their passkey, the browser sends an assertion response that needs to be validated.

### Step-by-Step Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        HandleAuthenticationAsync                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  1. Parse JSON request body                                                 │
│     └── No DB call                                                          │
│                                                                             │
│  2. Decode base64url inputs (credentialId, authenticatorData, etc.)         │
│     └── No DB call                                                          │
│                                                                             │
│  3. Open database connection                                                │
│     └── Connection opened                                                   │
│                                                                             │
├─────────────────────────────────────────────────────────────────────────────┤
│  DB CALL #1: ChallengeVerifyCommand                                         │
│  ─────────────────────────────────────────────────────────────────────────  │
│  Command: config.ChallengeVerifyCommand                                     │
│  Default: "select * from passkey_verify_challenge($1,$2)"                   │
│  Parameters:                                                                │
│    $1 = challengeId (uuid)                                                  │
│    $2 = "authentication" (operation type)                                   │
│  Returns: challenge bytes (bytea), or NULL if not found/expired             │
│  Purpose: Verify and consume the challenge (prevents replay attacks)        │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  4. If challenge not found/expired → return error                           │
│                                                                             │
├─────────────────────────────────────────────────────────────────────────────┤
│  DB CALL #2: CredentialGetCommand                                           │
│  ─────────────────────────────────────────────────────────────────────────  │
│  Command: config.CredentialGetCommand                                       │
│  Default: "select * from passkey_get_credential($1)"                        │
│  Parameters:                                                                │
│    $1 = credentialId (bytea)                                                │
│  Returns:                                                                   │
│    - user_id (text)                                                         │
│    - public_key (bytea)                                                     │
│    - public_key_algorithm (int)                                             │
│    - sign_count (bigint)                                                    │
│  Purpose: Get the stored credential for signature verification              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  5. If credential not found → return error                                  │
│                                                                             │
│  6. Validate assertion (in-memory cryptographic verification)               │
│     - Verify clientDataJSON (type, challenge, origin)                       │
│     - Verify authenticator data (rpIdHash, user present flag)               │
│     - Verify sign count (replay protection)                                 │
│     - Verify signature using stored public key                              │
│     └── No DB call                                                          │
│                                                                             │
│  7. If validation fails → return error                                      │
│                                                                             │
├─────────────────────────────────────────────────────────────────────────────┤
│  DB CALL #3: SignCountUpdateCommand                                         │
│  ─────────────────────────────────────────────────────────────────────────  │
│  Command: config.SignCountUpdateCommand                                     │
│  Default: "select passkey_update_sign_count($1,$2)"                         │
│  Parameters:                                                                │
│    $1 = credentialId (bytea)                                                │
│    $2 = newSignCount (bigint)                                               │
│  Returns: Nothing meaningful                                                │
│  Purpose: Update the signature counter for cloned authenticator detection   │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
├─────────────────────────────────────────────────────────────────────────────┤
│  DB CALL #4: LoginCommand (via LoginHandler.HandleAsync)                    │
│  ─────────────────────────────────────────────────────────────────────────  │
│  Command: config.LoginCommand                                               │
│  Default: "select * from passkey_login($1)"                                 │
│  Parameters:                                                                │
│    $1 = userId (text)                                                       │
│  Returns:                                                                   │
│    - status (int)                                                           │
│    - user_id, user_name, user_roles, etc.                                   │
│    - Additional columns become claims                                       │
│  Purpose: Get user claims for JWT token generation                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  8. LoginHandler builds ClaimsPrincipal from returned columns               │
│  9. LoginHandler calls SignIn to generate JWT/Cookie                        │
│                                                                             │
│  POTENTIAL ADDITIONAL DB CALLS (inside LoginHandler):                       │
│  ─────────────────────────────────────────────────────────────────────────  │
│  These are skipped for passkey auth (performHashVerification: false):       │
│  - PasswordVerificationFailedCommand                                        │
│  - PasswordVerificationSucceededCommand                                     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Summary of Database Calls

| # | Command | Default SQL | Purpose |
|---|---------|-------------|---------|
| 1 | `ChallengeVerifyCommand` | `select * from passkey_verify_challenge($1,$2)` | Verify & consume challenge |
| 2 | `CredentialGetCommand` | `select * from passkey_get_credential($1)` | Get stored credential |
| 3 | `SignCountUpdateCommand` | `select passkey_update_sign_count($1,$2)` | Update sign counter |
| 4 | `LoginCommand` | `select * from passkey_login($1)` | Get user claims |

**Total: 4 database round-trips for a single passkey login**

## Analysis: Why So Many Calls?

### Separation of Concerns (Current Design)

1. **Challenge Management** - Separate table/function for challenge lifecycle
2. **Credential Storage** - Separate table/function for credential data
3. **Sign Count Tracking** - Separate update for security counter
4. **User Claims** - Reuse existing login infrastructure

### The Problem

The design follows a "pure" separation of concerns, but results in:
- **4 network round-trips** to the database
- **4 separate transactions** (unless wrapped)
- Higher latency for authentication
- More connection overhead

## Potential Optimizations

### Option A: Combine Into Single Function

Create a single PostgreSQL function that does everything:

```sql
CREATE OR REPLACE FUNCTION passkey_authenticate(
    p_challenge_id uuid,
    p_credential_id bytea,
    p_new_sign_count bigint
) RETURNS TABLE (
    status int,
    challenge bytea,
    user_id text,
    user_name text,
    user_roles text,
    public_key bytea,
    public_key_algorithm int,
    stored_sign_count bigint
    -- additional claim columns...
) AS $$
DECLARE
    v_challenge bytea;
    v_user_id text;
    v_stored_sign_count bigint;
BEGIN
    -- 1. Verify and consume challenge
    DELETE FROM passkey_challenges
    WHERE id = p_challenge_id
      AND operation = 'authentication'
      AND expires_at > now()
    RETURNING challenge INTO v_challenge;

    IF v_challenge IS NULL THEN
        RETURN QUERY SELECT 400, NULL::bytea, NULL::text, NULL::text,
                            NULL::text, NULL::bytea, NULL::int, NULL::bigint;
        RETURN;
    END IF;

    -- 2. Get credential and update sign count atomically
    UPDATE passkeys
    SET sign_count = p_new_sign_count
    WHERE credential_id = p_credential_id
    RETURNING user_id, sign_count INTO v_user_id, v_stored_sign_count;

    IF v_user_id IS NULL THEN
        RETURN QUERY SELECT 401, v_challenge, NULL::text, NULL::text,
                            NULL::text, NULL::bytea, NULL::int, NULL::bigint;
        RETURN;
    END IF;

    -- 3. Return everything needed including user claims
    RETURN QUERY
    SELECT
        200,
        v_challenge,
        u.id::text,
        u.username,
        u.roles,
        p.public_key,
        p.public_key_algorithm,
        v_stored_sign_count
    FROM users u
    JOIN passkeys p ON p.user_id = u.id
    WHERE p.credential_id = p_credential_id;
END;
$$ LANGUAGE plpgsql;
```

**Benefits:**
- Single database round-trip
- Atomic operation (all or nothing)
- Less network latency
- Simpler code path

**Drawbacks:**
- More complex SQL function
- Less modular/reusable
- Harder to customize individual steps
- Sign count update happens BEFORE signature verification (security concern!)

### Option B: Two-Phase Approach

Split into 2 calls instead of 4:

**Phase 1: Get everything needed for verification**
```sql
-- passkey_get_auth_data($1, $2) returns:
-- challenge, public_key, algorithm, stored_sign_count, user_id
SELECT * FROM passkey_get_auth_data(p_challenge_id, p_credential_id);
```

**Phase 2: After successful verification, commit the authentication**
```sql
-- passkey_complete_auth($1, $2, $3) returns:
-- Deletes challenge, updates sign count, returns user claims
SELECT * FROM passkey_complete_auth(p_credential_id, p_new_sign_count, p_user_id);
```

**Benefits:**
- 2 round-trips instead of 4
- Sign count only updated after successful verification
- Still maintains some separation
- Claims returned with completion

### Option C: Keep Current Design

The current 4-call design has these advantages:
- Maximum flexibility - each function can be customized independently
- Standard PostgreSQL functions - easy to understand and maintain
- Reuses existing `LoginHandler` - consistent with other auth methods
- Clear audit trail - each step is a separate operation

**The 4 calls may be acceptable if:**
- Network latency to database is very low (same machine/local network)
- Authentication frequency is not extremely high
- Flexibility/maintainability is prioritized over raw performance

## Recommendations

### For Maximum Performance: Option A or B

If passkey authentication performance is critical:
1. Implement a combined function (Option A) or two-phase approach (Option B)
2. Update `HandleAuthenticationAsync` to use the new function(s)
3. Bypass `LoginHandler.HandleAsync` and handle claims directly

### For Balance: Hybrid Approach

Keep the current structure but allow users to provide a combined function:

```csharp
// In PasskeyConfig
public string? CombinedAuthenticationCommand { get; set; }
// If set, uses single-call approach
// If null, uses current 4-call approach
```

### For Current Design: Accept Trade-off

The current design prioritizes:
- **Modularity** over performance
- **Consistency** with other auth handlers
- **Flexibility** for custom implementations

If these are more important than shaving off a few milliseconds, the current design is reasonable.

## Questions for Decision

1. **What is the typical network latency to the database?**
   - If < 1ms (local), 4 calls add ~4ms overhead
   - If 10ms (remote), 4 calls add ~40ms overhead

2. **What is the expected authentication frequency?**
   - Occasional logins: current design is fine
   - High-frequency API authentication: optimize

3. **How important is customization of individual steps?**
   - Very important: keep current design
   - Not important: combine into fewer calls

4. **Is transactional atomicity required?**
   - Current design: each call is separate transaction
   - Combined: single atomic transaction

---

## Visual Comparison

### Current Flow (4 DB calls)
```
Client → API → DB (verify challenge)
              → DB (get credential)
              → [Verify signature in memory]
              → DB (update sign count)
              → DB (get user claims)
              → Client (JWT token)
```

### Optimized Flow (1-2 DB calls)
```
Client → API → DB (get all auth data)
              → [Verify signature in memory]
              → DB (commit auth + get claims)
              → Client (JWT token)
```

### Latency Comparison (assuming 5ms per DB call)

| Approach | DB Calls | DB Latency | Total Overhead |
|----------|----------|------------|----------------|
| Current | 4 | 20ms | Higher |
| Two-phase | 2 | 10ms | Medium |
| Single call | 1 | 5ms | Lowest |

*Note: Actual latency depends on network conditions and database performance.*
