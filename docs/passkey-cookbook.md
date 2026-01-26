# Passkey Authentication Cookbook

A step-by-step guide to implement and test WebAuthn/FIDO2 passkey authentication in your application.

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Database Setup](#database-setup)
3. [Configuration](#configuration)
4. [TypeScript Client](#typescript-client)
5. [Testing the Implementation](#testing-the-implementation)
6. [Troubleshooting](#troubleshooting)

---

## Prerequisites

- PostgreSQL 14+ with `pgcrypto` extension
- NpgsqlRestClient with passkey authentication enabled
- Modern browser with WebAuthn support (Chrome, Firefox, Safari, Edge)
- HTTPS connection (required for WebAuthn in production; localhost works for development)

---

## Database Setup

### Step 1: Create the Tables

```sql
-- Enable required extension
create extension if not exists pgcrypto;

-- Users table (adapt to your existing schema)
create table if not exists users (
    id text primary key,
    username text unique not null,
    display_name text,
    roles text,  -- comma-separated roles or use a separate table
    created_at timestamptz default now()
);

-- Passkeys table
create table if not exists passkeys (
    credential_id bytea primary key,
    user_id text not null references users(id) on delete cascade,
    user_handle bytea not null,
    public_key bytea not null,
    public_key_algorithm int not null,
    sign_count bigint not null default 0,
    transports text[],
    backup_eligible boolean default false,
    device_name text,
    created_at timestamptz default now(),
    last_used_at timestamptz
);

-- Challenge storage (for replay protection)
create table if not exists passkey_challenges (
    id uuid primary key default gen_random_uuid(),
    challenge bytea not null,
    user_id text,  -- null for authentication, set for registration
    operation text not null check (operation in ('registration', 'authentication')),
    expires_at timestamptz not null,
    created_at timestamptz default now()
);

-- Authentication audit log (optional, for analytics)
create table if not exists auth_audit_log (
    id bigserial primary key,
    user_id text not null,
    event_type text not null,
    analytics_data json,
    ip_address text,
    created_at timestamptz default now()
);

-- Indexes
create index idx_passkeys_user_id on passkeys(user_id);
create index idx_passkey_challenges_expires on passkey_challenges(expires_at);
create index idx_auth_audit_log_user_id on auth_audit_log(user_id);
create index idx_auth_audit_log_created_at on auth_audit_log(created_at);
```

### Step 2: Create the Functions

#### Registration Functions

```sql
-- 1. Get credential creation options for registration
create or replace function passkey_creation_options(p_user_id text)
returns table (
    status int,
    message text,
    challenge text,
    user_id text,
    user_name text,
    user_display_name text,
    user_handle text,
    exclude_credentials text,
    challenge_id uuid
) as $$
declare
    v_user record;
    v_challenge bytea;
    v_challenge_id uuid;
    v_user_handle bytea;
    v_exclude_creds jsonb;
begin
    -- Get or create user
    select u.id, u.username, u.display_name
    into v_user
    from users u
    where u.id = p_user_id or u.username = p_user_id;

    if v_user is null then
        -- For standalone registration, create a new user
        v_user_handle := gen_random_bytes(32);
        insert into users (id, username, display_name)
        values (p_user_id, p_user_id, p_user_id)
        returning users.id, users.username, users.display_name into v_user;
    else
        -- Get existing user handle from their first passkey, or generate new
        select p.user_handle into v_user_handle
        from passkeys p where p.user_id = v_user.id limit 1;

        if v_user_handle is null then
            v_user_handle := gen_random_bytes(32);
        end if;
    end if;

    -- Generate challenge
    v_challenge := gen_random_bytes(32);

    -- Store challenge
    insert into passkey_challenges (challenge, user_id, operation, expires_at)
    values (v_challenge, v_user.id, 'registration', now() + interval '5 minutes')
    returning id into v_challenge_id;

    -- Get existing credentials to exclude (prevent re-registration)
    select jsonb_agg(jsonb_build_object(
        'type', 'public-key',
        'id', encode(pk.credential_id, 'base64'),
        'transports', pk.transports
    ))
    into v_exclude_creds
    from passkeys pk
    where pk.user_id = v_user.id;

    return query select
        200,
        'OK'::text,
        encode(v_challenge, 'base64'),
        v_user.id,
        v_user.username,
        coalesce(v_user.display_name, v_user.username),
        encode(v_user_handle, 'base64'),
        coalesce(v_exclude_creds::text, '[]'),
        v_challenge_id;
end;
$$ language plpgsql;

-- 2. Verify and consume a challenge
create or replace function passkey_verify_challenge(
    p_challenge_id uuid,
    p_operation text
) returns bytea as $$
declare
    v_challenge bytea;
begin
    -- Delete and return the challenge (atomic consume)
    delete from passkey_challenges
    where id = p_challenge_id
      and operation = p_operation
      and expires_at > now()
    returning challenge into v_challenge;

    return v_challenge;
end;
$$ language plpgsql;

-- 3. Store a new passkey credential
create or replace function passkey_store(
    p_credential_id bytea,
    p_user_id text,
    p_user_handle bytea,
    p_public_key bytea,
    p_algorithm int,
    p_transports text[],
    p_backup_eligible boolean,
    p_device_name text
) returns table (status int, message text) as $$
begin
    -- Check for duplicate credential
    if exists (select 1 from passkeys where credential_id = p_credential_id) then
        return query select 409, 'Credential already registered'::text;
        return;
    end if;

    -- Store the credential
    insert into passkeys (
        credential_id, user_id, user_handle, public_key,
        public_key_algorithm, transports, backup_eligible, device_name
    ) values (
        p_credential_id, p_user_id, p_user_handle, p_public_key,
        p_algorithm, p_transports, p_backup_eligible, p_device_name
    );

    return query select 200, 'Passkey registered successfully'::text;
end;
$$ language plpgsql;
```

#### Authentication Functions

```sql
-- 1. Get credential request options for authentication
create or replace function passkey_request_options(p_user_name text)
returns table (
    status int,
    challenge text,
    allow_credentials text,
    challenge_id uuid
) as $$
declare
    v_challenge bytea;
    v_challenge_id uuid;
    v_allow_creds jsonb;
    v_user_id text;
begin
    -- Generate challenge
    v_challenge := gen_random_bytes(32);

    -- Get user ID if username provided
    if p_user_name is not null then
        select id into v_user_id from users where username = p_user_name;

        if v_user_id is null then
            return query select 404, null::text, null::text, null::uuid;
            return;
        end if;

        -- Get allowed credentials for this user
        select jsonb_agg(jsonb_build_object(
            'type', 'public-key',
            'id', encode(pk.credential_id, 'base64'),
            'transports', pk.transports
        ))
        into v_allow_creds
        from passkeys pk
        where pk.user_id = v_user_id;
    end if;

    -- Store challenge
    insert into passkey_challenges (challenge, user_id, operation, expires_at)
    values (v_challenge, v_user_id, 'authentication', now() + interval '5 minutes')
    returning id into v_challenge_id;

    return query select
        200,
        encode(v_challenge, 'base64'),
        coalesce(v_allow_creds::text, '[]'),
        v_challenge_id;
end;
$$ language plpgsql;

-- 2. Get authentication data (first DB call during login)
create or replace function passkey_authenticate_data(
    p_challenge_id uuid,
    p_credential_id bytea,
    p_operation text
) returns table (
    status int,
    message text,
    challenge bytea,
    user_id text,
    public_key bytea,
    public_key_algorithm int,
    sign_count bigint
) as $$
declare
    v_challenge bytea;
    v_cred record;
begin
    -- Step 1: Verify and consume challenge (atomic)
    delete from passkey_challenges
    where id = p_challenge_id
      and operation = p_operation
      and expires_at > now()
    returning passkey_challenges.challenge into v_challenge;

    if v_challenge is null then
        return query select
            400,
            'Challenge not found or expired'::text,
            null::bytea, null::text, null::bytea, null::int, null::bigint;
        return;
    end if;

    -- Step 2: Get credential data
    select p.user_id, p.public_key, p.public_key_algorithm, p.sign_count
    into v_cred
    from passkeys p
    where p.credential_id = p_credential_id;

    if v_cred is null then
        return query select
            401,
            'Credential not found'::text,
            v_challenge, null::text, null::bytea, null::int, null::bigint;
        return;
    end if;

    -- Return all data needed for verification
    return query select
        200,
        'OK'::text,
        v_challenge,
        v_cred.user_id,
        v_cred.public_key,
        v_cred.public_key_algorithm,
        v_cred.sign_count;
end;
$$ language plpgsql;

-- 3. Complete authentication (second DB call during login)
create or replace function passkey_authenticate_complete(
    p_credential_id bytea,
    p_new_sign_count bigint,
    p_user_id text,
    p_analytics_data json default null
) returns table (
    status int,
    user_id text,
    user_name text,
    user_roles text
) as $$
begin
    -- Update sign count and last used timestamp
    if p_new_sign_count > 0 then
        update passkeys
        set sign_count = p_new_sign_count,
            last_used_at = now()
        where credential_id = p_credential_id;
    else
        update passkeys
        set last_used_at = now()
        where credential_id = p_credential_id;
    end if;

    -- Log authentication event with analytics data
    if p_analytics_data is not null then
        insert into auth_audit_log (user_id, event_type, analytics_data, ip_address)
        values (
            p_user_id,
            'passkey_login',
            p_analytics_data,
            p_analytics_data->>'ip'
        );
    end if;

    -- Return user claims for JWT/cookie authentication
    return query
    select
        200,
        u.id,
        u.username,
        u.roles
    from users u
    where u.id = p_user_id;
end;
$$ language plpgsql;
```

#### Utility Functions

```sql
-- Cleanup expired challenges (run periodically)
create or replace function cleanup_expired_challenges()
returns int as $$
declare
    deleted_count int;
begin
    delete from passkey_challenges where expires_at < now();
    get diagnostics deleted_count = row_count;
    return deleted_count;
end;
$$ language plpgsql;

-- Get user's registered passkeys
create or replace function get_user_passkeys(p_user_id text)
returns table (
    credential_id text,
    device_name text,
    created_at timestamptz,
    last_used_at timestamptz,
    backup_eligible boolean
) as $$
begin
    return query
    select
        encode(p.credential_id, 'base64'),
        p.device_name,
        p.created_at,
        p.last_used_at,
        p.backup_eligible
    from passkeys p
    where p.user_id = p_user_id
    order by p.created_at desc;
end;
$$ language plpgsql;

-- Delete a passkey
create or replace function delete_user_passkey(p_user_id text, p_credential_id text)
returns table (status int, message text) as $$
declare
    v_cred_bytes bytea;
begin
    v_cred_bytes := decode(p_credential_id, 'base64');

    delete from passkeys
    where user_id = p_user_id and credential_id = v_cred_bytes;

    if found then
        return query select 200, 'Passkey deleted'::text;
    else
        return query select 404, 'Passkey not found'::text;
    end if;
end;
$$ language plpgsql;
```

---

## Configuration

### appsettings.json

```json
{
  "Auth": {
    "JwtAuth": true,
    "JwtSecret": "your-secret-key-at-least-32-characters-long-for-security",
    "JwtIssuer": "your-app",
    "JwtAudience": "your-app",
    "JwtExpireMinutes": 60,

    "PasskeyAuth": {
      "Enabled": true,
      "RelyingPartyId": "localhost",
      "RelyingPartyName": "My Application",
      "RelyingPartyOrigins": ["http://localhost:5000", "https://localhost:5001"],
      "AllowStandaloneRegistration": true,
      "UserVerificationRequirement": "preferred",
      "ResidentKeyRequirement": "preferred",
      "ValidateSignCount": true,
      "ChallengeTimeoutMinutes": 5,

      "RegistrationOptionsPath": "/api/passkey/register/options",
      "RegisterPath": "/api/passkey/register",
      "AuthenticationOptionsPath": "/api/passkey/login/options",
      "AuthenticatePath": "/api/passkey/login",

      "CredentialCreationOptionsCommand": "select * from passkey_creation_options($1)",
      "CredentialStoreCommand": "select * from passkey_store($1,$2,$3,$4,$5,$6,$7,$8)",
      "CredentialRequestOptionsCommand": "select * from passkey_request_options($1)",
      "ChallengeVerifyCommand": "select * from passkey_verify_challenge($1,$2)",
      "AuthenticateDataCommand": "select * from passkey_authenticate_data($1,$2,$3)",
      "AuthenticateCompleteCommand": "select * from passkey_authenticate_complete($1,$2,$3,$4)",

      "ClientAnalyticsIpKey": "ip"
    }
  }
}
```

### Configuration Options Reference

| Option | Default | Description |
|--------|---------|-------------|
| `Enabled` | `false` | Enable passkey authentication |
| `RelyingPartyId` | auto-detect | Domain name (e.g., "example.com") |
| `RelyingPartyName` | app name | Display name shown to users |
| `RelyingPartyOrigins` | `[]` | Allowed origins for validation |
| `AllowStandaloneRegistration` | `true` | Allow registration without existing auth |
| `UserVerificationRequirement` | `"preferred"` | `"preferred"`, `"required"`, or `"discouraged"` |
| `ResidentKeyRequirement` | `"preferred"` | `"preferred"`, `"required"`, or `"discouraged"` |
| `ValidateSignCount` | `true` | Enable clone detection via sign count |
| `ChallengeTimeoutMinutes` | `5` | Challenge expiration time |
| `ClientAnalyticsIpKey` | `"ip"` | JSON key for IP address (null to disable) |

---

## TypeScript Client

Create a `passkey.ts` module for your frontend:

```typescript
// passkey.ts - WebAuthn/Passkey Client Module

/**
 * Configuration for the passkey client
 */
export interface PasskeyConfig {
  registrationOptionsPath: string;
  registerPath: string;
  authenticationOptionsPath: string;
  authenticatePath: string;
  analyticsData?: () => object | null;
}

const defaultConfig: PasskeyConfig = {
  registrationOptionsPath: '/api/passkey/register/options',
  registerPath: '/api/passkey/register',
  authenticationOptionsPath: '/api/passkey/login/options',
  authenticatePath: '/api/passkey/login',
  analyticsData: () => ({
    timestamp: new Date().toISOString(),
    timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
    screen: {
      width: window.screen.width,
      height: window.screen.height,
      colorDepth: window.screen.colorDepth,
      pixelRatio: window.devicePixelRatio,
      orientation: (screen.orientation as any)?.type
    },
    browser: {
      userAgent: navigator.userAgent,
      language: navigator.language,
      languages: navigator.languages,
      cookiesEnabled: navigator.cookieEnabled,
      doNotTrack: navigator.doNotTrack,
      onLine: navigator.onLine,
      platform: navigator.platform,
      vendor: navigator.vendor
    },
    memory: {
      deviceMemory: (navigator as any).deviceMemory,
      hardwareConcurrency: navigator.hardwareConcurrency
    },
    window: {
      innerWidth: window.innerWidth,
      innerHeight: window.innerHeight,
      outerWidth: window.outerWidth,
      outerHeight: window.outerHeight
    },
    location: {
      href: window.location.href,
      hostname: window.location.hostname,
      pathname: window.location.pathname,
      protocol: window.location.protocol,
      referrer: document.referrer
    },
    performance: {
      navigation: {
        type: performance.navigation?.type,
        redirectCount: performance.navigation?.redirectCount
      },
      timing: performance.timing ? {
        loadEventEnd: performance.timing.loadEventEnd,
        loadEventStart: performance.timing.loadEventStart,
        domComplete: performance.timing.domComplete,
        domInteractive: performance.timing.domInteractive,
        domContentLoadedEventEnd: performance.timing.domContentLoadedEventEnd
      } : null
    }
  })
};

let config: PasskeyConfig = { ...defaultConfig };

/**
 * Configure the passkey client
 */
export function configure(options: Partial<PasskeyConfig>): void {
  config = { ...config, ...options };
}

/**
 * Check if WebAuthn is supported in this browser
 */
export function isSupported(): boolean {
  return !!(
    window.PublicKeyCredential &&
    typeof window.PublicKeyCredential === 'function'
  );
}

/**
 * Check if conditional mediation (autofill) is supported
 */
export async function isConditionalMediationSupported(): Promise<boolean> {
  if (!isSupported()) return false;

  try {
    return await PublicKeyCredential.isConditionalMediationAvailable?.() ?? false;
  } catch {
    return false;
  }
}

/**
 * Check if user verifying platform authenticator is available (e.g., Touch ID, Face ID, Windows Hello)
 */
export async function isPlatformAuthenticatorAvailable(): Promise<boolean> {
  if (!isSupported()) return false;

  try {
    return await PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable();
  } catch {
    return false;
  }
}

// ============================================================================
// Base64URL Encoding/Decoding
// ============================================================================

/**
 * Decode a base64url string to an ArrayBuffer
 */
export function base64UrlToBuffer(base64url: string): ArrayBuffer {
  // Replace base64url chars with base64 chars
  let base64 = base64url.replace(/-/g, '+').replace(/_/g, '/');

  // Pad with '=' to make length a multiple of 4
  while (base64.length % 4 !== 0) {
    base64 += '=';
  }

  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes.buffer;
}

/**
 * Encode an ArrayBuffer to a base64url string
 */
export function bufferToBase64Url(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = '';
  for (let i = 0; i < bytes.length; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  const base64 = btoa(binary);

  // Convert base64 to base64url
  return base64.replace(/\+/g, '-').replace(/\//g, '_').replace(/=/g, '');
}

// ============================================================================
// Registration (Creating a new passkey)
// ============================================================================

export interface RegistrationOptions {
  userName: string;
  userDisplayName?: string;
  deviceName?: string;
}

export interface RegistrationResult {
  success: boolean;
  credentialId?: string;
  error?: string;
}

/**
 * Register a new passkey for a user
 */
export async function register(options: RegistrationOptions): Promise<RegistrationResult> {
  if (!isSupported()) {
    return { success: false, error: 'WebAuthn is not supported in this browser' };
  }

  try {
    // Step 1: Get creation options from server
    const optionsResponse = await fetch(config.registrationOptionsPath, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        userName: options.userName,
        userDisplayName: options.userDisplayName
      })
    });

    if (!optionsResponse.ok) {
      const error = await optionsResponse.json();
      return { success: false, error: error.errorDescription || 'Failed to get registration options' };
    }

    const creationOptions = await optionsResponse.json();

    // Step 2: Create credential using browser WebAuthn API
    const publicKeyOptions: PublicKeyCredentialCreationOptions = {
      challenge: base64UrlToBuffer(creationOptions.challenge),
      rp: {
        id: creationOptions.rp.id,
        name: creationOptions.rp.name
      },
      user: {
        id: base64UrlToBuffer(creationOptions.user.id),
        name: creationOptions.user.name,
        displayName: creationOptions.user.displayName
      },
      pubKeyCredParams: creationOptions.pubKeyCredParams,
      timeout: creationOptions.timeout,
      attestation: creationOptions.attestation as AttestationConveyancePreference,
      authenticatorSelection: creationOptions.authenticatorSelection ? {
        residentKey: creationOptions.authenticatorSelection.residentKey as ResidentKeyRequirement,
        userVerification: creationOptions.authenticatorSelection.userVerification as UserVerificationRequirement,
        authenticatorAttachment: creationOptions.authenticatorSelection.authenticatorAttachment as AuthenticatorAttachment
      } : undefined,
      excludeCredentials: creationOptions.excludeCredentials?.map((cred: any) => ({
        type: cred.type as PublicKeyCredentialType,
        id: base64UrlToBuffer(cred.id),
        transports: cred.transports as AuthenticatorTransport[]
      }))
    };

    const credential = await navigator.credentials.create({
      publicKey: publicKeyOptions
    }) as PublicKeyCredential;

    if (!credential) {
      return { success: false, error: 'Failed to create credential' };
    }

    const attestationResponse = credential.response as AuthenticatorAttestationResponse;

    // Step 3: Send attestation to server for verification and storage
    const registerResponse = await fetch(config.registerPath, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        challengeId: creationOptions.challengeId,
        credentialId: bufferToBase64Url(credential.rawId),
        attestationObject: bufferToBase64Url(attestationResponse.attestationObject),
        clientDataJSON: bufferToBase64Url(attestationResponse.clientDataJSON),
        transports: attestationResponse.getTransports?.() || [],
        deviceName: options.deviceName,
        userName: options.userName,
        userDisplayName: options.userDisplayName
      })
    });

    if (!registerResponse.ok) {
      const error = await registerResponse.json();
      return { success: false, error: error.errorDescription || 'Registration failed' };
    }

    const result = await registerResponse.json();
    return { success: true, credentialId: result.credentialId };

  } catch (error: any) {
    // Handle specific WebAuthn errors
    if (error.name === 'NotAllowedError') {
      return { success: false, error: 'Registration was cancelled or timed out' };
    }
    if (error.name === 'InvalidStateError') {
      return { success: false, error: 'This authenticator is already registered' };
    }
    return { success: false, error: error.message || 'Unknown error during registration' };
  }
}

// ============================================================================
// Authentication (Signing in with a passkey)
// ============================================================================

export interface AuthenticationOptions {
  userName?: string;  // Optional: for non-discoverable credentials
}

export interface AuthenticationResult {
  success: boolean;
  token?: string;
  userId?: string;
  userName?: string;
  error?: string;
}

/**
 * Authenticate with a passkey
 */
export async function authenticate(options: AuthenticationOptions = {}): Promise<AuthenticationResult> {
  if (!isSupported()) {
    return { success: false, error: 'WebAuthn is not supported in this browser' };
  }

  try {
    // Step 1: Get authentication options from server
    const optionsResponse = await fetch(config.authenticationOptionsPath, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        userName: options.userName || null
      })
    });

    if (!optionsResponse.ok) {
      const error = await optionsResponse.json();
      return { success: false, error: error.errorDescription || 'Failed to get authentication options' };
    }

    const requestOptions = await optionsResponse.json();

    // Step 2: Get assertion using browser WebAuthn API
    const publicKeyOptions: PublicKeyCredentialRequestOptions = {
      challenge: base64UrlToBuffer(requestOptions.challenge),
      rpId: requestOptions.rpId,
      timeout: requestOptions.timeout,
      userVerification: requestOptions.userVerification as UserVerificationRequirement,
      allowCredentials: requestOptions.allowCredentials?.length > 0
        ? requestOptions.allowCredentials.map((cred: any) => ({
            type: cred.type as PublicKeyCredentialType,
            id: base64UrlToBuffer(cred.id),
            transports: cred.transports as AuthenticatorTransport[]
          }))
        : undefined
    };

    const credential = await navigator.credentials.get({
      publicKey: publicKeyOptions
    }) as PublicKeyCredential;

    if (!credential) {
      return { success: false, error: 'Failed to get credential' };
    }

    const assertionResponse = credential.response as AuthenticatorAssertionResponse;

    // Step 3: Collect analytics data
    const analyticsData = config.analyticsData?.();

    // Step 4: Send assertion to server for verification
    const authResponse = await fetch(config.authenticatePath, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        challengeId: requestOptions.challengeId,
        credentialId: bufferToBase64Url(credential.rawId),
        authenticatorData: bufferToBase64Url(assertionResponse.authenticatorData),
        clientDataJSON: bufferToBase64Url(assertionResponse.clientDataJSON),
        signature: bufferToBase64Url(assertionResponse.signature),
        userHandle: assertionResponse.userHandle
          ? bufferToBase64Url(assertionResponse.userHandle)
          : undefined,
        analyticsData: analyticsData ? JSON.stringify(analyticsData) : undefined
      })
    });

    if (!authResponse.ok) {
      const error = await authResponse.json();
      return { success: false, error: error.errorDescription || 'Authentication failed' };
    }

    // Parse response - could be JWT token or session info
    const responseText = await authResponse.text();

    // Try to parse as JSON first
    try {
      const result = JSON.parse(responseText);
      return {
        success: true,
        token: result.token || result.access_token,
        userId: result.user_id || result.userId,
        userName: result.user_name || result.userName
      };
    } catch {
      // If not JSON, assume it's a plain token
      return { success: true, token: responseText };
    }

  } catch (error: any) {
    if (error.name === 'NotAllowedError') {
      return { success: false, error: 'Authentication was cancelled or timed out' };
    }
    return { success: false, error: error.message || 'Unknown error during authentication' };
  }
}

// ============================================================================
// Conditional UI (Autofill) - Experimental
// ============================================================================

/**
 * Start conditional mediation (autofill) authentication
 * Call this on page load for login pages with autocomplete="webauthn"
 */
export async function startConditionalAuthentication(
  onSuccess: (result: AuthenticationResult) => void,
  onError?: (error: string) => void
): Promise<AbortController | null> {
  if (!await isConditionalMediationSupported()) {
    onError?.('Conditional mediation is not supported');
    return null;
  }

  const abortController = new AbortController();

  try {
    // Get options without username for discoverable credentials
    const optionsResponse = await fetch(config.authenticationOptionsPath, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ userName: null })
    });

    if (!optionsResponse.ok) {
      throw new Error('Failed to get authentication options');
    }

    const requestOptions = await optionsResponse.json();

    const publicKeyOptions: PublicKeyCredentialRequestOptions = {
      challenge: base64UrlToBuffer(requestOptions.challenge),
      rpId: requestOptions.rpId,
      timeout: requestOptions.timeout,
      userVerification: requestOptions.userVerification as UserVerificationRequirement
    };

    // This will wait for the user to select a passkey from autofill
    const credential = await navigator.credentials.get({
      publicKey: publicKeyOptions,
      mediation: 'conditional',
      signal: abortController.signal
    } as any) as PublicKeyCredential;

    if (!credential) {
      throw new Error('No credential returned');
    }

    const assertionResponse = credential.response as AuthenticatorAssertionResponse;
    const analyticsData = config.analyticsData?.();

    const authResponse = await fetch(config.authenticatePath, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        challengeId: requestOptions.challengeId,
        credentialId: bufferToBase64Url(credential.rawId),
        authenticatorData: bufferToBase64Url(assertionResponse.authenticatorData),
        clientDataJSON: bufferToBase64Url(assertionResponse.clientDataJSON),
        signature: bufferToBase64Url(assertionResponse.signature),
        userHandle: assertionResponse.userHandle
          ? bufferToBase64Url(assertionResponse.userHandle)
          : undefined,
        analyticsData: analyticsData ? JSON.stringify(analyticsData) : undefined
      })
    });

    if (!authResponse.ok) {
      const error = await authResponse.json();
      throw new Error(error.errorDescription || 'Authentication failed');
    }

    const responseText = await authResponse.text();
    try {
      const result = JSON.parse(responseText);
      onSuccess({
        success: true,
        token: result.token || result.access_token,
        userId: result.user_id || result.userId,
        userName: result.user_name || result.userName
      });
    } catch {
      onSuccess({ success: true, token: responseText });
    }

  } catch (error: any) {
    if (error.name !== 'AbortError') {
      onError?.(error.message || 'Conditional authentication failed');
    }
  }

  return abortController;
}

// ============================================================================
// Exports
// ============================================================================

export default {
  configure,
  isSupported,
  isConditionalMediationSupported,
  isPlatformAuthenticatorAvailable,
  base64UrlToBuffer,
  bufferToBase64Url,
  register,
  authenticate,
  startConditionalAuthentication
};
```

### Usage Examples

#### Basic Registration

```typescript
import * as passkey from './passkey';

// Check if passkeys are supported
if (!passkey.isSupported()) {
  console.log('Passkeys are not supported in this browser');
  return;
}

// Register a new passkey
const result = await passkey.register({
  userName: 'john@example.com',
  userDisplayName: 'John Doe',
  deviceName: 'MacBook Pro'
});

if (result.success) {
  console.log('Passkey registered:', result.credentialId);
} else {
  console.error('Registration failed:', result.error);
}
```

#### Basic Authentication

```typescript
import * as passkey from './passkey';

// Authenticate with passkey (discoverable credential - no username needed)
const result = await passkey.authenticate();

if (result.success) {
  console.log('Authenticated!', result.token);
  // Store token, redirect, etc.
} else {
  console.error('Authentication failed:', result.error);
}

// Or authenticate with username (for non-discoverable credentials)
const result2 = await passkey.authenticate({ userName: 'john@example.com' });
```

#### Autofill Integration (Conditional UI)

```html
<!-- Login form with autofill support -->
<form id="loginForm">
  <input
    type="text"
    name="username"
    autocomplete="username webauthn"
    placeholder="Username or email"
  />
  <button type="submit">Sign In</button>
</form>

<script type="module">
import * as passkey from './passkey.js';

// Start conditional authentication on page load
let abortController = null;

async function initConditionalAuth() {
  abortController = await passkey.startConditionalAuthentication(
    (result) => {
      console.log('Authenticated via autofill!', result);
      // Redirect to dashboard
      window.location.href = '/dashboard';
    },
    (error) => {
      console.log('Conditional auth error:', error);
    }
  );
}

// Cancel conditional auth when form is submitted normally
document.getElementById('loginForm').addEventListener('submit', () => {
  abortController?.abort();
});

initConditionalAuth();
</script>
```

#### React Component Example

```tsx
import { useState, useEffect } from 'react';
import * as passkey from './passkey';

export function PasskeyLogin() {
  const [supported, setSupported] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setSupported(passkey.isSupported());
  }, []);

  const handleLogin = async () => {
    setLoading(true);
    setError(null);

    const result = await passkey.authenticate();

    setLoading(false);

    if (result.success) {
      // Store token and redirect
      localStorage.setItem('token', result.token!);
      window.location.href = '/dashboard';
    } else {
      setError(result.error || 'Authentication failed');
    }
  };

  if (!supported) {
    return <p>Passkeys are not supported in this browser</p>;
  }

  return (
    <div>
      <button onClick={handleLogin} disabled={loading}>
        {loading ? 'Authenticating...' : 'Sign in with Passkey'}
      </button>
      {error && <p style={{ color: 'red' }}>{error}</p>}
    </div>
  );
}

export function PasskeyRegister({ userName }: { userName: string }) {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);

  const handleRegister = async () => {
    setLoading(true);
    setError(null);

    const result = await passkey.register({
      userName,
      deviceName: navigator.platform || 'Unknown Device'
    });

    setLoading(false);

    if (result.success) {
      setSuccess(true);
    } else {
      setError(result.error || 'Registration failed');
    }
  };

  if (success) {
    return <p>Passkey registered successfully!</p>;
  }

  return (
    <div>
      <button onClick={handleRegister} disabled={loading}>
        {loading ? 'Registering...' : 'Add Passkey'}
      </button>
      {error && <p style={{ color: 'red' }}>{error}</p>}
    </div>
  );
}
```

---

## Testing the Implementation

### Manual Test Checklist

#### 1. Test Registration Flow

```bash
# 1. Open browser developer tools (F12)
# 2. Go to your application's registration page
# 3. Enter a username and click "Register with Passkey"
# 4. Follow the browser prompts to create a passkey
# 5. Verify the passkey was stored in the database:

psql -c "SELECT user_id, device_name, created_at FROM passkeys ORDER BY created_at DESC LIMIT 5;"
```

#### 2. Test Authentication Flow

```bash
# 1. Go to your application's login page
# 2. Click "Sign in with Passkey"
# 3. Select your passkey from the browser prompt
# 4. Verify authentication was successful
# 5. Check the audit log:

psql -c "SELECT user_id, event_type, ip_address, created_at FROM auth_audit_log ORDER BY created_at DESC LIMIT 5;"
```

#### 3. Test Analytics Data

```bash
# Check that analytics data is being captured
psql -c "SELECT analytics_data FROM auth_audit_log ORDER BY created_at DESC LIMIT 1;"
```

#### 4. Test Challenge Expiration

```bash
# Create a challenge
curl -X POST http://localhost:5000/api/passkey/login/options \
  -H "Content-Type: application/json" \
  -d '{}'

# Wait 6 minutes (challenge expires after 5 minutes)
# Try to authenticate with the old challenge - should fail
```

#### 5. Test Error Cases

- Try to register the same passkey twice (should fail with 409)
- Try to authenticate with invalid signature (should fail with 401)
- Try to use expired challenge (should fail with 400)
- Try to authenticate with non-existent credential (should fail with 401)

### API Testing with curl

```bash
# 1. Get registration options
curl -X POST http://localhost:5000/api/passkey/register/options \
  -H "Content-Type: application/json" \
  -d '{"userName": "testuser@example.com"}' | jq

# 2. Get authentication options (discoverable)
curl -X POST http://localhost:5000/api/passkey/login/options \
  -H "Content-Type: application/json" \
  -d '{}' | jq

# 3. Get authentication options (with username)
curl -X POST http://localhost:5000/api/passkey/login/options \
  -H "Content-Type: application/json" \
  -d '{"userName": "testuser@example.com"}' | jq
```

### Database Verification Queries

```sql
-- Check registered users
select id, username, display_name, created_at from users order by created_at desc limit 10;

-- Check registered passkeys
select
    encode(credential_id, 'base64') as cred_id,
    user_id,
    device_name,
    sign_count,
    created_at,
    last_used_at
from passkeys
order by created_at desc
limit 10;

-- Check pending challenges (should be empty after use)
select
    id,
    user_id,
    operation,
    expires_at,
    created_at
from passkey_challenges
where expires_at > now()
order by created_at desc;

-- Check authentication audit log
select
    user_id,
    event_type,
    ip_address,
    analytics_data->>'timezone' as timezone,
    analytics_data->'browser'->>'userAgent' as user_agent,
    created_at
from auth_audit_log
order by created_at desc
limit 10;

-- Cleanup expired challenges
select cleanup_expired_challenges();
```

---

## Troubleshooting

### Common Issues

#### "WebAuthn is not supported"
- Ensure you're using HTTPS (or localhost for development)
- Check browser compatibility: Chrome 67+, Firefox 60+, Safari 13+, Edge 79+

#### "NotAllowedError" during registration/authentication
- User cancelled the operation
- Operation timed out
- No authenticator available
- Security key not inserted

#### "InvalidStateError" during registration
- The credential is already registered for this user
- Check `excludeCredentials` in the creation options

#### Challenge validation fails
- Challenge expired (default: 5 minutes)
- Challenge already consumed (replay attack prevention)
- Challenge ID mismatch

#### Signature validation fails
- Public key mismatch
- Data corrupted during transmission
- Wrong algorithm used

#### "Origin mismatch" error
- Add your origin to `RelyingPartyOrigins` in config
- Ensure scheme (http/https) matches exactly

### Debug Logging

Enable debug logging in your configuration:

```json
{
  "Log": {
    "Level": "Debug"
  }
}
```

This enables debug-level logging for NpgsqlRest which will show passkey authentication events.

### Browser Developer Tools

1. Open Network tab to inspect API requests/responses
2. Check Console for JavaScript errors
3. Use Application tab to see stored credentials (Chrome)

### Database Debugging

```sql
-- Check for orphaned challenges
select count(*) from passkey_challenges where expires_at < now();

-- Check sign count progression
select user_id, sign_count, last_used_at
from passkeys
where user_id = 'your-user-id'
order by last_used_at desc;

-- Verify user exists
select * from users where username = 'testuser@example.com';
```
