# NpgsqlRest — Full Annotation Reference

Every comment annotation, generated from `npgsqlrest --annotations` (NpgsqlRest v3.20.0).
Annotations apply to both endpoint sources (database routines via `comment on`, and `.sql` files via leading `--` comments). The `@` prefix is optional. Regenerate with `npgsqlrest --annotations`.

A hand-maintained **Supplement** at the end of this file covers annotations the binary does NOT emit — SQL-file statement annotations, plugin annotations (`mcp`, `openapi`), custom-parameter families (`tsclient*`, `dartclient*`, `table_format`), and test-file annotations. **Keep the Supplement when regenerating the section above.**

## `http`

- **Aliases:** http
- **Syntax:** `http [GET|POST|PUT|DELETE|QUERY] [path]`

Enable endpoint and configure HTTP method and/or path. Required (for HTTP exposure) when CommentsMode is OnlyAnnotated (or its alias OnlyWithHttpTag).

## `path`

- **Aliases:** path
- **Syntax:** `path <url_path>`

Override the endpoint URL path.

## `param_type`

- **Aliases:** request_param_type, param_type
- **Syntax:** `param_type [query_string|query|body_json|body]`

Set request parameter type to query string or JSON body.

## `authorize`

- **Aliases:** authorize, authorized, requires_authorization
- **Syntax:** `authorize [role1, role2, ...]`

Require authorization, optionally restricting to specific roles.

## `allow_anonymous`

- **Aliases:** allow_anonymous, anonymous, allow_anon, anon
- **Syntax:** `allow_anonymous`

Allow unauthenticated access to this endpoint.

## `login`

- **Aliases:** login, signin
- **Syntax:** `login`

Mark endpoint as a login/authentication endpoint.

## `logout`

- **Aliases:** logout, signout
- **Syntax:** `logout`

Mark endpoint as a logout endpoint.

## `raw`

- **Aliases:** raw, raw_mode, raw_results
- **Syntax:** `raw`

Return raw results without JSON formatting.

## `separator`

- **Aliases:** separator, raw_separator
- **Syntax:** `separator <value>`

Set the value separator for raw mode output.

## `new_line`

- **Aliases:** new_line, raw_new_line
- **Syntax:** `new_line <value>`

Set the line separator for raw mode output.

## `columns`

- **Aliases:** columns, names, column_names
- **Syntax:** `columns`

Include column names as the first row in raw output.

## `buffer_rows`

- **Aliases:** buffer_rows, buffer
- **Syntax:** `buffer_rows <number>`

Set the number of rows to buffer before sending response.

## `cached`

- **Aliases:** cached
- **Syntax:** `cached [param1, param2, ...]`

Enable response caching, optionally specifying cache key parameters.

## `cache_expires`

- **Aliases:** cache_expires, cache_expires_in
- **Syntax:** `cache_expires <interval>`

Set cache expiration time (PostgreSQL interval format).

## `cache_profile`

- **Aliases:** cache_profile
- **Syntax:** `cache_profile <name>`

Select a named cache profile defined in CacheOptions.Profiles. The profile supplies the cache backend, default expiration, default key parameters, and per-parameter skip conditions. Implies caching even without @cached. Existing @cached and @cache_expires annotations override the profile's defaults. Unknown profile names cause startup to fail.

## `connection_name`

- **Aliases:** connection, connection_name
- **Syntax:** `connection_name <name>`

Use a specific named connection string for this endpoint.

## `timeout`

- **Aliases:** command_timeout, timeout
- **Syntax:** `timeout <interval>`

Set command execution timeout (PostgreSQL interval format).

## `request_headers_mode`

- **Aliases:** request_headers_mode, request_headers
- **Syntax:** `request_headers [ignore|context|parameter]`

Control how HTTP request headers are passed to the routine.

## `request_headers_parameter_name`

- **Aliases:** request_headers_parameter_name, request_headers_param_name, request-headers-param-name
- **Syntax:** `request_headers_parameter_name <name>`

Set the parameter name for request headers when mode is parameter.

## `body_parameter_name`

- **Aliases:** body_parameter_name, body_param_name
- **Syntax:** `body_parameter_name <name>`

Set the parameter name for the JSON body content.

## `response_null_handling`

- **Aliases:** response_null_handling, response_null
- **Syntax:** `response_null [empty_string|null_literal|no_content|204]`

Control how NULL return values are rendered in responses.

## `query_string_null_handling`

- **Aliases:** query_string_null_handling, query_null_handling, query_string_null, query_null
- **Syntax:** `query_null [empty_string|null_literal|ignore]`

Control how NULL query string parameters are handled.

## `security_sensitive`

- **Aliases:** sensitive, security, security_sensitive
- **Syntax:** `security_sensitive`

Mark endpoint as security-sensitive (suppresses logging of parameters).

## `user_context`

- **Aliases:** user_context
- **Syntax:** `user_context`

Pass authenticated user context to the routine via connection settings.

## `user_parameters`

- **Aliases:** user_parameters, user_params
- **Syntax:** `user_parameters`

Map user claims to routine parameters.

## `upload`

- **Aliases:** upload
- **Syntax:** `upload [for handler1, handler2, ...]`

Enable file upload for this endpoint, optionally specifying upload handlers.

## `param`

- **Aliases:** parameter, param
- **Syntax:** `param <name> is hash of <other_name> | param <name> is upload metadata | param <original> <new_name> [type] | param <original> is <new_name> [type] | param <name> type is <type> | param <name> default <value>`

Configure parameter behavior: hash computation, upload metadata binding, rename/retype, retype WITHOUT rename ('param user_id type is integer'), or default value ('param user_id default null'). Rename forms: 'param $1 user_id', 'param $1 user_id integer', 'param $1 is user_id', 'param _old_name better_name'. In SQL files with named (:name) placeholders the parameter is addressed by the placeholder's own name (a leading ':' is tolerated). Works on all endpoint types.

## `sse_path`

- **Aliases:** sse, sse_path, sse_events_path
- **Syntax:** `sse_path [path] [on info|notice|warning]`

Enable Server-Sent Events streaming with optional path and notice level.

## `sse_level`

- **Aliases:** sse_level, sse_events_level
- **Syntax:** `sse_level [info|notice|warning]`

Set the PostgreSQL notice level for SSE events.

## `sse_scope`

- **Aliases:** sse_scope, sse_events_scope
- **Syntax:** `sse_scope [all|authorize|matching] [role1, role2, ...]`

Set the broadcast scope for SSE events.

## `basic_auth`

- **Aliases:** basic_authentication, basic_auth
- **Syntax:** `basic_auth [username] [password]`

Enable HTTP Basic Authentication for this endpoint.

## `basic_auth_realm`

- **Aliases:** basic_authentication_realm, basic_auth_realm, realm
- **Syntax:** `basic_auth_realm <realm>`

Set the authentication realm for Basic Auth challenges.

## `basic_auth_command`

- **Aliases:** basic_authentication_command, basic_auth_command, challenge_command
- **Syntax:** `basic_auth_command <sql_command>`

Set a custom SQL command for Basic Auth credential validation.

## `retry_strategy`

- **Aliases:** retry_strategy_name, retry_strategy, retry
- **Syntax:** `retry_strategy <name>`

Apply a named retry strategy for transient database errors.

## `rate_limiter`

- **Aliases:** rate_limiter_policy_name, rate_limiter_policy, rate_limiter
- **Syntax:** `rate_limiter <policy_name>`

Apply a named rate limiting policy to this endpoint.

## `error_code_policy`

- **Aliases:** error_code_policy_name, error_code_policy, error_code
- **Syntax:** `error_code_policy <name>`

Apply a named error code mapping policy for PostgreSQL error codes.

## `validate`

- **Aliases:** validate, validation
- **Syntax:** `validate <param_name> using <rule_name>`

Add parameter validation using a named validation rule.

## `proxy`

- **Aliases:** proxy, reverse_proxy
- **Syntax:** `proxy [GET|POST|PUT|DELETE|QUERY] [host_url]`

Configure endpoint as a reverse proxy.

## `proxy_out`

- **Aliases:** proxy_out, forward_proxy
- **Syntax:** `proxy_out [GET|POST|PUT|DELETE|QUERY] [host_url]`

Execute function first, then forward result body to upstream proxy service.

## `nested_json`

- **Aliases:** nested, nested_json, nested_composite
- **Syntax:** `nested_json`

Serialize composite type columns as nested JSON objects.

## `tags`

- **Aliases:** for, tags, tag
- **Syntax:** `for tag1, tag2, ...`

Filter endpoint availability by tags.

## `disabled`

- **Aliases:** disabled
- **Syntax:** `disabled [tag1, tag2, ...]`

Disable this endpoint, optionally only for specific tags.

## `enabled`

- **Aliases:** enabled
- **Syntax:** `enabled [tag1, tag2, ...]`

Enable this endpoint, optionally only for specific tags.

## `internal`

- **Aliases:** internal, internal_only
- **Syntax:** `internal`

Mark endpoint as internal-only. Not exposed as an HTTP route, only accessible via self-referencing calls (proxy, HTTP client types).

## `encrypt`

- **Aliases:** encrypt, encrypted, protect, protected
- **Syntax:** `encrypt [param1, param2, ...]`

Encrypt parameter values using the default data protector before sending to PostgreSQL. Without arguments, encrypts all text parameters.

## `decrypt`

- **Aliases:** decrypt, decrypted, unprotect, unprotected
- **Syntax:** `decrypt [column1, column2, ...]`

Decrypt result column values using the default data protector before returning to the client. Without arguments, decrypts all text columns.

## `custom_parameter`

- **Aliases:** 
- **Syntax:** `key = value`

Define a custom parameter as key-value pair (separated by =).

## `header`

- **Aliases:** 
- **Syntax:** `Header-Name: header-value`

Add a response header (separated by :).

## `resultN`

- **Aliases:** result1, result2, result3
- **Syntax:** `resultN <name> | resultN is <name>`

Rename a result key in multi-command SQL file responses. N is the 1-based command index. Example: '@result1 validate' renames the first result from 'result1' to 'validate'. SQL file source only.

## `define_param`

- **Aliases:** define_param
- **Syntax:** `define_param <name> [type]`

Define a virtual parameter that exists for HTTP matching and claim mapping but is NOT bound to the PostgreSQL command. Useful for SQL file endpoints where you need parameters for comment placeholders or claim mapping without referencing them in SQL. Default type is text. SQL file source only.

---

# Supplement — annotations not emitted by `--annotations`

Hand-maintained from the docs (<https://npgsqlrest.github.io/annotations/>). Everything above comes from the binary's annotation registry; the annotations below are equally real but live elsewhere (the SQL-file statement parser, plugins, custom-parameter conventions, the test runner), so `--annotations` does not list them.

> **Maintenance note:** `single`, `void`, `skip`, `returns`, `mcp`, and `openapi` were added to the binary's annotation registry after v3.20.0. When regenerating this file from a newer release they will appear in the generated section above — remove their sections from this supplement then. The custom-parameter families and test-file annotations stay hand-maintained.

## SQL-file statement annotations (SQL file source only)

Positional: placed on a line before a statement (or inline after the previous `;`), they apply to the NEXT statement — same placement rule as `resultN`.

### `single`

- **Syntax:** `single`

That statement returns a single object instead of an array.

### `skip`

- **Syntax:** `skip`

Run the statement but exclude its result from the response.

### `void`

- **Aliases:** void, void_result
- **Syntax:** `void`

The whole endpoint returns **204 No Content**; all statements run for side effects only.

### `returns`

- **Syntax:** `returns <composite_type> | returns <scalar_type> | returns void`

Skip the startup Describe for that statement and resolve result columns from the given type instead (composite type name, any built-in scalar type for a single-column result, or `void`). Needed when a statement references objects that don't exist at startup (e.g. a temp table created in a `DO` block).

## Plugin annotations

### `mcp` (3.17.0+)

- **Syntax:** `mcp [description]` · `mcp_description <text>` (alias `mcp_desc`) · `mcp_name <name>`

Expose the routine as an MCP tool. A bare `@mcp` with **no** HTTP tag = MCP-only (internal, no REST route). Description precedence: `@mcp_description` > inline `@mcp <text>` > comment prose fallback. No-op when the MCP plugin isn't loaded or `McpOptions.Enabled` is false.

### `openapi` (3.15.0+)

- **Syntax:** `openapi [hide|hidden|ignore]` · `openapi tag <name>` · `openapi tags <a>, <b>, ...`

Per-routine OpenAPI document control: hide the endpoint from the spec (default action; the endpoint itself is unaffected), or replace its default schema-name tag with custom tags (casing preserved). No-op when the OpenAPI plugin isn't loaded.

## Custom-parameter families (`@key = value` — the `=` is REQUIRED)

### TypeScript client (`tsclient*`)

| Key | Effect |
|-----|--------|
| `tsclient` | `off`/`false`/`disabled`/`0` — exclude endpoint from the generated TS client |
| `tsclient_module` | group endpoints with the same module name into one file |
| `tsclient_events` | enable/disable the SSE events parameter |
| `tsclient_parse_url` / `tsclient_parse_request` | enable/disable those generated-function parameters |
| `tsclient_status_code` | include status code in the return value |
| `tsclient_export_url` | `true` — export a URL constant regardless of global `ExportUrls` |
| `tsclient_hooks` (3.20.0+) | `off` — exclude from generated TanStack Query hooks only |
| `tsclient_url_only` | `true` — only URL constant + request interface (no fetch function); implies `tsclient_export_url` |

### Dart client (`dartclient*`)

Same shape as `tsclient*`: `dartclient` (off), `dartclient_module` (falls back to `tsclient_module`), `dartclient_status_code`, `dartclient_events`, `dartclient_parse_url`, `dartclient_parse_request`, `dartclient_export_url`, `dartclient_url_only`.

### Table format (`table_format`)

| Key | Effect |
|-----|--------|
| `table_format` | `html` (HTML table) or `excel` (`.xlsx` download); unrecognized value → warning + JSON fallback |
| `excel_file_name` | download filename (excel only; defaults to routine name) |
| `excel_sheet` | worksheet name (excel only; defaults to routine name, max 31 chars) |

Values accept `{param}` placeholders — e.g. `@table_format = {_format}` lets the caller pick the output format.

## Test-file annotations (`*.test.sql`, test runner only — not endpoint annotations)

**File header** (leading `--` comments before the first statement or HTTP block):

- `-- @setup <StepName | inline step>` — run-once setup step
- `-- @teardown <StepName | inline step>` — guaranteed teardown step
- `-- @connection <Name>` — run the file on a specific named connection
- `-- @tag a, b` — tags for `--testrunner:tag` / `--testrunner:excludetag` selection

**Inside HTTP block comments** (`/* METHOD /full/path ... */`):

- `# @claim name=value` — repeatable; any claim = authenticated principal, none = anonymous
- `# @response <name>` — capture the response into a custom temp table instead of `_response`
