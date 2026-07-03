---
name: npgsqlrest
description: Build and modify REST APIs with NpgsqlRest — exposing PostgreSQL as HTTP endpoints from two sources (database functions/procedures/tables/views, and plain .sql files), driven by SQL comment annotations (no C# needed). Use when working in an NpgsqlRest project: writing or changing endpoint SQL (functions or .sql files), comment annotations (HTTP routing, authorize, cached, proxy, HTTP Custom Types, SSE, MCP, upload), appsettings.json config, testing endpoints with SQL test files (`--test`), running in watch mode (`--watch`), or running/troubleshooting the `npgsqlrest` client.
---

# Working with NpgsqlRest

NpgsqlRest auto-generates a REST API from PostgreSQL. You write SQL and **annotate it with comments**; NpgsqlRest turns each annotated object into an HTTP endpoint at startup. There is **no controller/model/mapping layer and no C# to write** — behavior is declared in SQL comments and `appsettings.json`.

## Mental model (read this first)

- An endpoint comes from one of **two sources** (see below): a **database routine** (function/procedure/table/view) or a **`.sql` file**. Both use the **same annotation vocabulary**; only where you write the annotation differs.
- An endpoint is created when its comment/file carries an `HTTP` tag (or a plugin tag like `mcp`), under the default `CommentsMode: OnlyAnnotated`.
- The result is serialized to JSON automatically (a set → array; `@single` → one object; `@raw` → plain text; multi-statement `.sql` → object keyed per statement).
- Auth, caching, rate limiting, headers, validation, proxy, outbound HTTP, SSE, MCP — all are **comment annotations**.
- `appsettings.json` configures the server, connection, auth, and which sources/schemas/objects are exposed.

## Authoritative sources — prefer these over guessing

The annotation/config surface is large and version-dependent. In order of preference:

1. **Bundled with this skill** (offline, complete — these files sit next to this `SKILL.md`):
   - `annotations-reference.md` — every comment annotation (name, aliases, syntax, description).
   - `configuration-reference.jsonc` — the full `appsettings.json` with every option and inline comments.
2. **The installed binary** (authoritative for the exact version in use):
   ```bash
   npgsqlrest --annotations    # every supported annotation, as JSON
   npgsqlrest --config         # full default appsettings.json (every option, commented)
   npgsqlrest --validate       # test DB connectivity + that endpoints build (incl. .sql Describe)
   npgsqlrest --version
   ```
3. **Online:** docs at <https://npgsqlrest.github.io> (e.g. `/guide/annotations`, `/guide/sql-files`, `/annotations/<name>`, `/config/<section>`); source & issues at <https://github.com/NpgsqlRest/NpgsqlRest>.

The two bundled files were generated from `--annotations` / `--config`. If your installed `npgsqlrest` version differs, trust the binary (or regenerate them with those commands).

## The two endpoint sources

Both are independently enabled; you can use either or **both together** (a `.sql` file can call a function; an HTTP Custom Type can reference any endpoint regardless of source).

### 1. Database routines (functions / procedures / tables / views)

Annotations live in the PostgreSQL **object comment**. NpgsqlRest reads the catalog at startup. Nothing extra to enable beyond schema selection.

```sql
create function get_users(_active boolean default true)
returns setof users language sql as $$
  select * from users where active = _active;
$$;

comment on function get_users(boolean) is '
HTTP GET /api/users
@authorize
@cached _active
@cache_expires_in 5m';
```

- Default path derives from the routine name (kebab-cased, under `UrlPathPrefix`, default `/api`).
- Parameters auto-map by name; `@param` renames/retypes/defaults them.
- Param location (query vs JSON body) is governed by `@request_param_type` / defaults, **not** the HTTP verb (a POST can use query params).
- **Project helper pattern:** some projects wrap the comment in a helper like `call myschema.annotate('schema.func', 'HTTP POST', 'authorize manager', ...)` that just builds the `comment on ...` string. Follow the project's convention — the mechanism is always the routine comment.

### 2. SQL files (`.sql`)

Annotations live in **leading line comments** (`-- ...` or `/* ... */`) in a `.sql` file. No `CREATE FUNCTION`, no `COMMENT ON`. Enable the source and point it at a glob:

```json
{ "NpgsqlRest": { "SqlFileSource": { "Enabled": true, "FilePattern": "sql/**/*.sql" } } }
```

Files matching `SkipPattern` (default `"*.test.sql"`) are excluded from endpoint discovery — they are test files for the [test runner](#testing-sql-test-files---test), which makes the co-located layout (`app.sql` next to `app.test.sql`) safe.

```sql
-- sql/get-reports.sql
-- HTTP GET
-- @authorize
select id, title, created_at
from reports
where created_at between :from_date and :to_date;
```

- **Filename → path**: `get-reports.sql` → `/api/get-reports`.
- **Startup Describe = static type checking.** Each statement is parsed/described against the live DB (no execution); SQL errors fail startup (`ErrorMode: Exit`; set `Skip` to log-and-continue). This catches `column does not exist` etc. before serving.
- **Parameters: named (`:name`) or positional (`$1, $2`)** — one style per file (mixing is a startup error). **Prefer named**: the placeholder IS the parameter name (camelCased for the API: `:from_date` → `fromDate`), a repeated name is ONE parameter (also across statements), and claim mappings (`@user_parameters` + `:_user_id`) hook up with zero annotations. `@param` then only handles type/default: `@param from_date default null`, `@param :from_date date` (Describe type hint), `@param from_date type is date` (retype without rename). Positional: `@param $N name [type] [default ...]` names/retypes/defaults. `@define_param name [type]` creates a *virtual* param (placeholders/claims; not bound to SQL). Tokenizer caveats for `:name`: `::casts`, `:=`, and `a[1:3]` never match; a variable slice bound needs a space (`a[1 : n]`).
- **Verb inference** when no `HTTP` tag: `SELECT`→GET, `INSERT`→PUT, `UPDATE`→POST, `DELETE`→DELETE, `DO`→POST (most destructive wins). Explicit `-- HTTP POST` overrides.
- **Multi-command files** (statements split on `;`) run in **one batch / round-trip** and return a JSON object keyed per statement. Positional annotations apply to the *next* statement (or inline after `;`):
  - `@result name` — name the result key (default `result1`, `result2`, …)
  - `@single` — that statement returns one object instead of an array
  - `@skip` — run it but exclude from the response
  - `@void` — whole endpoint returns **204 No Content** (all statements run for side effects only)
- **`@returns <type|scalar|void>`** — skip Describe for a statement and resolve columns from a type instead. Needed when a statement references objects that don't exist at startup (e.g. a temp table built in a `DO` block).
- **`DO` block limits** (PostgreSQL, not NpgsqlRest): `DO` blocks can't take `$N` params or return values. Bridge with `set_config(key, $1, true)` + `current_setting()`, or a temp table + `@returns`. For real procedural logic, prefer a function.
- Config knobs: `CommentScope` (`All` | `Header`), `UnnamedSingleColumnSet` (single-column → flat array), `ResultPrefix`, `SkipNonQueryCommands`.

### Functions vs SQL files — when to use which

- **SQL files**: declarative queries, multi-statement workflows, teams that prefer plain `.sql` over DDL.
- **Functions**: procedural logic, native params/returns, `assert`-based tests in repeatable migrations, optimizer hints (`STABLE`/`COST`/`ROWS`), overloading.

Everything below (annotations, HTTP types, proxy, caching, auth, SSE, MCP) works **identically in both sources**.

## Annotation cheat-sheet (grouped)

Same annotations apply to functions and `.sql` files. Confirm exact syntax/aliases with `npgsqlrest --annotations`.

**Routing / exposure**
- `HTTP [METHOD] [/path]` — expose. `@path /x` — override path. `@disabled`/`@enabled`. `@internal` — exists but no public route (reachable via proxy / HTTP-type self-call). `@tags a,b`, `@openapi …`.

**Auth**
- `@authorize` (any authenticated) / `@authorize role1, role2`. `@allow_anonymous` (aliases `@anonymous`, `@anon`). `@login` / `@logout`. `@user_context` (claims → PG context + headers), `@user_parameters` (claims → params). `@security_sensitive` (keep param values out of logs).

**Params / request**
- `@param $1 name [type] [default …]` (rename/retype/default). `@define_param name [type]` (virtual, SQL-file). `@request_param_type query_string | body_json`. `@body_parameter_name _x` (one param = raw body). `@request_headers_mode parameter` + `@request_headers_parameter_name _headers`. Resolved param: a line `_token = select api_token from tokens where user_name = {_name}` fills `_token` server-side (client can't override).

**Response shaping**
- `@single`, `@raw` (+ `@separator`, `@new_line`, `@columns`), `@result <name>` (multi-command key), `@skip`, `@void` (204), `@nested` (keep composites nested), `@returns <type>` (SQL-file, skip Describe). Header lines like `Content-Type: text/csv`.

**Caching** (server-side response cache)
- `@cached [p1, p2, …]` — **always list the key params explicitly** (a bare `@cached` keys on the endpoint only). `@cache_expires_in 30s|5m|1h` (alias `@cache_expires`). `@cache_profile name` (backend + defaults from `CacheOptions:Profiles`).

**Other**
- `@rate_limiter_policy name`. `@command_timeout 30s`. `@connection Name` (multi-connection). `@buffer_rows N`. `@validate _email using required, email`. `@error_code_policy 23505 -> 409`. `@upload [for csv|excel|file_system|large_object]`.

**Test files only** (`*.test.sql`, run by `--test` — not endpoint annotations)
- Header: `-- @setup Step ...`, `-- @teardown Step ...`, `-- @connection Name`, `-- @tag a, b`. Inside HTTP blocks: `# @claim name=value`, `# @response name`. Includes: `\i file` / `\ir file`.

## HTTP Custom Types (outbound HTTP from SQL)

A **composite type** whose comment defines an outbound HTTP request. When a routine (or `.sql` param) is of that type, NpgsqlRest performs the call **before** running the endpoint and fills the composite fields.

```sql
create type books_api as (body text, status_code int, success boolean, error_message text);

-- directives go BEFORE the request line
comment on type books_api is '@timeout 30s
@retry_delay 1s, 2s on 429, 503
@cache 5m
GET https://books.toscrape.com/
Accept: text/html';
```

- Directives `@timeout` / `@retry_delay` / `@cache` may appear before the request line OR after the headers (both work since 3.18.0; before is safest).
- Response fields (names configurable in `HttpClientOptions`): `body`, `status_code`, `content_type`, `headers` (json), `success`, `error_message`.
- `@cache <interval>` (3.18.0+): caches the outbound response — **GET only, 2xx only**, stampede-coalesced; globally toggled by `HttpClientOptions.CacheEnabled`.
- Placeholders `{name}` in URL/headers/body substitute from params, resolved-param expressions, allow-listed env vars.
- A relative URL (`GET /api/other`) is a **self-call** to another endpoint with no HTTP round-trip — give a function several HTTP-type params and they fire concurrently.
- Requires `HttpClientOptions.Enabled = true`.

## Proxy (reverse proxy endpoints)

`@proxy [METHOD] [host]` forwards upstream (target = `host + incoming path + query`; host from annotation or `ProxyOptions.Host`).

- **Passthrough** (no proxy-response params): function body is **not executed**; upstream response streamed back; no DB connection.
- **Transform** (routine declares `_proxy_status_code int`, `_proxy_body text`, `_proxy_success boolean`, `_proxy_headers json`, `_proxy_content_type text`, `_proxy_error_message text`): NpgsqlRest proxies, binds the response into those params, runs the function, returns its result.
- **Automatic params forwarded upstream** (3.18.1+): user claims, IP, HTTP-Custom-Type fields, resolved-param expressions are forwarded in the endpoint's native shape (query for `QueryString` endpoints, merged JSON body for `BodyJson`), honoring `@body_parameter_name`.
- Requires `ProxyOptions.Enabled = true`.

## SSE (server-sent events)

A long-running routine streams progress via `RAISE INFO/NOTICE`. Annotate with `@sse` (or a project's `sse_publish`/`sse_subscribe` split) and a scope (`all` | `authorize` | `matching`). Per-recipient targeting: `raise info 'msg' using hint = format('authorize %s', _user_id)`. A subscribe-only endpoint's body never runs. A cache hit skips execution → no RAISE → no broadcast (correct).

## MCP (Model Context Protocol)

`@mcp [text]` exposes a routine as an MCP tool. A bare `@mcp` with **no** HTTP tag = MCP-only (no public route). `@mcp_description` / `@mcp_name` refine it. Served at `/mcp` when the `NpgsqlRest.Mcp` plugin is loaded.

## Auth pattern (login → claims)

A login endpoint is a routine annotated `@login` returning a status/claims row; NpgsqlRest reads configured columns and issues the cookie/token:

```sql
-- returns (status, scheme, user_id, user_name, user_roles, message) by convention
comment on function auth_login(text, text) is 'HTTP POST
@login
@allow_anonymous
@rate_limiter_policy login_throttle
@security_sensitive';
```

- `status` 200 → success (other codes → that HTTP status); `scheme` picks the auth scheme/cookie.
- Column→claim mapping in `AuthenticationOptions` (`StatusColumnName`, `SchemeColumnName`, role/name/id claim columns).
- **Inject identity from claims, never trust client-supplied IDs.** Declare params like `_user_id text = null`, `_user_roles text[] = '{}'` and map them via `AuthenticationOptions.ParameterNameClaimsMapping` / `IpAddressParameterName`. NpgsqlRest fills them from the authenticated principal.
- Use `security definer` on API functions and still permission-check inside (`assert _user_roles && array['admin']` …).

## Configuration (appsettings.json)

`npgsqlrest --config` prints the full annotated default. Most-used sections:

```jsonc
{
  "ConnectionStrings": { "Default": "Host={PGHOST};Port={PGPORT};Database={PGDATABASE};Username={APP_USER};Password={APP_PASSWORD}" },
  "Urls": "http://0.0.0.0:8080",
  "Auth": { "CookieAuth": true, "CookieName": "app", "CookieValid": "365 days" /* + Jwt/Bearer/Passkey/External */ },
  "NpgsqlRest": {
    "IncludeSchemas": ["myapi"],
    "CommentsMode": "OnlyAnnotated",
    "UrlPathPrefix": "/api",
    "RequiresAuthorization": true,
    "KebabCaseUrls": true, "CamelCaseNames": true,
    "AuthenticationOptions": { /* status/scheme/claim columns, ParameterNameClaimsMapping, IpAddressParameterName */ },
    "SqlFileSource": { "Enabled": false, "FilePattern": "sql/**/*.sql", "ErrorMode": "Exit", "CommentScope": "All" }
  },
  "CacheOptions": { "Enabled": true, "Type": "Memory" /* or Redis / Hybrid */, "Profiles": { } },
  "HttpClientOptions": { "Enabled": false /* + CacheEnabled, response field names */ },
  "ProxyOptions": { "Enabled": false, "Host": null },
  "RateLimiterOptions": { "Enabled": false, "Policies": { } },
  "Log": { "MinimalLevels": { "NpgsqlRest": "Information" } }
}
```

- **Two sources, independently enabled:** database routines (always available via the catalog, filtered by `IncludeSchemas`/`SchemaSimilarTo`/`NameSimilarTo`/`CommentsMode`) and `NpgsqlRest:SqlFileSource` (off by default; needs `Enabled` + `FilePattern`).
- `{ENV_VAR}` placeholders are substituted from environment variables at startup.
- `--config <file>` loads a specific file; multiple files overlay (later wins): `npgsqlrest ./appsettings.json ./appsettings.development.json`.
- Dev override file commonly enables `Debug` logging, TypeScript client codegen, and `.http` export.

## Running

```bash
npgsqlrest                                  # appsettings.json in cwd
npgsqlrest ./config/appsettings.json ./config/appsettings.development.json
npgsqlrest --connectionstrings:default="Host=localhost;Database=db;Username=postgres;Password=postgres"
npgsqlrest --log:minimallevels:npgsqlrest=debug    # see every annotation parsed
npgsqlrest ./config.json --test             # run SQL test files, then exit (0 pass / 1 fail / 2 error / 3 config / 4 none)
npgsqlrest ./config.json --watch            # dev server: restart on SQL file / config / database routine changes
npgsqlrest ./config.json --test --watch     # dev test loop: re-run tests on changes
```

Install via the GitHub release binary, `npm install -g npgsqlrest`, or the `vbilopav/npgsqlrest` Docker image.

## Testing: SQL test files (`--test`)

Tests are plain `.sql` files (glob: `TestRunner.FilePattern`, e.g. `./tests/**/*.test.sql` or co-located next to endpoints). Each file runs on its **own non-pooled connection**, in parallel; endpoints are invoked **in-process** (full pipeline: routing, auth, params, serialization) **on the test's own connection/transaction** — so a test can `begin`, insert fixtures, call the endpoint (it sees the uncommitted rows), assert, and `rollback`.

```sql
begin;
insert into users (email) values ('x@example.com');

/*
GET /api/get-users
# @claim user_id=1
*/
select (select status from _response) = 200, 'authenticated caller gets 200';
select (select body::jsonb @> '[{"email": "x@example.com"}]' from _response), 'fixture listed';

rollback;
```

Essentials:
- **Assertions**: a SELECT whose first column is `boolean` (2nd column = assertion name), or a `do $$ ... assert ... $$` block. Everything else is arrange/act.
- **HTTP blocks**: block comment, first line `METHOD /full/path` (incl. `/api` prefix). Directives: `# @claim name=value` (repeatable; any claim = authenticated principal, none = anonymous), `# @response name` (custom capture table). Body after a blank line. Response lands in temp table `_response` (`_response_{n}` for 2+ blocks): `status int, body text, content_type text, headers jsonb, is_success boolean`.
- **Per-file header annotations** (leading `--` comments, TEST files only): `-- @setup StepName ...`, `-- @teardown StepName ...`, `-- @connection Name` (perfect isolation: run the file on its own database), `-- @tag smoke, slow`. Reuse scripts with psql-style `\i file` / `\ir file` includes (paste semantics).
- **Setup/Teardown/Steps** (`TestRunner` config): run-once steps in written order — `{ "Sql": ... }` / `{ "SqlFile": ... }` / `{ "Command": ... }`, each with optional `ConnectionName` and `Enabled` (false = ignored wherever referenced; the default config ships disabled examples to flip on). `Setup` runs BEFORE endpoint discovery → it can `create database app_test_{rnd5}` on an admin connection; `TestRunner.ConnectionName` points the whole run at it; teardown drops it (guaranteed — runs even on Ctrl+C/SIGTERM). `{rnd1}`..`{rnd10}` are per-run-stable random tokens (`{rndN_1}`.. for independent instances).
- **Selection**: `--testrunner:filter=login` (path substring/glob), `--testrunner:tag=smoke --testrunner:excludetag=slow`.
- **Endpoint coverage** reports after full runs (on by default; `CoverageThreshold: 100` fails the build naming untested endpoints). `JUnitOutput` for CI.
- **Debugging**: `DetailedReport: true` (richer report); `ResponseTempTable.DebugTable: "_responses_debug"` mirrors every response into a permanent table (survives rollback; query it after the run; not for CI); the `NpgsqlRestTest` log channel at `Verbose` shows every statement and HTTP call.

## Watch mode (`--watch`)

One flag (`--watch`, or config `Watch: { "Enabled": true }`), two flavors chosen by `--test`:
- **Server watch** (`--watch`): a supervisor restarts the server (~1s) on `.sql` source changes (SkipPattern-filtered), **configuration file** changes, and **database routine changes** — polling runs the routine discovery query hashed server-side (`Watch:DatabasePollingInterval`, default `2s`; detects create/replace/drop/comment on functions, grants, and type/table shape changes; never fires on unrelated objects). Code generation (TS client, HTTP files) re-runs every cycle. A broken SQL file logs its error and drops only its endpoint (`ErrorMode` forced to `Skip` while watching).
- **Test watch** (`--test --watch`): a changed test re-runs alone; a changed endpoint file or database routine rebuilds endpoints in-process (endpoint delta reported) and re-runs everything; teardown runs once, on exit.

For Docker Desktop bind mounts set `DOTNET_USE_POLLING_FILE_WATCHER=1` (file events only; database polling is unaffected).

## Project conventions worth copying (from real projects)

- **One schema for the API** (`IncludeSchemas: ["myapi"]`); internal helpers in another schema.
- **Organize by feature/domain**, not by migration type. Define endpoints as **repeatable migrations** (`R__<action>_<entity>.sql`) so re-running is idempotent — or as plain `.sql` files under the source glob.
- **Dev codegen:** enable the TypeScript client generator + `.http` export in the dev override file only; commit the generated client.
- **Two-layer caching:** a `Cache-Control` header for the browser + server-side `@cached`/`@cache_profile` for cross-client dedup; match the windows.
- **Cache-key discipline:** include every param that changes the result (and `_user_id` for per-user data); omit `_user_id` for shared results so all users hit one entry. Add a `_cache_bust`/`_param_hash` param to force misses after writes.

## Gotchas

- **`@cached` needs an explicit param list** — bare `@cached` keys on the endpoint only (a common silent bug).
- **Verb ≠ param location** — use `@request_param_type`, not the method, to reason about where params come from / are forwarded.
- **HTTP-type directives** are safest **before the request line**.
- **Passthrough proxy doesn't run the function** — declare proxy-response params (transform mode) if you need the body executed.
- **`HttpClientOptions.Enabled` / `ProxyOptions.Enabled` / `SqlFileSource.Enabled`** must be true for those features. `@cache` on a non-GET HTTP type is ignored with a warning.
- **SQL files:** annotations are in `--`/`/* */` comments; params are named `:name` (preferred — auto-named) or positional `$N` (name via `@param`) — **never mixed in one file** (startup error); a startup Describe error fails boot unless `ErrorMode: Skip`; use `@returns` for statements over not-yet-existing objects (temp tables); `DO` blocks can't take params or return values.
- **A `.sql` file named `*.test.sql` never becomes an endpoint** — `SqlFileSource.SkipPattern` excludes test files by default.
- **Test HTTP block paths must be the FULL path** including `UrlPathPrefix` (`/api/...`) — a bare `/get-users` is a 404 with a warning.
- **`ResponseTempTable.DebugTable` is a dev-only debugging aid** — never enable in CI; combine with `Keep: true` when the run drops its test database.
- After changing an annotation, re-run with `--log:minimallevels:npgsqlrest=debug` (or `--validate`) to confirm it parsed as intended.
