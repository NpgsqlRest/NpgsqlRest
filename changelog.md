# Changelog

Note: The changelog for older versions (3.11.1 and earlier) can be found here: [Changelog Archive](https://github.com/NpgsqlRest/NpgsqlRest/blob/master/changelog-old.md)

---

## Version [3.13.0](https://github.com/NpgsqlRest/NpgsqlRest/tree/3.13.0) (2026-04-24)

[Full Changelog](https://github.com/NpgsqlRest/NpgsqlRest/compare/3.12.0...3.13.0)

### New: Caching Profiles (`CacheOptions.Profiles` + `@cache_profile` annotation)

Named cache profiles allow you to maintain multiple distinct caching policies in one application — different backends, expirations, key shapes, or bypass conditions — and let endpoints opt into them via a single comment annotation.

```jsonc
"CacheOptions": {
  "Enabled": true,
  "Type": "Memory",
  "Profiles": {
    "fast_memory": {
      "Enabled": false,
      "Type": "Memory",
      "Expiration": "30 seconds",
      "Parameters": ["user_id"]
    },
    "shared_redis": {
      "Enabled": false,
      "Type": "Redis",
      "Expiration": "1 hour"
    },
    "date_range_hybrid": {
      "Enabled": false,
      "Type": "Hybrid",
      "Expiration": "5 minutes",
      "Parameters": ["from", "to"],
      "When": [
        { "Parameter": "to", "Value": null, "Then": "skip" }
      ]
    }
  }
}
```

```sql
comment on function get_orders(from text, to text) is '
HTTP GET
@cache_profile date_range_hybrid
';
```

**Profile fields:**

- **`Enabled`** (bool, default `false`) — disabled profiles are skipped at startup; flip to `true` to activate.
- **`Type`** — `Memory`, `Redis`, or `Hybrid`. Backends are pooled: all profiles of the same type share one instance (one Memory cache, one Redis connection, one HybridCache singleton). A backend type is only instantiated if root or some enabled profile uses it.
- **`Expiration`** — default expiration in PostgreSQL interval format. Used when the endpoint has no `@cache_expires` annotation.
- **`Parameters`** — default cache-key parameter list:
  - `null` (or property omitted): use **all** routine parameters.
  - `[]` (empty array): URL-only cache (one entry per endpoint, regardless of inputs).
  - `["x", "y"]`: only those named parameters as the key.
  
  The endpoint's `@cached p1, p2` annotation overrides this.
- **`When`** — list of conditional rules. Each rule has:
  - **`Parameter`** — the routine parameter name to inspect.
  - **`Value`** — match condition. Scalar (single match) or array (OR over entries). JSON `null` matches .NET `null`/`DBNull.Value` (does **not** match empty string). Other values are stringify-and-equal case-insensitive.
  - **`Then`** — the literal string `"skip"` to bypass the cache for that request, OR a PostgreSQL interval (e.g. `"30 seconds"`) to override the entry's TTL when writing.

  Rules evaluate in declaration order; **first match wins**. No match → fall through to the profile's `Expiration`.

  This unlocks scenarios that pure skip-on-condition couldn't express:
  - Skip-on-null: `[{ "Parameter": "to", "Value": null, "Then": "skip" }]`
  - Tiered TTL: `[{ "Parameter": "tier", "Value": "free", "Then": "5 minutes" }, { "Parameter": "tier", "Value": "pro", "Then": "1 hour" }]`
  - Status-aware caching: `[{ "Parameter": "status", "Value": ["draft", null], "Then": "skip" }, { "Parameter": "status", "Value": "published", "Then": "1 hour" }]`

  Validation: a rule whose `Parameter` is not in the resolved cache-key parameter list (`Parameters` or the endpoint's `@cached`) is dropped at startup with a Warning. This prevents the surprising case where two requests with different rule-matched values share the same cache entry.

**Annotation:** `@cache_profile <name>` selects a profile. It implies caching even without a separate `@cached` annotation. The existing `@cached p1, p2` (overrides profile params) and `@cache_expires <interval>` (overrides profile expiration) annotations continue to work and take precedence over the profile's defaults.

**Misconfiguration is loud at startup.** Unknown profile names referenced by `@cache_profile` cause startup to fail with a single `InvalidOperationException` listing every unresolved name and the endpoints that referenced each — so typos surface immediately rather than silently disabling caching at runtime. Profiles registered but unreferenced log an Information warning. Bad `Type` or `Expiration` values warn and skip the profile. Empty/whitespace profile names are rejected.

**Cache key isolation.** Entries written under a profile are prefixed with the profile name, so two profiles sharing the same backend (e.g., two Memory profiles) cannot collide on the same routine + parameters. Endpoints without a profile have no prefix — existing cache entries stay wire-compatible across the upgrade.

The default `appsettings.json` ships with three disabled example profiles covering each Type and demonstrating a `When` rule. Flip `"Enabled": true` on the one(s) you need.

#### Never-expiring (infinite) cache entries

There is no explicit "forever" or "never" literal — instead, **omit the `Expiration` field** to get never-expiring entries. This applies at every level:

- **Endpoint without `@cache_expires`** → entry never expires (today's pre-3.13 behavior, unchanged).
- **Profile without `Expiration` field** → entries written under that profile never expire.
- **Both `@cache_expires` and profile `Expiration` set** → annotation wins (the explicit interval is used).

If you need a mix in one app — some profiles with TTL, others never-expiring — define a dedicated profile with no `Expiration`:

```jsonc
"Profiles": {
  "static_lookup_data": {
    "Enabled": true,
    "Type": "Memory"
    // No Expiration → entries never expire (suitable for ISO codes, taxonomies, etc.)
  },
  "session_data": {
    "Enabled": true,
    "Type": "Redis",
    "Expiration": "1 hour"
  }
}
```

Endpoints opt into the appropriate profile via `@cache_profile`. This pattern handles the common cases (static reference data, immutable historical content) without needing a separate "force never expire" override.

### New: `WrapInTransaction` Option (Connection Pooler Compatibility)

When set to `true`, **every request** is wrapped in an explicit `BEGIN ... COMMIT`, and all `set_config` calls switch from session-scoped (`is_local=false`) to transaction-local (`is_local=true`).

**This is required for connection poolers in transaction mode** — including PgBouncer transaction-pool, AWS RDS Proxy in transaction mode, and Supabase Pooler. Previously, `set_config(name, value, false)` would set the GUC at the session level on the underlying PostgreSQL backend. With a transaction-mode pooler, the same backend is reused for unrelated client requests, which means session-scoped GUCs from one request could be visible to the next. With `WrapInTransaction = true`, GUCs are scoped to the request transaction and discarded on `COMMIT`.

The default remains `false` to preserve existing behavior; it is safe to leave off when using Npgsql's native pool only (which issues `DISCARD ALL` on connection return).

```jsonc
{
  "NpgsqlRest": {
    "WrapInTransaction": true
  }
}
```

### New: `BeforeRoutineCommands` Option

A new option allowing arbitrary SQL commands to run after any context is set but before the main routine call. They run in the **same batch** as the context `set_config` calls, so there is no extra network round-trip.

Each entry can be either a raw SQL string (no parameters) or an object with `Sql` and `Parameters`. Each parameter has a `Source` (`Claim`, `RequestHeader`, or `IpAddress`) and an optional `Name` (claim type or header name). Parameter values are bound at request time from `HttpContext` — claim and header values are passed as parameterized SQL inputs (no string interpolation, no injection risk).

The most useful pattern is **multi-tenant `search_path` setup** driven by a JWT/cookie claim:

```jsonc
{
  "NpgsqlRest": {
    "WrapInTransaction": true,
    "BeforeRoutineCommands": [
      "select set_config('app.request_time', clock_timestamp()::text, true)",
      {
        "Sql": "select set_config('search_path', $1, true)",
        "Parameters": [{ "Source": "Claim", "Name": "tenant_id" }]
      }
    ]
  }
}
```

Per-request execution order with this config:
1. `BEGIN`
2. Each `BeforeRoutineCommand` is added as a `NpgsqlBatchCommand` (with parameters bound from claims/headers/IP) and dispatched in a single batch.
3. The main routine call.
4. `COMMIT`.

Steps 1–3 share a single network round-trip.

### Fix: 400 Bad Request responses are no longer silent in logs

Endpoints that returned **HTTP 400** were not being logged at all, making client-error problems invisible in production. Two independent paths produced silent 400s:

1. **Database exceptions mapped to 400** (`P0001 raise exception`, `P0004 assert_failure`, or any user-configured `ErrorHandlingOptions` mapping to 400). The exception handler in `NpgsqlRestEndpoint` explicitly skipped logging for status `400`.
2. **Validation rule failures** (`ValidationOptions.Rules` → 400). These were logged at `Debug` level, which is below the default minimum log level (`Information`), so they never appeared in production logs.

**Fix:** 400s are now logged at **`Warning` level** — visible by default but not raised to `Error`, since 400 is a client-side problem rather than a server fault. Genuine server errors (500, etc.) continue to be logged at `Error` with full stack traces.

### Docker Images: Ubuntu 26.04 LTS Base

The native AOT Docker images (`latest`, `latest-arm`, `latest-bun`) now build on Ubuntu 26.04 "Resolute Wolf" LTS, up from Ubuntu 25.04 (a 9-month interim release that is reaching end of support). This extends the security-update window for the published images to the 5-year LTS support period and picks up a newer stack (Linux 7.0 kernel, newer OpenSSL, cgroup v2). No changes are required for consumers of the images — runtime dependencies (`libssl3`, `libgssapi-krb5-2`, `ca-certificates`) resolve under the same package names on 26.04.

### NuGet Package Upgrades

**`NpgsqlRest` (main library):**

- `Microsoft.SourceLink.GitHub` 10.0.201 → 10.0.203 *(build-time only)*

**`NpgsqlRestClient` (client application):**

- `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.5 → 10.0.7
- `Microsoft.Extensions.Caching.Hybrid` 10.4.0 → 10.5.0
- `Microsoft.Extensions.Caching.StackExchangeRedis` 10.0.5 → 10.0.7
- `StackExchange.Redis` 2.12.8 → 2.12.14

---

## Version [3.12.0](https://github.com/NpgsqlRest/NpgsqlRest/tree/3.12.0) (2026-03-23)

[Full Changelog](https://github.com/NpgsqlRest/NpgsqlRest/compare/3.11.1...3.12.0)

---

### New Endpoint Source Plugin: `NpgsqlRest.SqlFileSource`

In addition to the existing endpoint sources — **RoutineSource** (PostgreSQL functions and procedures) and **CrudSource** (tables and views) — NpgsqlRest now supports a third source: **SQL files**.

Generate REST API endpoints directly from `.sql` files. Place SQL files in a configured directory, and NpgsqlRest creates endpoints automatically — no PostgreSQL functions needed.

#### How It Works

1. At startup, the plugin scans the directory matching the configured glob pattern (e.g., `sql/**/*.sql`)
2. Each `.sql` file is parsed: comments are extracted as annotations, SQL is split into statements
3. Each statement is analyzed via PostgreSQL's wire protocol (`SchemaOnly`) — parameter types and return columns are inferred without executing the query
4. A REST endpoint is created for each file, with the URL path derived from the filename

#### Single-Command Files

A file with one SQL statement produces a standard endpoint:

```sql
-- sql/get_reports.sql
-- HTTP GET
-- @param $1 from_date
-- @param $2 to_date
-- @authorize admin
SELECT id, title, created_at
FROM reports
WHERE created_at BETWEEN $1 AND $2;
```

`GET /api/get-reports?from_date=2024-01-01&to_date=2024-12-31` → `[{"id": 1, "title": "Q1", "createdAt": "..."}]`

**HTTP verb auto-detection** (when no explicit `HTTP` annotation):

| SQL Statement | HTTP Verb | Rationale |
|---|---|---|
| `SELECT` / `WITH ... SELECT` | GET | Read-only |
| `INSERT` | PUT | Creation |
| `UPDATE` | POST | Modification |
| `DELETE` | DELETE | Removal |
| `DO $$ ... $$` | POST | Anonymous script |
| Mixed mutations | Most destructive wins | DELETE > POST > PUT |

An explicit `HTTP GET`, `HTTP POST`, etc. annotation always overrides auto-detection.

**Note:** `DO` blocks do not support `$N` parameters — this is a PostgreSQL language limitation. A `DO` block always produces a parameterless endpoint. In multi-command files, `DO` blocks work alongside parameterized statements — the other commands receive the shared parameters, the `DO` block receives none.

#### Multi-Command Files

A file with multiple statements (separated by `;`) returns a JSON object. Each key corresponds to one command's result:

```sql
-- sql/process_order.sql
-- HTTP POST
-- @param $1 order_id
-- @result validate
SELECT count(*) FROM orders WHERE id = $1;
UPDATE orders SET status = 'processing' WHERE id = $1;
-- @result confirm
SELECT id, status FROM orders WHERE id = $1;
```

`POST /api/process-order` with `{"order_id": 42}` →

```json
{
  "validate": [1],
  "result2": 1,
  "confirm": [{"id": 42, "status": "processing"}]
}
```

**Result set rules:**

- Commands returning rows → JSON array of row objects (same format as single-command endpoints)
- Void commands (INSERT/UPDATE/DELETE without RETURNING) → rows-affected count as integer
- Multi-command endpoints are never void — they always return a JSON object

**Result naming:**

- Default keys: `result1`, `result2`, `result3`, ... (prefix configurable via `ResultPrefix` setting)
- Override with the positional `@result` annotation placed before the statement it applies to, or inline after the semicolon:
  - `@result validate` — renames the next result to `validate`
  - `@result is validate` — same ("is" style)
  - Commands without `@result` keep their default name

**Execution:**

- Uses `NpgsqlBatch` with one `NpgsqlBatchCommand` per statement — single database round-trip
- All statements share the same parameters (`$1`, `$2`, etc.) — user sends each parameter once
- Full retry logic via `ExecuteBatchReaderWithRetryAsync` with error code mapping and timeout handling
- If any command fails, the entire request fails — no partial results

#### Parameters

SQL files use PostgreSQL positional parameters (`$1`, `$2`, ...). Parameters are passed via query string (GET) or JSON body (POST/PUT/DELETE):

```
GET /api/my-query?$1=hello&$2=42
POST /api/my-mutation {"$1": "hello", "$2": 42}
```

Use the `@param` annotation for better names:

```sql
-- @param $1 user_name
-- @param $2 age
SELECT * FROM users WHERE name = $1 AND age > $2;
```

Now: `GET /api/my-query?user_name=hello&age=42`

**For multi-command files:** Each statement is described individually. Parameter types are merged across all statements:
- Same `$N` with same type across statements → use that type
- Same `$N` with conflicting types → startup error with clear message (override with `@param $1 name type`)
- `$N` used in only some statements → type from the statement(s) that reference it

#### Virtual Parameters

Use `@define_param` to create HTTP parameters that are NOT bound to the PostgreSQL command. These parameters exist for HTTP request matching, custom parameter placeholders, and claim mapping — without appearing in the SQL query.

**Use case: custom parameter placeholders** — pass HTTP parameters that control endpoint behavior (e.g., output format) without referencing them in SQL:

```sql
-- sql/users_report.sql
-- @define_param format text
-- table_format = {format}
-- @param $1 department_id
SELECT id, name, email FROM users WHERE department_id = $1;
```

`GET /api/users-report?department_id=5&format=html_table` — the `format` parameter feeds into the `table_format` custom parameter via `{format}` placeholder, selecting the output format (JSON, HTML table, etc.) without being part of the SQL query.

**Use case: claim mapping** — auto-fill from user claims without SQL reference:

```sql
-- @authorize
-- @user_parameters
-- @define_param _user_id
SELECT * FROM user_data;
```

Default type is `text`; specify a type with `@define_param name type`.

#### Comments and Annotations

All comments in the SQL file are parsed as annotations, just like `COMMENT ON FUNCTION` in PostgreSQL:

```sql
-- Line comments are annotations
/* Block comments are annotations too */
SELECT * FROM table;
-- Comments after statements also work
```

All existing NpgsqlRest annotations work: `@authorize`, `@allow_anonymous`, `@tag`, `@sse`, `@request_param_type`, `@path`, `@timeout`, `@cached`, `@raw`, `@header`, `@separator`, `@login`, `@logout`, `@encrypt`, `@decrypt`, etc.

**New annotations for SQL files:**

| Annotation | Description | Example |
|---|---|---|
| `@param $N name` | Rename positional parameter | `-- @param $1 user_id` |
| `@param $N name type` | Rename + retype parameter | `-- @param $1 user_id integer` |
| `@param $N is name` | Rename ("is" style) | `-- @param $1 is user_id` |
| `@result name` | Rename multi-command result key (positional) | `-- @result validate` |
| `@result is name` | Rename result key ("is" style, positional) | `-- @result is validate` |
| `@define_param name [type]` | Define virtual parameter (not bound to SQL) | `-- @define_param _user_id` |

**`CommentScope` setting** controls which comments are parsed:
- `All` (default) — every comment in the file, regardless of position
- `Header` — only comments before the first SQL statement

#### Wire Protocol Introspection

At startup, each statement is analyzed via PostgreSQL's Parse → Describe → Sync cycle (`CommandBehavior.SchemaOnly`):

- **Parameter types** inferred from `ParameterDescription` message (authoritative OIDs)
- **Return columns** inferred from `RowDescription` message (column names and types)
- No query planning, no execution — roughly the cost of `SELECT 1`
- Uses `reader.GetName()` / `reader.GetDataTypeName()` instead of `GetColumnSchema()` to avoid .NET type mapping failures for custom composite types
- Unknown type OIDs (custom types returning `"-.-"`) resolved via `pg_catalog.pg_type` query

#### Custom / Composite Type Support

Composite type columns in SQL file endpoints behave the same way as routine endpoints (functions and procedures) — flat by default, nested with the `@nested` annotation or `NestedJsonForCompositeTypes` setting. Arrays of composite types are also supported.

#### Unnamed and Duplicate Columns

SQL without column aliases:
```sql
SELECT $1, $2;
```

Produces valid JSON with unique fallback names instead of duplicate `?column?` keys:
```json
[{"column1": "hello", "column2": "world"}]
```

Use `AS` aliases for meaningful names: `SELECT $1 AS name, $2 AS value`.

#### URL Path Derivation

The endpoint path is derived from the filename (without `.sql` extension) using the same `NameConverter` as functions. For example, with the default camelCase converter:

- `get_reports.sql` → `/api/get-reports`
- `user_profile.sql` → `/api/user-profile`

Override with the `@path` annotation: `-- @path /custom/path/{id}`

#### Error Handling

| Mode | Behavior | Use Case |
|---|---|---|
| `ParseErrorMode.Exit` (default) | Logs error, exits process | Fail-fast — catches SQL errors at startup |
| `ParseErrorMode.Skip` | Logs error, skips file, continues | Production — tolerate partial failures |

All SQL file errors are logged at `Error` level. In `Exit` mode, a `Critical` log explains the exit and how to switch to `Skip` mode. PostgreSQL errors include compiler-like formatting with line:column position, source line excerpt, and a caret pointing at the error location:

```
SqlFileSource: /path/to/get-posts.sql:
error 42703: column u.id does not exist
  at line 3, column 12
  select u.id, u.name from users u
             ^
```

A warning is logged when the configured file pattern matches no files.

Errors caught at startup:
- Parse errors (malformed SQL, unclosed strings/quotes)
- Describe errors (PostgreSQL syntax errors, invalid table/column references)
- Parameter type conflicts in multi-command files

#### Feature Parity

SQL file endpoints support all features available to function/procedure endpoints:

- Composite type expansion (flat by default, nested with `@nested` annotation)
- Response caching (`cached`, `cache_expires_in`)
- Raw mode (`raw`, `raw_value_separator`, `raw_new_line_separator`, `raw_column_names`)
- Binary mode
- Encryption/decryption (`encrypt`, `decrypt`)
- Table format handlers (e.g., HTML table output)
- SSE events
- Authorization (`authorize`, `allow_anonymous`)
- Custom headers (`header`)
- Retry logic with error code mapping
- Buffer rows configuration
- HTTP client types (`@param $1 name http_type_name` — composite type parameters with HTTP definitions)
- Self-referencing HTTP client types — relative paths (e.g., `GET /api/endpoint`) call back to the same server instance, enabling parallel internal endpoint composition

#### Configuration Reference

```json
"NpgsqlRest": {
  "SqlFileSource": {
    "Enabled": true,
    "FilePattern": "sql/**/*.sql",
    "CommentsMode": "ParseAll",
    "CommentScope": "All",
    "ErrorMode": "Exit",
    "ResultPrefix": "result",
    "UnnamedSingleColumnSet": true,
    "NestedJsonForCompositeTypes": false
  }
}
```

| Setting | Type | Default | Description |
|---|---|---|---|
| `Enabled` | bool | `false` | Enable or disable SQL file source endpoints |
| `FilePattern` | string | `""` | Glob pattern for SQL files. Supports `*`, `**` (recursive), `?`. Empty = disabled |
| `CommentsMode` | enum | `OnlyWithHttpTag` | `OnlyWithHttpTag` = requires explicit `HTTP` annotation. `ParseAll` = every file becomes an endpoint |
| `CommentScope` | enum | `All` | `All` = parse all comments. `Header` = only before first statement |
| `ErrorMode` | enum | `Exit` | `Exit` = log error + exit process. `Skip` = log error + continue |
| `ResultPrefix` | string | `"result"` | Prefix for multi-command result keys (e.g., `result1`, `result2`) |
| `UnnamedSingleColumnSet` | bool | `true` | Single-column queries return flat arrays (`["a","b"]`) instead of object arrays (`[{"col":"a"},{"col":"b"}]`). Applies to both single-command and per-result in multi-command files. Matches function behavior for `setof` single values |
| `NestedJsonForCompositeTypes` | bool | `false` | When `true`, composite type columns are serialized as nested JSON objects under their column name. When `false` (default), composite fields are flattened inline — matching routine behavior. Can also be enabled per-endpoint with the `nested` annotation |

---

### New Annotations

---

#### New Core Annotation: `@param` / `@parameter` — Rename and Retype Parameters

A new comment annotation that renames and optionally retypes individual parameters. Works on **all** endpoint types — functions, procedures, CRUD, and SQL file endpoints.

Positional parameters (`$1`, `$2`) already work as HTTP parameter names (`?$1=value`), but this annotation provides better API ergonomics:

```sql
-- Simplest form: rename only
-- @param $1 user_id

-- Simplest form: rename + retype
-- @param $1 user_id integer

-- "is" style: rename only (consistent with existing @param X is hash of Y)
-- @param $1 is user_id

-- "is" style: rename + retype
-- @param $1 is user_id integer

-- Rename named parameters (works on function/procedure params too)
-- @param _old_name better_name
-- @param _old_name better_name text
```

All forms coexist with existing `@param X is hash of Y` and `@param X is upload metadata` handlers without ambiguity. Both `@param` and `@parameter` (long form) are supported.

---

#### `@param` Default Values for SQL File Parameters

SQL file parameters can now have default values via the `@param` annotation. When a parameter with a default is not provided in the request, the default value is bound instead of returning 404.

This is essential for SQL files because positional parameters (`$1`, `$2`) must always be bound — unlike PostgreSQL functions where the engine applies its own defaults.

**Syntax:**

```sql
-- Separate annotations (rename first, then set default):
-- @param $1 user_id
-- @param user_id default null

-- Combined rename + default on a single line:
-- @param $1 user_id default null

-- "is" style rename + default:
-- @param $1 is user_id default null

-- Rename + retype + default:
-- @param $1 user_id integer default 42
-- @param $1 is user_id integer default 42

-- Default without rename:
-- @param $1 default 'fallback'

-- Various value types:
-- @param $1 status default 'active'     -- text (single-quoted)
-- @param $1 amount default 42           -- number
-- @param $1 enabled default true        -- boolean
-- @param $1 filter default null         -- SQL NULL (unquoted)
-- @param $1 tag default 'null'          -- literal text "null" (quoted)
-- @param $1 val default                 -- no value = NULL
```

**Value parsing rules (SQL conventions):**
- Unquoted `null` (case-insensitive) → `DBNull.Value`
- Single-quoted `'text value'` → string literal (supports multi-word)
- Unquoted value → raw string (Npgsql handles type conversion via `NpgsqlDbType`)

**Real-world example** — user identity endpoint with claim-filled parameters that fall back to NULL:

```sql
/* HTTP GET
@authorize
@user_parameters
@param $1 _user_id default null
@param $2 _username default null
@param $3 _email default null
*/
select $1 as user_id, $2 as username, $3 as email;
```

When authenticated, claims fill the parameters automatically. The defaults ensure the parameters are always bindable.

**Effects on generated output:**
- **TsClient**: Parameters with defaults get `?` suffix in TypeScript interfaces (optional)
- **OpenAPI**: Parameters with defaults are marked `required: false`

---

#### `@param` Rename Validation

Parameter names are now validated when renaming via `@param`. Invalid renames are rejected with a warning log instead of silently creating broken endpoints.

**Rules:**
- Must be a valid PostgreSQL identifier: starts with letter or `_`, followed by letters, digits, `_`, or `$`
- Positional parameters (`$1`, `$2`) are allowed

```sql
-- Valid:
-- @param $1 user_id        ✓
-- @param $1 _val$1         ✓

-- Rejected (with warning log):
-- @param $1 1bad           ✗ starts with digit
-- @param $1 my-param       ✗ invalid character (hyphen)
```

---

#### `@param` Default Value: `=` Alias for `default`

The `@param` annotation now accepts `=` as a shorthand for `default` when setting default values:

```sql
-- These are equivalent:
-- @param $1 _user_id text default null
-- @param $1 _user_id text = null

-- Works with any value type:
-- @param $1 user_id integer default 42
-- @param $1 user_id integer = 42

-- Also works with standalone default:
-- @param my_name = 'hello'

-- And "is" style:
-- @param $1 is greeting = 'hey'
```

---

#### `@param` Type Hints for SQL File Describe

When a SQL file parameter has an explicit type in the `@param` annotation (e.g., `@param $1 name text`), that type is now used during the PostgreSQL Describe step instead of `Unknown`. This fixes startup errors like `42P18: could not determine data type of parameter $1` that occurred when PostgreSQL's parser couldn't infer the parameter type from context alone — for example, in `select set_config('key', $1, true)`.

---

#### New Positional Annotation: `@returns` — Skip Describe and Declare Return Type

New positional annotation `@returns` that skips the PostgreSQL Describe step entirely for a statement. The SQL is never sent to PostgreSQL during startup. Supports three forms:

- `@returns <composite_type>` — resolve columns from the composite type definition
- `@returns <scalar_type>` — declare a single typed column (e.g., `integer`, `text`, `boolean`). Only the first column is used at runtime.
- `@returns void` — no columns, void result

**Composite type example** (temp tables created at runtime):

```sql
-- HTTP GET
-- @param $1 val1 text
-- @param $2 val2 integer
begin;
select set_config('app.val1', $1, true); -- @skip
select set_config('app.val2', $2::text, true); -- @skip
do $$ begin
    create temp table _result on commit drop as
    select current_setting('app.val1') as val1,
           current_setting('app.val2')::int as val2;
end; $$;
-- @returns my_result_type
-- @result data
-- @single
select * from _result;
end;
```

Without `@returns`, the `select * from _result` statement fails during startup Describe because the temp table doesn't exist yet. With `@returns my_result_type`, the columns are resolved from the composite type definition in `pg_catalog` instead.

**Scalar type example** — declare a single typed column, extra columns ignored:

```sql
-- @returns integer
-- @single
select count(*) from users;
```

Returns bare `42` instead of `[{"count": 42}]`.

**Void example** — no results, skipping Describe entirely:

```sql
-- @returns void
select set_config('key', $1, false);
```

In multi-command files, void statements produce a rows-affected count. For single-command files, it makes the entire endpoint void (204 No Content).

The Describe step is completely skipped for annotated statements — the SQL is never sent to PostgreSQL during startup. The composite type must exist in the database at startup. If the type is not found, an error is logged and the file is skipped or the process exits (depending on `ErrorMode`).

---

#### New Annotation: `@void` — Force Void Response

New comment annotation `void` (alias: `void_result`) that forces an endpoint to return 204 No Content instead of a JSON response. All statements are executed for side effects only.

This is particularly useful for multi-command SQL files where all statements are side-effect-only (e.g., `set_config` calls followed by a `DO` block):

```sql
/* HTTP POST
@void
@param $1 message_text text
@param $2 _user_id text = null
*/
select set_config('app.message', $1, true);
select set_config('app.user_id', $2, true);
do $$ begin
    -- use current_setting() to read params inside DO block
    insert into messages (user_id, text)
    values (current_setting('app.user_id')::int, current_setting('app.message'));
end; $$;
```

Without `@void`, this multi-command endpoint would return `{"result1":"...","result2":"...","result3":-1}`. With `@void`, it returns 204 — no JSON, no need to add `@skip` to every statement.

Works on all endpoint types: functions, procedures, CRUD, and SQL file endpoints.

---

#### New Comment Annotation: `@single`

New comment annotation `single` (aliases: `single_record`, `single_result`) that returns a single record as a JSON object instead of a JSON array.

Works across all endpoint sources: PostgreSQL functions, SQL files, and CRUD endpoints.

**Usage:**

```sql
-- PostgreSQL function
CREATE FUNCTION get_user(int) RETURNS TABLE(id int, name text) ...
COMMENT ON FUNCTION get_user IS 'HTTP GET
@single';

-- SQL file
-- HTTP GET
-- @single
-- @param $1 id
SELECT id, name FROM users WHERE id = $1;
```

**Without `@single`:** `[{"id": 1, "name": "alice"}]` (array)
**With `@single`:** `{"id": 1, "name": "alice"}` (object)

**Behavior:**

- Multi-column results return a JSON object (no array wrapping)
- Single unnamed column results return a bare JSON value (e.g., `"hello"`, `42`)
- If the query returns multiple rows, only the first row is returned (early exit from rendering loop)
- Empty results respect the `response_null` annotation: `empty_string` (default), `null_literal`, or `no_content` (204)
- TypeScript client generates `Promise<IResponse>` instead of `Promise<IResponse[]>`

**Per-command `@single` in multi-command files:**

In multi-command SQL files, `@single` is positional — it applies to the next statement below it:

```sql
-- HTTP POST
-- @param $1 id
-- @single
SELECT id, name FROM users WHERE id = $1;
UPDATE orders SET status = 'done' WHERE id = $1;
-- @single
SELECT id, status FROM orders WHERE id = $1;
```

Result: `{"result1": {"id": 1, "name": "alice"}, "result2": 1, "result3": {"id": 1, "status": "done"}}`

- First and third commands return objects (have `@single` above them)
- Second command returns rows-affected count (void, no `@single`)
- Empty per-command `@single` results render as `null`

---

#### Positional `@result` Annotation for Multi-Command Files

Result keys in multi-command SQL files are named positionally. Annotations can be placed in two ways:

**Before the statement** (on a separate line) — applies to the next statement below:

```sql
-- @result users
SELECT id, name FROM users;
-- @result orders
SELECT id, total FROM orders;
```

**Inline after the semicolon** (on the same line) — applies to the statement on that line:

```sql
SELECT id, name FROM users; -- @result users
SELECT id, total FROM orders; -- @result orders
```

Both produce: `{"users": [...], "orders": [...]}`

This same placement rule applies to all positional annotations: `@result`, `@single`, and `@skip`.

- `@result name` — names the result key for the associated statement
- `@result is name` — "is" syntax also supported
- Commands without `@result` get auto-generated keys: `result1`, `result2`, etc.

---

#### `SkipNonQueryCommands` Setting and `@skip` Annotation

##### `SkipNonQueryCommands` (default: `true`)

Non-query commands in multi-command SQL files are now automatically excluded from the JSON response while still being executed. This eliminates noise like `"result1": -1` from transaction control and session statements.

**Affected commands:** `BEGIN`, `COMMIT`, `END`, `ROLLBACK`, `SAVEPOINT`, `RELEASE`, `SET`, `RESET`, `DO` blocks, `DISCARD`, `LOCK`, `LISTEN`, `NOTIFY`, `DEALLOCATE`.

```sql
-- HTTP POST
-- @param $1 id
BEGIN;
UPDATE users SET active = true WHERE id = $1;
COMMIT;
-- @result verification
SELECT id, active FROM users WHERE id = $1;
```

**Before (without `SkipNonQueryCommands`):**
```json
{"result1":-1,"result2":1,"result3":-1,"verification":[{"id":1,"active":true}]}
```

**After (with `SkipNonQueryCommands`, default):**
```json
{"result1":1,"verification":[{"id":1,"active":true}]}
```

Skipped commands don't consume result numbers — the UPDATE gets `result1`, not `result2`.

DML commands (`INSERT`, `UPDATE`, `DELETE`) are NOT skipped — their rows-affected count is meaningful.

Set `"SkipNonQueryCommands": false` in `SqlFileSource` configuration to disable.

##### `@skip` Annotation (aliases: `@skip_result`, `@no_result`)

For cases not covered by `SkipNonQueryCommands`, use the `@skip` positional annotation to explicitly exclude any statement from the response:

```sql
-- @skip
do $$ begin perform pg_notify('channel', 'event'); end; $$;
-- @result data
SELECT id, name FROM users;
```

Result: `{"data": [...]}`

---

#### New Core Annotation: `@internal` / `@internal_only`

Mark an endpoint as **internal-only** — accessible via self-referencing calls (proxy, HTTP client types) but NOT exposed as a public HTTP route:

```sql
-- Helper endpoint: returns data but is not callable from outside
create function get_cached_rates()
returns json language sql as $$
    select rates from exchange_rates order by fetched_at desc limit 1
$$;
comment on function get_cached_rates() is 'HTTP GET
internal';

-- Public endpoint that composes the internal one
create function convert_currency(_amount numeric, _from text, _to text)
returns json language plpgsql as $$
...
$$;
comment on function convert_currency(numeric, text, text) is 'HTTP GET
proxy GET /api/get-cached-rates';
```

Direct HTTP call to `/api/get-cached-rates` returns 404. But `proxy GET /api/get-cached-rates` and HTTP client types with relative paths can still invoke it internally.

Works on all endpoint sources: functions, procedures, tables/views (CRUD), and SQL files.

---

### HTTP Custom Types & Self-Referencing Calls

---

#### Self-Referencing Calls: Relative Path Support for Proxy and HTTP Client Types

Both `proxy` annotations and HTTP client type definitions now support **relative paths** that call back to the same server instance:

```sql
-- Proxy to another endpoint on the same server
comment on function my_aggregator() is 'HTTP GET
proxy POST /api/data-source';

-- HTTP client type calling a local endpoint
comment on type local_api as 'POST /api/process';
```

**Parallel query composition:** Combined with HTTP client types that execute all requests in parallel (`Task.WhenAll`), this enables a single endpoint to fan out to multiple internal endpoints simultaneously — effectively running parallel queries without client-side orchestration:

```sql
-- Two HTTP types calling different internal endpoints
create type api_users as (body text);
comment on type api_users is 'GET /api/users';

create type api_orders as (body text);
comment on type api_orders is 'GET /api/orders';

-- Function that composes both in parallel
create function get_dashboard(
    _users api_users,
    _orders api_orders
) returns json language plpgsql as $$
begin
    return json_build_object('users', (_users).body::json, 'orders', (_orders).body::json);
end;
$$;
-- One request → two parallel internal calls → combined response
```

Configuration:
- `HttpClientOptions.SelfBaseUrl` / `ProxyOptions.SelfBaseUrl` — explicit base URL for relative path resolution (auto-detected from server addresses when not set)
- In production, relative paths resolve via loopback HTTP to the server's own address
- In test environments, `SetSelfClient()` injects an in-memory handler that bypasses the network entirely

---

#### Internal Self-Call Optimization: Zero HTTP Overhead

Self-referencing endpoints (HTTP client types and proxy definitions with relative paths like `/api/endpoint`) now bypass the HTTP stack entirely. Instead of making a loopback HTTP call through TCP, the endpoint handler is invoked directly in-process via `InternalRequestHandler`.

This enables efficient parallel query composition: a single endpoint can fan out to multiple internal endpoints in parallel (via `Task.WhenAll`), collect the results, and combine them — all without network overhead. Use cases include:
- Parallel data aggregation across multiple queries
- Orchestrating multiple mutations in a single request
- Composing responses from several independent data sources

**Performance:** Microseconds instead of milliseconds per internal call. No TCP connection, no HTTP parsing, no serialization/deserialization at the transport layer.

**Internal handler routing** now matches by HTTP method + path (e.g., `GET /api/data`) instead of path alone. Two endpoints with the same path but different methods (GET vs POST) are correctly distinguished for internal calls.

---

#### Composite Type Parameters in SQL Files — No SQL Rewriting

Composite type parameters in SQL files are now passed as single text values instead of being expanded into multiple parameters with ROW() SQL rewriting. The SQL stays exactly as the user wrote it.

**HTTP custom type parameters** (auto-filled from HTTP calls):
```sql
-- @param $1 _response example_9.exchange_rate_api
select ($1::example_9.exchange_rate_api).body;
```
The framework makes the HTTP call and passes the result as a single composite text value. No SQL rewriting.

**Client-sent composite type parameters:**
```sql
-- @param $1 data my_composite_type
select ($1::my_composite_type).field1, ($1::my_composite_type).field2;
```
The client sends the composite value as PostgreSQL composite text format: `?data=("val1","val2")`. The SQL casts it with `$1::my_composite_type`.

Unknown types in `@param` annotations now produce a warning log instead of silently falling back to `unknown`.

---

### Configuration Changes

---

#### RoutineSource: `Enabled` Configuration Option

The `RoutineOptions` section now supports an `Enabled` setting (default `true`). Set to `false` to disable automatic endpoint creation from PostgreSQL functions and procedures:

```json
"RoutineOptions": {
  "Enabled": false
}
```

This is useful for SQL-files-only deployments where the overhead of querying the PostgreSQL catalog for routines is unnecessary.

---

#### CrudSource Disabled by Default

The `CrudSource:Enabled` setting now defaults to `false` (was `true`).

CrudSource auto-generates CRUD endpoints for all PostgreSQL tables and views, which is rarely desired in production without explicit configuration. Users who need CRUD endpoints should explicitly set `"CrudSource": { "Enabled": true }`.

---

#### CrudSource No Longer Blocks SqlFileSource

Previously, when `CrudSource` was disabled (or its config section was missing), `CreateEndpointSources()` returned early, preventing `SqlFileSource` from being registered. All three endpoint sources (RoutineSource, CrudSource, SqlFileSource) are now independently enabled/disabled.

---

#### DataProtection Disabled by Default

The `DataProtection:Enabled` setting now defaults to `false` (was `true`).

DataProtection is only needed when using Cookie Authentication, Antiforgery tokens, or `@encrypt`/`@decrypt` annotations — all of which are themselves disabled by default. Enabling it unconditionally added unnecessary key management overhead and, on Linux/Docker with `Storage: "Default"`, caused silent key loss on restart (invalidating auth cookies without warning).

Users who enable Auth, Antiforgery, or encrypt/decrypt annotations should explicitly set `"DataProtection": { "Enabled": true }` and choose an appropriate storage mode.

---

#### `SqlFileSource:LogCommandText` Setting

New setting `LogCommandText` in the `SqlFileSource` configuration (default `false`) controls whether multi-command SQL file endpoints include the full SQL text in debug command logs. When false, only the file path and statement count are logged:

```
[DBG] -- POST http://127.0.0.1:8080/api/send-message
-- $1 text = 'hello'
SQL file: sql/send-message.sql (5 statements)
```

When true, the full SQL body is logged (previous behavior). Single-command SQL file endpoints always log the SQL text regardless of this setting. This only applies when `LogCommands` is true.

---

### TsClient Improvements

---

#### TsClient: Composite Type Support for SQL Files

The TypeScript client generator now correctly handles composite type columns in SQL file endpoints, generating interfaces that match the actual JSON response — same behavior as routine endpoints, including flat/nested modes and recursive composites.

---

#### TsClient: Multi-Command SQL File Support

The TypeScript client generator now handles multi-command SQL file endpoints. For multi-command endpoints, TsClient generates a typed response interface with one property per result:

```typescript
interface IProcessOrderResponse {
    validate: number[];  // single-column → flat array (UnnamedSingleColumnSet)
    result2: number;  // void command → rows affected
    confirm: { id: number, status: string }[];
}

export async function processOrder(
    request: IProcessOrderRequest
) : Promise<IProcessOrderResponse> { ... }
```

- Void commands are typed as `number` (rows affected count)
- Data-returning commands are typed as arrays of inline object types
- Single-column commands with `UnnamedSingleColumnSet` enabled generate flat array types (e.g., `string[]`) instead of object arrays
- Single-command SQL file endpoints generate standard typed functions (no change)

---

#### TsClient: SQL File Comment Headers

The TypeScript client generator now produces correct JSDoc comment headers for SQL file endpoints:

- Header line shows the full file path (e.g., `SQL file: /path/to/get-posts.sql`) instead of just the filename
- The `@remarks` section outputs SQL file comments directly instead of incorrectly wrapping them in `comment on function ...` syntax

---

#### TsClient: Type Alias Extraction for Error and Result Types

When `IncludeStatusCode` is enabled, the TypeScript client generator now emits reusable type aliases at the top of each generated file instead of repeating the full inline types everywhere:

```typescript
type ApiError = {status: number; title: string; detail?: string | null};
type ApiResult<T> = {status: number, response: T, error: ApiError | undefined};
```

These aliases are used in function signatures, JSDoc comments, and `as` casts — significantly reducing repetition and line length:

```typescript
// Before (repeated 3x per function):
) : Promise<{status: number, response: string, error: {status: number; title: string; detail?: string | null} | undefined}>

// After:
) : Promise<ApiResult<string>>
```

The type aliases are **not exported**, so importing multiple generated files causes no naming conflicts. TypeScript's structural typing ensures full compatibility.

Two new options control the alias names:
- `ErrorTypeName` (default: `"ApiError"`) — name for the error type alias
- `ResultTypeName` (default: `"ApiResult"`) — name for the generic result type alias

---

#### TsClient: Fix `SkipTypes` Generating Invalid JavaScript

Fixed two bugs when `SkipTypes` is enabled (pure JavaScript output):

1. **Invalid `as` cast in error handling**: The error expression was always generated with a TypeScript `as` type cast (e.g., `await response.json() as {status: number; ...}`), which is invalid JavaScript syntax. The `as` cast is now omitted when `SkipTypes` is true.

2. **No file output with `CreateSeparateTypeFile = false`**: When both `SkipTypes` and `CreateSeparateTypeFile = false` were set, no file was written at all. The code-only content is now written correctly.

---

### Bug Fixes & Log Improvements

---

#### Graceful Shutdown with Active SSE Connections

The application now shuts down cleanly when SSE (Server-Sent Events) connections are active. Previously, pressing Ctrl+C while clients were connected to SSE endpoints would hang because the broadcaster channels were never completed, leaving `ReadAllAsync` loops blocked indefinitely.

On `ApplicationStopping`, all broadcaster channels are now completed, causing SSE middleware to exit gracefully and allowing the app to terminate.

---

#### Downgrade Basic Auth Missing Header Log to Debug

The "No Authorization header found" log message during Basic Authentication was downgraded from `Warning` to `Debug`. This message fires on every initial browser request before credentials are sent, which is normal behavior in the HTTP Basic Auth challenge-response flow — not a warning condition.

---

#### Improved Log Level Classification

Moved verbose per-item logging from `Debug` to `Trace` level to reduce noise at the default `Debug` level:

- **Connection source logs**: Per-source "Using DataSource..." messages now include the source name (e.g., `RoutineSource`, `SqlFileSource`) and are logged at `Trace` instead of `Debug`.
- **TsClient/HttpFiles file generation**: Individual "Created file" messages moved to `Trace`. A single `Debug` summary reports the total count (e.g., `TsClient: Created 15 TypeScript file(s)`).
- **Upload handler config details**: Detailed parameter dumps for each handler type (mime patterns, buffer sizes, etc.) moved to `Trace`.

---

#### Fix `@separator` and `@new_line` Annotations Not Working with `@` Prefix

The `@separator` and `@new_line` comment annotations were silently ignored when using the `@` prefix syntax (e.g., `@separator ,` in `/* */` block comments). This affected SQL file endpoints using block comment annotations. Line comment annotations without `@` prefix (e.g., `-- separator ,`) were not affected.

The root cause: the annotation matching used `line.StartsWith("separator ")` which failed when the line started with `@separator`. All other annotation handlers used `StrEqualsToArray()` which correctly strips the `@` prefix.

---

#### Aggregated Comment Annotation Logging

Comment annotation debug logs are now aggregated into a single line per endpoint instead of one line per annotation. This significantly reduces log noise during development.

**Before (multiple Debug lines per endpoint):**
```
[DBG] SQL file: who-am-i.sql mapped to GET /api/who-am-i has set HTTP by the comment annotation to GET /api/who-am-i
[DBG] SQL file: who-am-i.sql mapped to GET /api/who-am-i has set REQUIRED AUTHORIZATION by the comment annotation.
[DBG] SQL file: who-am-i.sql mapped to GET /api/who-am-i has set SINGLE RECORD by the comment annotation.
```

**After (one Debug line per endpoint):**
```
[DBG] SQL file: who-am-i.sql mapped to GET /api/who-am-i annotations: [HTTP GET, authorize, single]
```

The individual per-annotation log messages are still available at **Trace** level for detailed debugging.

---

#### Fix: `OnlyWithHttpTag` Mode Skips Files Before Describe

When `CommentsMode` is `OnlyWithHttpTag` (the default), SQL files without an HTTP tag are now skipped **before** the PostgreSQL describe step. Previously, files without an HTTP tag were still described against the database, causing errors on invalid SQL files (e.g., migration scripts, utility files) instead of being silently skipped. With `ErrorMode.Exit`, this would crash the process.

---

### Internal & Breaking Changes

---

#### Interface Refactoring: `IEndpointSource` / `IRoutineSource`

`IRoutineSource` split into two interfaces:

- **`IEndpointSource`** — base interface with `CommentsMode`, `NestedJsonForCompositeTypes`, and `Read()`. Used by lightweight sources like `SqlFileSource`.
- **`IRoutineSource : IEndpointSource`** — extended interface adding `Query`, schema/name filtering. Used by `RoutineSource` and `CrudSource`.

`NestedJsonForCompositeTypes` moved from `IRoutineSource` to `IEndpointSource` so that all endpoint sources (including `SqlFileSource`) support composite type nesting configuration.

**Breaking:** `NpgsqlRestOptions.RoutineSources` renamed to `EndpointSources`. `SourcesCreated` callback renamed to `EndpointSourcesCreated`. Custom `IEndpointSource` implementations must now implement `NestedJsonForCompositeTypes`.

---

#### Composite Type Cache: Public API

- `CompositeTypeCache.ResolveTypeDescriptor(TypeDescriptor)` — new public method for plugins to resolve composite/array-of-composite type metadata
- `Routine.CompositeColumnInfo` and `Routine.ArrayCompositeColumnInfo` — changed from `internal` to `public` for plugin access
- Schema-prefix fallback: `public.my_type` now matches cache key `my_type` (handles `GetDataTypeName` vs `regtype::text` format mismatch)

---

#### Glob Pattern Enhancement: `**` Recursive Matching

`Parser.IsPatternMatch` now supports `**` for recursive directory matching:

- `*` — matches any characters (backward-compatible: matches `/` when no `**` in pattern)
- `**` — matches any characters including `/` (crosses directory boundaries)
- When `**` is present in the pattern, `*` stops matching `/` (standard glob semantics)

Examples:
- `sql/**/*.sql` matches `sql/file.sql`, `sql/dir/file.sql`, `sql/a/b/c/file.sql`
- `**/*.sql` matches any `.sql` file at any depth
- `dir/**/file.sql` matches `dir/file.sql` and `dir/a/b/file.sql`

This enhancement benefits all existing `IsPatternMatch` consumers (`StaticFiles.AuthorizePaths`, `StaticFiles.ParseContentOptions.ParsePatterns`, upload MIME types) and enables the SQL file source's recursive file scanning.

---

#### Internal Changes

- `RoutineType.SqlFile` — new enum value for SQL file endpoints (was `Other`), shown in log messages
- `NpgsqlRestParameter.ConvertedName` / `ActualName` — `internal set` (was `private set`) for `@param` rename support
- `ParameterHandler.HandleParameterRename` — new method handling all rename/retype annotation forms
- `SqlFileParameterFormatter` — static singleton, `IsFormattable = false`, zero per-endpoint allocation
- `Routine.MultiCommandInfo` — per-command metadata array (statement SQL, column info, result names)
- `NpgsqlRetryExtensions.ExecuteBatchReaderWithRetryAsync` — new retry extension for `NpgsqlBatch` readers
- Multi-command rendering in `NpgsqlRestEndpoint.cs` — `NpgsqlBatch` execution, `do/while NextResultAsync()` loop, JSON object wrapper with `multiCmdWriteWrapper` flag (skipped in raw/binary mode), table format handler called per result set
- `JsonValueFormatter.FormatValue` — shared value type dispatch for both single and multi-command rendering paths
- Three new log messages: `CommentParamNotExistsCantRename`, `CommentParamRenamed`, `CommentParamRetyped`
- `NpgsqlRestEndpoint` split into partial class files: `NpgsqlRestEndpoint.cs` (request handling + rendering, ~2866 lines) and `NpgsqlRestEndpoint.Helpers.cs` (helper methods, ~352 lines) for easier maintenance
- JSON key escaping: column names, composite field names, and multi-command result keys are now properly escaped with `PgConverters.SerializeString`. Pre-escaped values stored in `Routine.JsonColumnNames`, `MultiCommandInfo.JsonName`/`JsonColumnNames` at startup to avoid per-row escaping overhead during request execution
- `HttpClientOptions.SelfBaseUrl` — configurable base URL for relative-path HTTP client type definitions. Auto-detected from server addresses at runtime when not configured
- `HttpClientTypeHandler.SetSelfClient` — allows injecting a custom `HttpClient` for self-referencing calls (used by `WebApplicationFactory` in tests)
- `HttpClientTypes` initialization moved before `Build()` in `NpgsqlRestBuilder` so definitions are available when endpoint sources process files
- `InternalRequestHandler` — direct in-process endpoint invocation for self-referencing calls. Endpoint handlers stored in `FrozenDictionary` keyed by path. Uses `NonClosingMemoryStream` to prevent `PipeWriter.Complete` from closing the response stream. Supports path parameter matching via segment-by-segment template comparison with route value extraction

---

