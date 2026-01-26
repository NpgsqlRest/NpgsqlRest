# Passkey SQL Commands Reference

This document provides example SQL functions for implementing passkey authentication with NpgsqlRestClient. These are templates - adapt them to your database schema.

## Table of Contents

1. [Database Schema](#database-schema)
2. [Registration Commands](#registration-commands)
3. [Authentication Commands](#authentication-commands)
4. [Utility Commands](#utility-commands)

---

## Database Schema

### Required Tables

```sql
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
    sign_count bigint not null default 0,  -- can be omitted if ValidateSignCount is false
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

-- Indexes
create index idx_passkeys_user_id on passkeys(user_id);
create index idx_passkey_challenges_expires on passkey_challenges(expires_at);
```

---

## Registration Commands

### 1. CredentialCreationOptionsCommand

**Config:** `CredentialCreationOptionsCommand`
**Default:** `select * from passkey_creation_options($1)`
**Parameters:** `$1 = user_id or username (text)`

Returns the data needed to generate WebAuthn creation options.

```sql
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
        returning id, username, display_name into v_user;
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
        'id', encode(credential_id, 'base64'),
        'transports', transports
    ))
    into v_exclude_creds
    from passkeys
    where user_id = v_user.id;

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
```

### 2. ChallengeVerifyCommand

**Config:** `ChallengeVerifyCommand`
**Default:** `select * from passkey_verify_challenge($1,$2)`
**Parameters:** `$1 = challenge_id (uuid), $2 = operation (text)`

Verifies and consumes a challenge during registration. Deletes the challenge to prevent replay attacks.

```sql
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
```

### 3. CredentialStoreCommand

**Config:** `CredentialStoreCommand`
**Default:** `select * from passkey_store($1,$2,$3,$4,$5,$6,$7,$8)`
**Parameters:** `$1-$8 = credential_id, user_id, user_handle, public_key, algorithm, transports, backup_eligible, device_name`

Stores a newly registered passkey credential.

```sql
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

---

## Authentication Commands

Authentication uses 2 database calls for optimal performance.

### 1. CredentialRequestOptionsCommand

**Config:** `CredentialRequestOptionsCommand`
**Default:** `select * from passkey_request_options($1)`
**Parameters:** `$1 = username (text, optional - null for discoverable credentials)`

Returns the data needed to generate WebAuthn request options.

```sql
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
            'id', encode(credential_id, 'base64'),
            'transports', transports
        ))
        into v_allow_creds
        from passkeys
        where user_id = v_user_id;
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
```

### 2. AuthenticateDataCommand

**Config:** `AuthenticateDataCommand`
**Default:** `select * from passkey_authenticate_data($1,$2,$3)`
**Parameters:** `$1 = challenge_id (uuid), $2 = credential_id (bytea), $3 = operation (text)`

**First DB call during authentication.** Verifies and consumes the challenge, then retrieves credential data.

```sql
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
```

### 3. AuthenticateCompleteCommand

**Config:** `AuthenticateCompleteCommand`
**Default:** `select * from passkey_authenticate_complete($1,$2,$3,$4)`
**Parameters:** `$1 = credential_id (bytea), $2 = new_sign_count (bigint), $3 = user_id (text), $4 = analytics_data (json, optional)`

**Second DB call during authentication.** Updates sign count (if enabled) and returns user claims.

```sql
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
    -- Add more columns for additional claims
) as $$
begin
    -- Update sign count and last used timestamp
    -- Note: p_new_sign_count will be 0 if ValidateSignCount is disabled
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

    -- Optional: Store analytics data for audit/security purposes
    -- You can log this to a separate table or use it for fraud detection
    -- Example: insert into auth_audit_log (user_id, event_type, analytics_data, created_at)
    --          values (p_user_id, 'passkey_login', p_analytics_data, now());

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

**Analytics Data Structure:**

The `p_analytics_data` parameter contains browser information collected during authentication. The JSON structure includes:

```json
{
  "ip": "192.168.1.1",
  "timestamp": "2024-01-15T10:30:00.000Z",
  "timezone": "America/New_York",
  "screen": {
    "width": 1920,
    "height": 1080,
    "colorDepth": 24,
    "pixelRatio": 2,
    "orientation": "landscape-primary"
  },
  "browser": {
    "userAgent": "Mozilla/5.0...",
    "language": "en-US",
    "languages": ["en-US", "en"],
    "cookiesEnabled": true,
    "doNotTrack": null,
    "onLine": true,
    "platform": "MacIntel",
    "vendor": "Google Inc."
  },
  "memory": {
    "deviceMemory": 8,
    "hardwareConcurrency": 8
  },
  "window": {
    "innerWidth": 1920,
    "innerHeight": 1080,
    "outerWidth": 1920,
    "outerHeight": 1080
  },
  "location": {
    "href": "https://example.com/login",
    "hostname": "example.com",
    "pathname": "/login",
    "protocol": "https:",
    "referrer": ""
  },
  "performance": {
    "navigation": {
      "type": 0,
      "redirectCount": 0
    },
    "timing": {
      "loadEventEnd": 1234567890,
      "domComplete": 1234567890
    }
  }
}
```

**Configuration options:**
- `ClientAnalyticsData`: JavaScript expression that collects the analytics data in the browser. Set to `null` to disable.
- `ClientAnalyticsIpKey`: Key name for the IP address added server-side (default: `"ip"`). Set to `null` to disable IP collection.

---

## Utility Commands

### Challenge Cleanup

Run periodically to remove expired challenges:

```sql
-- Manual cleanup
delete from passkey_challenges where expires_at < now();

-- Or create a scheduled function (using pg_cron or similar)
create or replace function cleanup_expired_challenges()
returns void as $$
begin
    delete from passkey_challenges where expires_at < now();
end;
$$ language plpgsql;
```

---

## Authentication Flow

```
Browser                     API Server                  Database
   |                            |                           |
   |-- POST /login/options ---->|                           |
   |                            |-- passkey_request_options -->|
   |                            |<-- challenge, credentials ---|
   |<-- options ----------------|                           |
   |                            |                           |
   |-- [User authenticates] ----|                           |
   |                            |                           |
   |-- POST /login ------------>|                           |
   |                            |-- passkey_authenticate_data ->|  (DB call #1)
   |                            |<-- challenge + credential ----|
   |                            |                           |
   |                            |-- [Verify signature] -----|
   |                            |                           |
   |                            |-- passkey_authenticate_complete ->| (DB call #2)
   |                            |<-- user claims --------------|
   |<-- JWT token --------------|                           |
```

---

## Configuration Example

```json
{
  "Auth": {
    "PasskeyAuth": {
      "Enabled": true,
      "RelyingPartyId": "example.com",
      "RelyingPartyName": "My Application",
      "RelyingPartyOrigins": ["https://example.com"],
      "ValidateSignCount": true,
      "AuthenticateDataCommand": "select * from passkey_authenticate_data($1,$2,$3)",
      "AuthenticateCompleteCommand": "select * from passkey_authenticate_complete($1,$2,$3,$4)",
      "ClientAnalyticsIpKey": "ip"
    }
  }
}
```

**Configuration options:**

- `ValidateSignCount`: Set to `false` to disable signature counter validation and updates. This simplifies the database schema (no `sign_count` column needed) but disables clone detection. Some authenticators don't support sign count and always return 0.

- `ClientAnalyticsData`: JavaScript expression that collects browser analytics data during authentication. The default collects timestamp, timezone, screen dimensions, browser info, memory, window size, location, and performance metrics. Set to `null` to disable analytics collection entirely.

- `ClientAnalyticsIpKey`: The JSON key name used to add the client's IP address to the analytics data server-side. Default is `"ip"`. Set to `null` to disable IP address collection.
