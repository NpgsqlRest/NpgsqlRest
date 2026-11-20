# NpgsqlRest — Full Annotation Reference

Every comment annotation and custom parameter, generated from `npgsqlrest --annotations` (NpgsqlRest v3.22.0): what core reads and what every plugin and handler of the client reads. Regenerate with `npgsqlrest --annotations`.

Each entry says who reads it (**Owner**), how it is written (**Form**) and where it is read (**Where**). The `@` prefix is optional on endpoint annotations. In an endpoint comment, a line starting with `@` that nothing reads is not published as the description, and an unknown `@key = value` logs a warning at startup; `npgsqlrest --endpoints` lists both per endpoint (`unknownAnnotations`, `unknownCustomParameters`, `diagnostics`).

# Endpoint annotations (core)

## `http`

- **Aliases:** http
- **Syntax:** `http [GET|POST|PUT|DELETE|QUERY] [path]`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** text
- **Example:** `HTTP GET /users/{id}`

Enable endpoint and configure HTTP method and/or path. Required (for HTTP exposure) when CommentsMode is OnlyAnnotated (or its alias OnlyWithHttpTag).

## `path`

- **Aliases:** path
- **Syntax:** `path <url_path>`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** path
- **Example:** `path /users/{id}`

Override the endpoint URL path.

## `param_type`

- **Aliases:** request_param_type, param_type
- **Syntax:** `param_type [query_string|query|body_json|body]`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** enum (query_string, query, body_json, body)
- **Example:** `param_type body_json`

Set request parameter type to query string or JSON body.

## `authorize`

- **Aliases:** authorize, authorized, requires_authorization
- **Syntax:** `authorize [role1, role2, user:name, id:user_id, ...]`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** list
- **Example:** `authorize admin, user:alice`

Require authorization, optionally restricting access. Plain values are role names (role claim); user:name matches the user name claim and id:value the user id claim.

## `allow_anonymous`

- **Aliases:** allow_anonymous, anonymous, allow_anon, anon
- **Syntax:** `allow_anonymous`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** none
- **Example:** `allow_anonymous`

Allow unauthenticated access to this endpoint.

## `login`

- **Aliases:** login, signin
- **Syntax:** `login`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** none
- **Example:** `login`

Mark endpoint as a login/authentication endpoint.

## `logout`

- **Aliases:** logout, signout
- **Syntax:** `logout`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** none
- **Example:** `logout`

Mark endpoint as a logout endpoint.

## `raw`

- **Aliases:** raw, raw_mode, raw_results
- **Syntax:** `raw`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `false`
- **Example:** `raw`

Return raw results without JSON formatting.

## `separator`

- **Aliases:** separator, raw_separator
- **Syntax:** `separator <value>`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** text
- **Example:** `separator ,`

Set the value separator for raw mode output.

## `new_line`

- **Aliases:** new_line, raw_new_line
- **Syntax:** `new_line <value>`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** text
- **Example:** `new_line \n`

Set the line separator for raw mode output.

## `columns`

- **Aliases:** columns, names, column_names
- **Syntax:** `columns`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `false`
- **Example:** `columns`

Include column names as the first row in raw output.

## `buffer_rows`

- **Aliases:** buffer_rows, buffer
- **Syntax:** `buffer_rows <number>`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** integer · **Default:** `25`
- **Example:** `buffer_rows 100`

Set the number of rows to buffer before sending response.

## `cached`

- **Aliases:** cached
- **Syntax:** `cached [param1, param2, ...]`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** list
- **Example:** `cached _user_id`

Enable response caching, optionally specifying cache key parameters.

## `cache_expires`

- **Aliases:** cache_expires, cache_expires_in
- **Syntax:** `cache_expires <interval>`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** interval
- **Example:** `cache_expires 5 minutes`

Set cache expiration time (PostgreSQL interval format).

## `cache_profile`

- **Aliases:** cache_profile
- **Syntax:** `cache_profile <name>`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** name
- **Example:** `cache_profile fast_memory`

Select a named cache profile defined in CacheOptions.Profiles. The profile supplies the cache backend, default expiration, default key parameters, and per-parameter skip conditions. Implies caching even without @cached. Existing @cached and @cache_expires annotations override the profile's defaults. Unknown profile names cause startup to fail.

## `connection_name`

- **Aliases:** connection, connection_name
- **Syntax:** `connection_name <name>`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** name
- **Example:** `connection read_replica`

Use a specific named connection string for this endpoint.

## `timeout`

- **Aliases:** command_timeout, timeout
- **Syntax:** `timeout <interval>`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** interval
- **Example:** `timeout 30s`

Set command execution timeout (PostgreSQL interval format).

## `request_headers_mode`

- **Aliases:** request_headers_mode, request_headers
- **Syntax:** `request_headers [ignore|context|parameter]`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** enum (ignore, context, parameter)
- **Example:** `request_headers context`

Control how HTTP request headers are passed to the routine.

## `request_headers_parameter_name`

- **Aliases:** request_headers_parameter_name, request_headers_param_name, request-headers-param-name
- **Syntax:** `request_headers_parameter_name <name>`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** name
- **Example:** `request_headers_parameter_name _headers`

Set the parameter name for request headers when mode is parameter.

## `body_parameter_name`

- **Aliases:** body_parameter_name, body_param_name
- **Syntax:** `body_parameter_name <name>`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** name
- **Example:** `body_parameter_name _body`

Set the parameter name for the JSON body content.

## `response_null_handling`

- **Aliases:** response_null_handling, response_null
- **Syntax:** `response_null [empty_string|null_literal|no_content|204]`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** enum (empty_string, null_literal, no_content, 204) · **Default:** `empty_string`
- **Example:** `response_null no_content`

Control how NULL return values are rendered in responses.

## `query_string_null_handling`

- **Aliases:** query_string_null_handling, query_null_handling, query_string_null, query_null
- **Syntax:** `query_null [empty_string|null_literal|ignore]`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** enum (empty_string, null_literal, ignore) · **Default:** `ignore`
- **Example:** `query_null empty_string`

Control how NULL query string parameters are handled.

## `security_sensitive`

- **Aliases:** sensitive, security, security_sensitive
- **Syntax:** `security_sensitive`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** none
- **Example:** `security_sensitive`

Mark endpoint as security-sensitive (suppresses logging of parameters).

## `user_context`

- **Aliases:** user_context
- **Syntax:** `user_context`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `false`
- **Example:** `user_context`

Pass authenticated user context to the routine via connection settings.

## `user_parameters`

- **Aliases:** user_parameters, user_params
- **Syntax:** `user_parameters`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `false`
- **Example:** `user_parameters`

Map user claims to routine parameters.

## `upload`

- **Aliases:** upload
- **Syntax:** `upload [for handler1, handler2, ...]`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** list
- **Example:** `upload for file_system`

Enable file upload for this endpoint, optionally specifying upload handlers.

## `param`

- **Aliases:** parameter, param
- **Syntax:** `param <name> is hash of <other_name> | param <name> is upload metadata | param <original> <new_name> [type] | param <original> is <new_name> [type] | param <name> type is <type> | param <name> default <value>`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** text
- **Example:** `param $1 user_id integer`

Configure parameter behavior: hash computation, upload metadata binding, rename/retype, retype WITHOUT rename ('param user_id type is integer'), or default value ('param user_id default null'). Rename forms: 'param $1 user_id', 'param $1 user_id integer', 'param $1 is user_id', 'param _old_name better_name'. In SQL files with named (:name) placeholders the parameter is addressed by the placeholder's own name (a leading ':' is tolerated). Works on all endpoint types.

## `sse_path`

- **Aliases:** sse, sse_path, sse_events_path
- **Syntax:** `sse_path [path] [on info|notice|warning]`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** path
- **Example:** `sse /events on notice`

Enable Server-Sent Events streaming with optional path and notice level.

## `sse_level`

- **Aliases:** sse_level, sse_events_level
- **Syntax:** `sse_level [info|notice|warning]`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** enum (info, notice, warning)
- **Example:** `sse_level notice`

Set the PostgreSQL notice level for SSE events.

## `sse_scope`

- **Aliases:** sse_scope, sse_events_scope
- **Syntax:** `sse_scope [all|authorize|matching] [role1, role2, user:name, id:user_id, ...]`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** text
- **Example:** `sse_scope authorize admin`

Set the broadcast scope for SSE events. Authorize values: plain = role, user:name = user name claim, id:value = user id claim.

## `basic_auth`

- **Aliases:** basic_authentication, basic_auth
- **Syntax:** `basic_auth [username] [password]`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** text
- **Example:** `basic_auth`

Enable HTTP Basic Authentication for this endpoint.

## `basic_auth_realm`

- **Aliases:** basic_authentication_realm, basic_auth_realm, realm
- **Syntax:** `basic_auth_realm <realm>`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** text
- **Example:** `basic_auth_realm Reports`

Set the authentication realm for Basic Auth challenges.

## `basic_auth_command`

- **Aliases:** basic_authentication_command, basic_auth_command, challenge_command
- **Syntax:** `basic_auth_command <sql_command>`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** sql
- **Example:** `basic_auth_command = select * from check_basic_auth($1, $2)`

Set a custom SQL command for Basic Auth credential validation.

## `retry_strategy`

- **Aliases:** retry_strategy_name, retry_strategy, retry
- **Syntax:** `retry_strategy <name>`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** name
- **Example:** `retry_strategy default`

Apply a named retry strategy for transient database errors.

## `rate_limiter`

- **Aliases:** rate_limiter_policy_name, rate_limiter_policy, rate_limiter
- **Syntax:** `rate_limiter <policy_name>`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** name
- **Example:** `rate_limiter fixed`

Apply a named rate limiting policy to this endpoint.

## `error_code_policy`

- **Aliases:** error_code_policy_name, error_code_policy, error_code
- **Syntax:** `error_code_policy <name>`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** name
- **Example:** `error_code_policy default`

Apply a named error code mapping policy for PostgreSQL error codes.

## `validate`

- **Aliases:** validate, validation
- **Syntax:** `validate <param_name> using <rule_name>`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** text
- **Example:** `validate _email using email`

Add parameter validation using a named validation rule.

## `proxy`

- **Aliases:** proxy, reverse_proxy
- **Syntax:** `proxy [GET|POST|PUT|DELETE|QUERY] [host_url]`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** text
- **Example:** `proxy GET https://api.example.com/users`

Configure endpoint as a reverse proxy.

## `proxy_out`

- **Aliases:** proxy_out, forward_proxy
- **Syntax:** `proxy_out [GET|POST|PUT|DELETE|QUERY] [host_url]`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** text
- **Example:** `proxy_out POST https://api.example.com/render`

Execute function first, then forward result body to upstream proxy service.

## `nested_json`

- **Aliases:** nested, nested_json, nested_composite
- **Syntax:** `nested_json`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** none
- **Example:** `nested_json`

Serialize composite type columns as nested JSON objects.

## `tags`

- **Aliases:** for, tags, tag
- **Syntax:** `for tag1, tag2, ...`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment
- **Value:** list
- **Example:** `for function, stable`

Filter endpoint availability by tags.

## `disabled`

- **Aliases:** disabled
- **Syntax:** `disabled [tag1, tag2, ...]`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** list
- **Example:** `disabled`

Disable this endpoint, optionally only for specific tags.

## `enabled`

- **Aliases:** enabled
- **Syntax:** `enabled [tag1, tag2, ...]`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** list
- **Example:** `enabled`

Enable this endpoint, optionally only for specific tags.

## `internal`

- **Aliases:** internal, internal_only
- **Syntax:** `internal`
- **Owner:** core · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** none
- **Example:** `internal`

Mark endpoint as internal-only. Not exposed as an HTTP route, only accessible via self-referencing calls (proxy, HTTP client types).

## `encrypt`

- **Aliases:** encrypt, encrypted, protect, protected
- **Syntax:** `encrypt [param1, param2, ...]`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** list
- **Example:** `encrypt _card_number`

Encrypt parameter values using the default data protector before sending to PostgreSQL. Without arguments, encrypts all text parameters.

## `decrypt`

- **Aliases:** decrypt, decrypted, unprotect, unprotected
- **Syntax:** `decrypt [column1, column2, ...]`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** list
- **Example:** `decrypt card_number`

Decrypt result column values using the default data protector before returning to the client. Without arguments, decrypts all text columns.

## `custom_parameter`

- **Syntax:** `key = value`
- **Owner:** core · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** text
- **Example:** `my_key = my value`

Define a custom parameter as key-value pair (separated by =).

## `header`

- **Syntax:** `Header-Name: header-value`
- **Owner:** core · **Form:** a line of its own shape · **Where:** routine comment, SQL file
- **Value:** text
- **Example:** `Content-Type: text/csv`

Add a response header (separated by :).

## `result`

- **Aliases:** result
- **Syntax:** `result <name> | result is <name>`
- **Owner:** core · **Form:** keyword line · **Where:** SQL file, per statement
- **Value:** name
- **Example:** `result users`

Name a statement's result in the response of a SQL file with several statements (the default names are result1, result2 ...). Positional: applies to the next statement, or, written after the semicolon on the same line, to the statement that line ends.

## `define_param`

- **Aliases:** define_param
- **Syntax:** `define_param <name> [type]`
- **Owner:** core · **Form:** keyword line · **Where:** SQL file
- **Value:** text
- **Example:** `define_param tenant_id integer`

Define a virtual parameter that exists for HTTP matching and claim mapping but is NOT bound to the PostgreSQL command. Useful for SQL file endpoints where you need parameters for comment placeholders or claim mapping without referencing them in SQL. Default type is text. SQL file source only.

## `single`

- **Aliases:** single, single_record, single_result
- **Syntax:** `single`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file, SQL file, per statement
- **Value:** boolean · **Default:** `false`
- **Example:** `single`

Return only the first row as a JSON object instead of a JSON array. In multi-command SQL files the annotation is positional: it applies to the next statement (or, inline after the semicolon, to the statement on that line).

## `void`

- **Aliases:** void, void_result
- **Syntax:** `void`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `false`
- **Example:** `void`

Return no content (204 No Content); results are discarded. In multi-command SQL files all statements still execute for side effects.

## `skip`

- **Aliases:** skip, skip_result, no_result
- **Syntax:** `skip`
- **Owner:** core · **Form:** keyword line · **Where:** SQL file, per statement
- **Value:** none
- **Example:** `skip`

Execute the statement but exclude its result from the response. Positional: applies to the next statement (or, inline after the semicolon, to the statement on that line). SQL file source only.

## `returns`

- **Aliases:** returns
- **Syntax:** `returns <composite_type> | returns <scalar_type> | returns void`
- **Owner:** core · **Form:** keyword line · **Where:** SQL file, per statement
- **Value:** name
- **Example:** `returns my_schema.user_row`

Skip the startup Describe for a statement and resolve result columns from the given type instead: a composite type name (schema-qualified or not), a built-in scalar type (single-column result), or void. Needed when a statement references objects that do not exist at startup (e.g. a temp table created in a DO block). SQL file source only.

## `sse_publish`

- **Aliases:** sse_publish, sse_events_publish
- **Syntax:** `sse_publish [on info|notice|warning]`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** text
- **Example:** `sse_publish on notice`

Publish this routine's RAISE messages of the given level (default: the configured level) as Server-Sent Events, without giving the endpoint an event stream of its own. Subscribers are endpoints with sse or sse_subscribe.

## `sse_subscribe`

- **Aliases:** sse_subscribe, sse_events_subscribe
- **Syntax:** `sse_subscribe [path] [on info|notice|warning]`
- **Owner:** core · **Form:** keyword line or `key = value` · **Where:** routine comment, SQL file
- **Value:** path
- **Example:** `sse_subscribe /events`

Give this endpoint a Server-Sent Events stream that clients subscribe to, without publishing this routine's own RAISE messages. Without a path, the path is the level's name.

## `table_format`

- **Aliases:** table_format
- **Syntax:** `table_format = <format>`
- **Owner:** core · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** name
- **Example:** `table_format = html`

Return the rows in a table format instead of JSON: a key of the configured table format handlers (the npgsqlrest client has html and excel when TableFormatOptions is enabled).

# Upload handler parameters

## `stop_after_first_success` (upload)

- **Aliases:** stop_after_first_success, large_object_stop_after_first_success, file_system_stop_after_first_success, csv_stop_after_first_success, excel_stop_after_first_success
- **Syntax:** `stop_after_first_success = <true|false>`
- **Owner:** upload · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `false`
- **Example:** `stop_after_first_success = true`

With several upload handlers: a file that one handler processed successfully is skipped by the handlers after it.

## `included_mime_types` (upload)

- **Aliases:** included_mime_types, large_object_included_mime_types, file_system_included_mime_types, csv_included_mime_types, excel_included_mime_types
- **Syntax:** `included_mime_types = <list>`
- **Owner:** upload · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** list
- **Example:** `included_mime_types = image/*, application/pdf`

Accept only files whose MIME type matches one of these patterns (* and ? are wildcards). Others are rejected.

## `excluded_mime_types` (upload)

- **Aliases:** excluded_mime_types, large_object_excluded_mime_types, file_system_excluded_mime_types, csv_excluded_mime_types, excel_excluded_mime_types
- **Syntax:** `excluded_mime_types = <list>`
- **Owner:** upload · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** list
- **Example:** `excluded_mime_types = application/x-msdownload`

Reject files whose MIME type matches any of these patterns (* and ? are wildcards).

## `buffer_size` (upload)

- **Aliases:** buffer_size, large_object_buffer_size, file_system_buffer_size, csv_buffer_size, excel_buffer_size
- **Syntax:** `buffer_size = <number>`
- **Owner:** upload · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** integer · **Default:** `8192`
- **Example:** `buffer_size = 65536`

Size in bytes of the chunks a file is read and written in (large_object and file_system).

## `fallback_handler` (upload)

- **Aliases:** fallback_handler, csv_fallback_handler, excel_fallback_handler
- **Syntax:** `fallback_handler = <name>`
- **Owner:** upload · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** name
- **Example:** `fallback_handler = file_system`

The upload handler that takes over when csv or excel cannot process the file.

## `oid` (upload:large_object)

- **Aliases:** oid, large_object_oid
- **Syntax:** `oid = <number>`
- **Owner:** upload:large_object · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** integer
- **Example:** `oid = {_oid}`

The OID of the large object to create. Without it PostgreSQL assigns one.

## `check_text` (upload:large_object)

- **Aliases:** check_text, large_object_check_text
- **Syntax:** `check_text = <true|false>`
- **Owner:** upload:large_object · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `false`
- **Example:** `check_text = true`

Reject files that are not text (binary content).

## `check_image` (upload:large_object)

- **Aliases:** check_image, large_object_check_image
- **Syntax:** `check_image = <value>`
- **Owner:** upload:large_object · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** text · **Default:** `false`
- **Example:** `check_image = jpg, png`

Reject files that are not images, by their first bytes: true for any supported type, or a list of jpg, jpeg, png, gif, bmp, tiff, webp.

## `test_buffer_size` (upload:large_object)

- **Aliases:** test_buffer_size, large_object_test_buffer_size
- **Syntax:** `test_buffer_size = <number>`
- **Owner:** upload:large_object · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** integer · **Default:** `4096`
- **Example:** `test_buffer_size = 8192`

How many bytes of the file the text check reads.

## `non_printable_threshold` (upload:large_object)

- **Aliases:** non_printable_threshold, large_object_non_printable_threshold
- **Syntax:** `non_printable_threshold = <number>`
- **Owner:** upload:large_object · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** integer · **Default:** `5`
- **Example:** `non_printable_threshold = 0`

How many non-printable bytes the text check tolerates before the file counts as binary.

## `path` (upload:file_system)

- **Aliases:** path, file_system_path
- **Syntax:** `path = <path>`
- **Owner:** upload:file_system · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** path
- **Example:** `path = /var/uploads/{_folder}`

The directory the file is saved in. Default: the configured FileSystemPath.

## `file` (upload:file_system)

- **Aliases:** file, file_system_file
- **Syntax:** `file = <value>`
- **Owner:** upload:file_system · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** text
- **Example:** `file = {_name}.pdf`

The name the file is saved under (relative, no '..'). Default: the uploaded file's own name. With unique_name only its extension is used.

## `unique_name` (upload:file_system)

- **Aliases:** unique_name, file_system_unique_name
- **Syntax:** `unique_name = <true|false>`
- **Owner:** upload:file_system · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `true`
- **Example:** `unique_name = false`

Save the file under a generated unique name that keeps the extension.

## `create_path` (upload:file_system)

- **Aliases:** create_path, file_system_create_path
- **Syntax:** `create_path = <true|false>`
- **Owner:** upload:file_system · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean
- **Example:** `create_path = true`

Create the directory when it does not exist. Default: the configured FileSystemCreatePathIfNotExists.

## `check_text` (upload:file_system)

- **Aliases:** check_text, file_system_check_text
- **Syntax:** `check_text = <true|false>`
- **Owner:** upload:file_system · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `false`
- **Example:** `check_text = true`

Reject files that are not text (binary content).

## `check_image` (upload:file_system)

- **Aliases:** check_image, file_system_check_image
- **Syntax:** `check_image = <value>`
- **Owner:** upload:file_system · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** text · **Default:** `false`
- **Example:** `check_image = true`

Reject files that are not images, by their first bytes: true for any supported type, or a list of jpg, jpeg, png, gif, bmp, tiff, webp.

## `test_buffer_size` (upload:file_system)

- **Aliases:** test_buffer_size, file_system_test_buffer_size
- **Syntax:** `test_buffer_size = <number>`
- **Owner:** upload:file_system · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** integer · **Default:** `4096`
- **Example:** `test_buffer_size = 8192`

How many bytes of the file the text check reads.

## `non_printable_threshold` (upload:file_system)

- **Aliases:** non_printable_threshold, file_system_non_printable_threshold
- **Syntax:** `non_printable_threshold = <number>`
- **Owner:** upload:file_system · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** integer · **Default:** `5`
- **Example:** `non_printable_threshold = 0`

How many non-printable bytes the text check tolerates before the file counts as binary.

## `check_format` (upload:csv)

- **Aliases:** check_format, csv_check_format
- **Syntax:** `check_format = <true|false>`
- **Owner:** upload:csv · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `true`
- **Example:** `check_format = false`

Check that the file is text with at least one line break before it is processed.

## `test_buffer_size` (upload:csv)

- **Aliases:** test_buffer_size, csv_test_buffer_size
- **Syntax:** `test_buffer_size = <number>`
- **Owner:** upload:csv · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** integer · **Default:** `4096`
- **Example:** `test_buffer_size = 8192`

How many bytes of the file the format check reads.

## `non_printable_threshold` (upload:csv)

- **Aliases:** non_printable_threshold, csv_non_printable_threshold
- **Syntax:** `non_printable_threshold = <number>`
- **Owner:** upload:csv · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** integer · **Default:** `5`
- **Example:** `non_printable_threshold = 0`

How many non-printable bytes the format check tolerates before the file counts as binary.

## `delimiters` (upload:csv)

- **Aliases:** delimiters, csv_delimiters
- **Syntax:** `delimiters = <value>`
- **Owner:** upload:csv · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** text · **Default:** `,`
- **Example:** `delimiters = ;\t`

The field delimiter characters; every character of the value is one. \t is a tab.

## `has_fields_enclosed_in_quotes` (upload:csv)

- **Aliases:** has_fields_enclosed_in_quotes, csv_has_fields_enclosed_in_quotes
- **Syntax:** `has_fields_enclosed_in_quotes = <true|false>`
- **Owner:** upload:csv · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `true`
- **Example:** `has_fields_enclosed_in_quotes = false`

Fields may be enclosed in double quotes.

## `set_white_space_to_null` (upload:csv)

- **Aliases:** set_white_space_to_null, csv_set_white_space_to_null
- **Syntax:** `set_white_space_to_null = <true|false>`
- **Owner:** upload:csv · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `true`
- **Example:** `set_white_space_to_null = false`

Empty and whitespace-only fields are passed as NULL.

## `row_command` (upload:csv)

- **Aliases:** row_command, csv_row_command
- **Syntax:** `row_command = <sql>`
- **Owner:** upload:csv · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** sql · **Default:** `call process_csv_row($1,$2,$3,$4)`
- **Example:** `row_command = select import_row($1, $2, $3, $4)`

The SQL command run for every row: $1 is the row number (from 1), $2 the fields as text[], $3 the previous row's result, $4 the upload metadata. {placeholders} are never substituted in it.

## `sheet_name` (upload:excel)

- **Aliases:** sheet_name, excel_sheet_name
- **Syntax:** `sheet_name = <value>`
- **Owner:** upload:excel · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** text
- **Example:** `sheet_name = Orders`

The sheet to process (case-insensitive). Default: the first sheet. Ignored with all_sheets.

## `all_sheets` (upload:excel)

- **Aliases:** all_sheets, excel_all_sheets
- **Syntax:** `all_sheets = <true|false>`
- **Owner:** upload:excel · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `false`
- **Example:** `all_sheets = true`

Process every sheet of the workbook.

## `time_format` (upload:excel)

- **Aliases:** time_format, excel_time_format
- **Syntax:** `time_format = <value>`
- **Owner:** upload:excel · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** text · **Default:** `HH:mm:ss`
- **Example:** `time_format = HH:mm`

The .NET format that cells holding only a time are passed in.

## `date_format` (upload:excel)

- **Aliases:** date_format, excel_date_format
- **Syntax:** `date_format = <value>`
- **Owner:** upload:excel · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** text · **Default:** `yyyy-MM-dd`
- **Example:** `date_format = dd.MM.yyyy`

The .NET format that cells holding a date without a time are passed in.

## `datetime_format` (upload:excel)

- **Aliases:** datetime_format, excel_datetime_format
- **Syntax:** `datetime_format = <value>`
- **Owner:** upload:excel · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** text · **Default:** `yyyy-MM-dd HH:mm:ss`
- **Example:** `datetime_format = yyyy-MM-ddTHH:mm:ss`

The .NET format that cells holding a date and a time are passed in.

## `row_is_json` (upload:excel)

- **Aliases:** row_is_json, excel_row_is_json
- **Syntax:** `row_is_json = <true|false>`
- **Owner:** upload:excel · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `false`
- **Example:** `row_is_json = true`

Pass the row ($2) as a JSON object keyed by cell reference ({"A1": ...}) instead of text[].

## `row_command` (upload:excel)

- **Aliases:** row_command, excel_row_command
- **Syntax:** `row_command = <sql>`
- **Owner:** upload:excel · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** sql · **Default:** `call process_excel_row($1,$2,$3,$4)`
- **Example:** `row_command = select import_row($1, $2, $3, $4)`

The SQL command run for every non-empty row: $1 is the row number, $2 the cells, $3 the previous row's result, $4 the upload metadata. {placeholders} are never substituted in it.

# Table format parameters

## `excel_file_name` (table_format:excel)

- **Aliases:** excel_file_name
- **Syntax:** `excel_file_name = <value>`
- **Owner:** table_format:excel · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** text
- **Example:** `excel_file_name = orders-{_year}`

The file name of the Excel download (.xlsx is added when missing). Default: the routine's name.

## `excel_sheet` (table_format:excel)

- **Aliases:** excel_sheet
- **Syntax:** `excel_sheet = <value>`
- **Owner:** table_format:excel · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** text
- **Example:** `excel_sheet = Orders`

The name of the worksheet (31 characters at most). Default: the ExcelSheetName setting, else the routine's name.

# TypeScript client (tsclient)

## `tsclient`

- **Aliases:** tsclient
- **Syntax:** `tsclient = <true|false>`
- **Owner:** tsclient · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `true`
- **Example:** `tsclient = false`

Set to false to leave this endpoint out of the generated TypeScript client. Accepted values: true, on, enabled, 1 and false, off, disabled, 0.

## `tsclient_module`

- **Aliases:** tsclient_module
- **Syntax:** `tsclient_module = <name>`
- **Owner:** tsclient · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** name
- **Example:** `tsclient_module = users`

The module (file) this endpoint's TypeScript code is generated into; endpoints with the same module share a file. SQL file endpoints get their directory name by default.

## `tsclient_events`

- **Aliases:** tsclient_events
- **Syntax:** `tsclient_events = <true|false>`
- **Owner:** tsclient · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `true`
- **Example:** `tsclient_events = false`

Set to false to leave the Server-Sent Events parameters out of this endpoint's generated function.

## `tsclient_parse_url`

- **Aliases:** tsclient_parse_url
- **Syntax:** `tsclient_parse_url = <true|false>`
- **Owner:** tsclient · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean
- **Example:** `tsclient_parse_url = true`

Add (or remove) the parseUrl parameter, a callback that may change the request URL. Default: the IncludeParseUrlParam option.

## `tsclient_parse_request`

- **Aliases:** tsclient_parse_request
- **Syntax:** `tsclient_parse_request = <true|false>`
- **Owner:** tsclient · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean
- **Example:** `tsclient_parse_request = true`

Add (or remove) the parseRequest parameter, a callback that may change the request before it is sent. Default: the IncludeParseRequestParam option.

## `tsclient_status_code`

- **Aliases:** tsclient_status_code
- **Syntax:** `tsclient_status_code = <true|false>`
- **Owner:** tsclient · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean
- **Example:** `tsclient_status_code = true`

Return the HTTP status code together with the response. Default: the IncludeStatusCode option.

## `tsclient_export_url`

- **Aliases:** tsclient_export_url
- **Syntax:** `tsclient_export_url = <true|false>`
- **Owner:** tsclient · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean
- **Example:** `tsclient_export_url = true`

Also export a function that builds this endpoint's URL. Default: the ExportUrls option.

## `tsclient_url_only`

- **Aliases:** tsclient_url_only
- **Syntax:** `tsclient_url_only = <true|false>`
- **Owner:** tsclient · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `false`
- **Example:** `tsclient_url_only = true`

Generate only the URL function and the request type for this endpoint, no fetch function.

## `tsclient_hooks`

- **Aliases:** tsclient_hooks
- **Syntax:** `tsclient_hooks = <true|false>`
- **Owner:** tsclient · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `true`
- **Example:** `tsclient_hooks = false`

Set to false to leave this endpoint out of the generated React Query hooks.

# Dart client (dartclient)

## `dartclient`

- **Aliases:** dartclient
- **Syntax:** `dartclient = <true|false>`
- **Owner:** dartclient · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `true`
- **Example:** `dartclient = false`

Set to false to leave this endpoint out of the generated Dart client. Accepted values: true, on, enabled, 1 and false, off, disabled, 0.

## `dartclient_module`

- **Aliases:** dartclient_module
- **Syntax:** `dartclient_module = <name>`
- **Owner:** dartclient · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** name
- **Example:** `dartclient_module = users`

The module (file) this endpoint's Dart code is generated into; endpoints with the same module share a file. Without it, dartclient falls back to tsclient_module.

## `dartclient_events`

- **Aliases:** dartclient_events
- **Syntax:** `dartclient_events = <true|false>`
- **Owner:** dartclient · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `true`
- **Example:** `dartclient_events = false`

Set to false to leave the Server-Sent Events parameters out of this endpoint's generated function.

## `dartclient_parse_url`

- **Aliases:** dartclient_parse_url
- **Syntax:** `dartclient_parse_url = <true|false>`
- **Owner:** dartclient · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean
- **Example:** `dartclient_parse_url = true`

Add (or remove) the parseUrl parameter, a callback that may change the request URL. Default: the IncludeParseUrlParam option.

## `dartclient_parse_request`

- **Aliases:** dartclient_parse_request
- **Syntax:** `dartclient_parse_request = <true|false>`
- **Owner:** dartclient · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean
- **Example:** `dartclient_parse_request = true`

Add (or remove) the parseRequest parameter, a callback that may change the request before it is sent. Default: the IncludeParseRequestParam option.

## `dartclient_status_code`

- **Aliases:** dartclient_status_code
- **Syntax:** `dartclient_status_code = <true|false>`
- **Owner:** dartclient · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean
- **Example:** `dartclient_status_code = true`

Return the HTTP status code together with the response. Default: the IncludeStatusCode option.

## `dartclient_export_url`

- **Aliases:** dartclient_export_url
- **Syntax:** `dartclient_export_url = <true|false>`
- **Owner:** dartclient · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean
- **Example:** `dartclient_export_url = true`

Also export a function that builds this endpoint's URL. Default: the ExportUrls option.

## `dartclient_url_only`

- **Aliases:** dartclient_url_only
- **Syntax:** `dartclient_url_only = <true|false>`
- **Owner:** dartclient · **Form:** `key = value` · **Where:** routine comment, SQL file
- **Value:** boolean · **Default:** `false`
- **Example:** `dartclient_url_only = true`

Generate only the URL function and the request type for this endpoint, no fetch function.

# MCP

## `mcp`

- **Aliases:** mcp
- **Syntax:** `mcp [description]`
- **Owner:** mcp · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** text
- **Example:** `mcp Returns the orders of one customer`

Expose the routine as an MCP tool (Model Context Protocol). Text after the keyword is the tool's description. Without an HTTP tag the routine becomes an MCP-only tool with no public HTTP route. Description precedence: mcp_description, then this text, then the comment's prose.

## `mcp_name`

- **Aliases:** mcp_name
- **Syntax:** `mcp_name <name>`
- **Owner:** mcp · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** name
- **Example:** `mcp_name customer_orders`

Expose the routine as an MCP tool under this name (one word). Default: the routine's name.

## `mcp_description`

- **Aliases:** mcp_description, mcp_desc
- **Syntax:** `mcp_description <text>`
- **Owner:** mcp · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** text
- **Example:** `mcp_description Returns the orders of one customer, newest first`

Expose the routine as an MCP tool with this description, which wins over the text after mcp and over the comment's prose.

# OpenAPI

## `openapi`

- **Aliases:** openapi
- **Syntax:** `openapi [hide|hidden|ignore] | openapi tag <name> [, <name> ...]`
- **Owner:** openapi · **Form:** keyword line · **Where:** routine comment, SQL file
- **Value:** text
- **Example:** `openapi tag partners`

Control this endpoint in the OpenAPI document: hide it (also what a bare openapi does; the endpoint itself is unaffected), or replace its default tag, the schema name, with one or more tags. A tag is one word.

# SQL test files (`npgsqlrest --test`)

## `setup`

- **Aliases:** setup
- **Syntax:** `-- @setup <step> [, <step> ...]`
- **Owner:** test · **Form:** keyword line · **Where:** test file
- **Value:** list
- **Example:** `-- @setup seed_users`

Run these steps (named in TestRunner:Steps) before the file. In the file's header only: the comment lines at its top. An unknown step is an error and the file does not run.

## `teardown`

- **Aliases:** teardown
- **Syntax:** `-- @teardown <step> [, <step> ...]`
- **Owner:** test · **Form:** keyword line · **Where:** test file
- **Value:** list
- **Example:** `-- @teardown remove_users`

Run these steps (named in TestRunner:Steps) after the file, whether it passed or not. In the file's header only.

## `connection`

- **Aliases:** connection
- **Syntax:** `-- @connection <name>`
- **Owner:** test · **Form:** keyword line · **Where:** test file
- **Value:** name
- **Example:** `-- @connection reporting`

Run the file's SQL and its endpoint calls on this connection (a name in ConnectionStrings) instead of the run's test connection. In the file's header only.

## `tag`

- **Aliases:** tag
- **Syntax:** `-- @tag <name> [, <name> ...]`
- **Owner:** test · **Form:** keyword line · **Where:** test file
- **Value:** list
- **Example:** `-- @tag smoke, auth`

Tags for this file, which TestRunner:Tag and TestRunner:ExcludeTag select or leave out. In the file's header only.

## `include`

- **Aliases:** \i, \ir
- **Syntax:** `\i <path> | \ir <path>`
- **Owner:** test · **Form:** a line of its own shape · **Where:** test file
- **Value:** path
- **Example:** `\ir fixtures/users.sql`

Include another file here, as if its text were pasted: \i reads the path from the working directory, \ir from the including file's directory. On a line of its own, between statements. The rest of the line is the path.

## `test_request`

- **Syntax:** `/* GET|POST|PUT|DELETE /path[?query] ... */`
- **Owner:** test · **Form:** a line of its own shape · **Where:** test file
- **Value:** text
- **Example:** `/* GET /api/users?id=1 */`

A block comment that starts with a request line calls that endpoint in-process. Header lines and # @claim / # @response directives follow the request line, then a blank line, then the body. The response lands in a temporary table (_response, or _response_1, _response_2 ... when the file has several calls) with the columns status, body, content_type, headers and is_success, which the SQL after it asserts on.

## `claim`

- **Aliases:** claim
- **Syntax:** `# @claim <name>=<value>`
- **Owner:** test · **Form:** keyword line · **Where:** test file
- **Value:** text
- **Example:** `# @claim user_id=42`

Inside a request block: the call is made by a signed-in user with this claim. Repeat it for more claims. Without any, the call is anonymous.

## `response`

- **Aliases:** response
- **Syntax:** `# @response <table_name>`
- **Owner:** test · **Form:** keyword line · **Where:** test file
- **Value:** name
- **Example:** `# @response created`

Inside a request block: the name of the temporary table that receives this call's response.

# HTTP Custom Types (comments on composite types)

## `http_type_request`

- **Syntax:** `GET|POST|PUT|PATCH|DELETE <url>`
- **Owner:** http_type · **Form:** a line of its own shape · **Where:** type comment (HTTP Custom Type)
- **Value:** text
- **Example:** `GET https://api.example.com/users/{_id}`

The request line: the HTTP method and the URL to call, absolute, or starting with / for a call to an endpoint of this application. Only directives and # comment lines may come before it. Header lines follow it, then a blank line, then the request body. {name} placeholders in the URL, the headers and the body take the routine's parameter values. The response fills the type's fields by name: body, status_code, headers, content_type, success, error_message (the names are configurable in HttpClientOptions).

## `http_type_header`

- **Syntax:** `Header-Name: value`
- **Owner:** http_type · **Form:** a line of its own shape · **Where:** type comment (HTTP Custom Type)
- **Value:** text
- **Example:** `Authorization: Bearer {_token}`

A request header, on the lines after the request line. A Content-Type header sets the body's content type (application/json when there is a body and no header).

## `timeout`

- **Aliases:** timeout
- **Syntax:** `timeout <interval>`
- **Owner:** http_type · **Form:** keyword line or `key = value` · **Where:** type comment (HTTP Custom Type)
- **Value:** interval · **Default:** `30s`
- **Example:** `@timeout 10s`

How long one attempt may take. A number is seconds; 10s, 500ms, 2 minutes and 00:00:10 are also read. On a timeout status_code is 408 and error_message says so.

## `retry_delay`

- **Aliases:** retry_delay
- **Syntax:** `retry_delay <interval>, <interval>, ... [on <status>, <status>, ...]`
- **Owner:** http_type · **Form:** keyword line or `key = value` · **Where:** type comment (HTTP Custom Type)
- **Value:** list · **Default:** `no retries`
- **Example:** `@retry_delay 1s, 3s, 10s on 429, 503`

Retry a failed call: one retry per delay, each waited before its retry. Without 'on', any response outside 200-299 is retried; with it, only the listed status codes. Timeouts and network errors are always retried.

## `cache`

- **Aliases:** cache
- **Syntax:** `cache [interval]`
- **Owner:** http_type · **Form:** keyword line or `key = value` · **Where:** type comment (HTTP Custom Type)
- **Value:** interval · **Default:** `off`
- **Example:** `@cache 5 minutes`

Keep the response in memory and answer identical calls from it, for the given time (without a time, until the application stops). GET requests only, and only responses with status 200-299.
