# Annotations Guide

This guide describes all available annotations that can be used in PostgreSQL function or procedure comments to customize REST API behavior.

## AllowAnonymous

```console
allow_anonymous
anonymous
allow_anon
anon
```

Allow anonymous access with no authorization to this endpoint.

## Authorize

```console
authorize
authorized
requires_authorization
authorize [role1, role2, role3 [, ...]]
authorized [role1, role2, role3 [, ...]]
requires_authorization [role1, role2, role3 [, ...]]
```

Require authorization for this endpoint.

- If the user is not authorized and authorization is required, the endpoint will return the status code `401 Unauthorized`.
- If the user is authorized but not in any of the roles required by the authorization, the endpoint will return the status code `403 Forbidden`.


## BasicAuthentication

```console
basic_authentication [ [ username ] [ password ] ]
basic_auth [ [ username ] [ password ] ]
```

Enable Basic Authentication for this endpoint. Optionally, set the expected password or username and password. If no username or password is set, default will be used from configuration.

## BasicAuthenticationCommand

```console
basic_authentication_command [ command ]
basic_auth_command [ command ]
challenge_command [ command ]
```

Set Basic Authentication challenge command for this endpoint. Note: basic authentication must be enabled for this to take effect.

This command will always be executed regardless if endpoint has set password and username in configuration. This command will always perform validation if it is present. Same rules will apply when using login command and it can return valid claims if necessary.

It takes 4 optional parameters:

- `$1`: Unnamed, positional and optional parameter containing username from basic authentication header.
- `$2`: Unnamed, positional and optional parameter containing password from basic authentication header.
- `$3`: Unnamed, positional and optional parameter containing basic authentication realm.
- `$4`: Unnamed, positional and optional parameter containing endpoint path.

## BasicAuthenticationRealm

```console
basic_authentication_realm [ realm ]
basic_auth_realm [ realm ]
realm [ realm ]
```

Set Basic Authentication Realm for this endpoint. Note: basic authentication must be enabled for this to take effect.

## BodyParameterName

```console
body_parameter_name [name]
body_param_name [name]
```

Set the name of the existing parameter which is sent as body content. When the `body_parameter_name` is set, all other parameters are sent by the query string.

## BufferRows

```console
buffer_rows [number]
buffer [number]
```

Sets the buffered amount of rows before they are written to the response for this endpoint.

This value can also be set using custom parameters by setting number values to parameters with same name:
- `buffer_rows`
- `buffer`

## Cached

```console
cached
cached [param1, param2, param3 [, ...]]
cached [param1 param2 param3 [...]]
```

If the routine returns a single value of any type, result will be cached in memory and retrieved from memory on next call. Use the optional list of parameter names (original or converted) to be used as additional cache keys.

## CacheExpiresIn

```console
cache_expires [time_span_value]
cache_expires_in [time_span_value]
```

Sets the cache expiration time window if the routine was cached.

Value is a simplified PostgreSQL interval value, for example `10s` or `10sec` for 10 seconds, `5d` is for 5 days and so on. For example `3h`, `3hours`, `3 h` and `3 hours` are the same. Valid abbreviations are:

| abbr | meaning |
| ---- | ------------------------------- |
| `s`, `sec`, `second` or `seconds` | value is expressed in seconds |
| `m`, `min`, `minute` or `minutes` | value is expressed in minutes |
| `h`, `hour`, `hours` | value is expressed in hours |
| `d`, `day`, `days` | value is expressed in days |

## ColumnNames

```console
columns
names
column_names
```

If this option is set to true - and if the endpoint is in the "raw" mode - the endpoint will contain header names. If separators are applied, they will be used also.

This value can also be set using custom parameters by setting true or false value to parameters with these names:
- `columns`
- `names`
- `column_names`

## CommandTimeout

```console
command_timeout [seconds]
timeout [seconds]
```

Set the command execution timeout in seconds.

## ConnectionName

```console
connection [name]
connection_name [name]
```

Defines an alternative connection name. The name must be an existing key in a `ConnectionStrings` dictionary option. This is useful when some routines have to read from read-only replicas. However, the same routine also has to exist on the primary connection to be able to build the necessary metadata.

This value can also be set using custom parameters by setting text value to parameters with these names:
- `connection`
- `connection_name`

## ContentType

```console
content-type: [content_type_value]
content_type: [content_type_value]
```

Sets the content type header for the response. This uses the header format with a colon separator.

Example: `content-type: application/json`

## Disabled

```console
disabled
disabled [tag1, tag2, tag3 [, ...]]
```

The endpoint is disabled. Optional tag list disabled only for tags in the argument list.

## Enabled

```console
enabled
enabled [tag1, tag2, tag3 [, ...]]
```

The endpoint is enabled. Optional tag list enabled only for tags in the argument list.

## HTTP

```console
http
http [GET | POST | PUT | DELETE]
http [GET | POST | PUT | DELETE] path
http path
```

HTTP settings:
- Use HTTP annotation to enable when running in `CommentsMode.OnlyWithHttpTag` option.
- Change the HTTP method with the optional second argument.
- Change the HTTP path with the optional third argument.
- Change the HTTP path with second argument if the second argument doesn't match any valid VERB (GET, POST, etc).

## SseEventsPath

```console
sse
sse [path]
sse [path] on [info | notice | warning]
sse_path
sse_path [path]
sse_path [path] on [info | notice | warning]
sse_events_path
sse_events_path [path]
sse_events_path [path] on [info | notice | warning]
```

Additional path appended as a subpath to the main endpoint path for SSE (Server-Sent Events) streaming. If the endpoint path is `/path` and this value is set to `/info`, the streaming path will be `/path/info`.

**Usage:**
- Without arguments (just `sse` or `sse_path` or `sse_events_path`): Sets the path to default, which depends on the default notice level (`info` for INFO level, `notice` for NOTICE level, `warning` for WARNING level).
- Single argument: Treated as the path.
- Path followed by `on info` or `on notice` or `on warning`: Sets both the path and the notice level.

**Note:** You can also set SSE path using parameter annotation syntax (`key = value`), for example `sse = /my_sse_path` or `sse_path = /my_sse_path`.

## SseEventsLevel

```console
sse_level [info | notice | warning]
sse_events_level [info | notice | warning]
```

Sets the PostgreSQL notice level for SSE events. Determines which PostgreSQL notice messages will be streamed to the client.

**Note:** You can also set SSE level using parameter annotation syntax (`key = value`), for example `sse_level = info`.

## SseEventsScope

```console
sse_scope [self | matching | authorize | all] | [authorize [role1, role2, role3 [, ...]]]
sse_events_scope [self | matching | authorize | all] | [authorize [role1, role2, role3 [, ...]]]
```

Scope that determines to whom events are streamed:

- **`self`** (default): Only the original endpoint initiator session, regardless of the security context.
- **`matching`**: Sessions with matching security context of the endpoint initiator. If the endpoint initiator requires authorization, all authorized sessions will receive these messages. If the endpoint initiator requires authorization for certain roles, all sessions requiring the same roles will receive these messages.
- **`authorize`**: Only authorized sessions will receive these messages. If the `SseEventsRoles` property contains a list of roles, only sessions with those roles will receive messages.
- **`all`**: All sessions regardless of the security context will receive these messages.

When using `authorize`, add an optional list of authorized roles.

## Login

```console
login
signin
```

This annotation will transform the routine into the authentication endpoint that performs the sign-in operation. Endpoint will be used for the login endpoint. If NULL, the login endpoint will not be created.

Login endpoint expects a PostgreSQL command that will be executed to authenticate the user that follow this convention:
 
- Must return at least one record when authentication is successful. If no records are returned endpoint will return 401 Unauthorized.
- If record is returned, the authentication is successful, if not set in StatusColumnName column otherwise.
- All records will be added to user principal claim collection where column name is claim type and column value is claim value, except for three special columns defined in StatusColumnName, SchemeColumnName and MessageColumnName options:

- If "StatusColumnName" is present in the returned record, it must be either boolean (true for success, false for failure) or numeric (HTTP Status Code, 200 for success, anything else for failure). If not present, the success is when the endpoint returns any records.
- If "SchemeColumnName" is present in the returned record, it must be text value that defines the authentication scheme to use for the login.
- If "MessageColumnName" is present in the returned record, it must be text value that defines the message to return to the client. This only works for authentication that doesn't write response body (cookie authentication).

## Logout

```console
logout
signout
```

This annotation will transform the routine into the endpoint that performs the logout or the sign-out operation. Endpoint will be used for the logout endpoint. If NULL, the logout endpoint will not be created. 

Login endpoint expects a PostgreSQL command that performs the logout or the sign-out operation. If the routine doesn't return any data, the default authorization scheme is signed out. 

Any values returned will be interpreted as scheme names (converted to string) to sign out.

## RateLimiterPolicy

```console
rate_limiter_policy_name [name]
rate_limiter_policy [name]
rate_limiter [name]
```

Set the rate limiter policy name for this endpoint. The policy name must be registered in the application configuration.

## RetryStrategy

```console
retry_strategy_name [name]
retry_strategy [name]
retry [name]
```

Set the retry strategy name for this endpoint. The strategy name must be registered in the command retry options configuration.

## ErrorCodePolicy

```console
error_code_policy_name [name]
error_code_policy [name]
error_code [name]
```

Set the error code policy name for this endpoint. The policy name must be registered in the application configuration.

## NewLine

```console
new_line [newline_value]
raw_new_line [newline_value]
```

Defines a standard separator between raw value rows. It only applies when `raw` is on.

This value can also be set using custom parameters by setting text value to parameters with these names:
- `new_line`
- `raw_new_line`

## Parameter Annotations

The following annotations can be used to set special parameter behaviors:

### Parameter Hash

```console
param param_name1 is hash of param_name2
```

Hashes value of the `param_name1` with the value of `param_name2` parameter by using default hasher.

### Parameter Upload Metadata

```console
param param_name is upload metadata
upload param_name as metadata
```

Set the upload metadata parameter name.

### Parameter User ID

```console
param param_name is user_id
```

Marks a parameter to be populated with the current user's ID from authentication claims.

### Parameter User Name

```console
param param_name is user_name
```

Marks a parameter to be populated with the current user's name from authentication claims.

### Parameter User Roles

```console
param param_name is user_roles
```

Marks a parameter to be populated with the current user's roles from authentication claims.

### Parameter IP Address

```console
param param_name is ip_address
```

Marks a parameter to be populated with the current client's IP address.

### Parameter User Claims

```console
param param_name is user_claims
```

Marks a parameter to be populated with the current user's complete claims information from authentication.

## Path

```console
path [path]
```

Sets the custom HTTP path.

## QueryStringNullHandling

```console
query_string_null_handling [empty_string | empty | null_literal | null | ignore]
query_null_handling [empty_string | empty | null_literal | null | ignore]
query_string_null [empty_string | empty | null_literal | null | ignore]
query_null [empty_string | empty | null_literal | null | ignore]
```

Sets the response NULL handling option for the query string parameters:

- `empty_string` or `empty`: Empty query string is interpreted as the NULL value parameter.
- `null_literal` or `null`: The literal value `null` (case insensitive) in the query string is interpreted as the NULL value parameter.
- `ignore`: Doesn't handle NULL parameters in query strings. This is the default.

## Raw

```console
raw
raw_mode
raw_results
```

Sets response to a "raw" mode. HTTP response is written exactly as it is received from PostgreSQL (raw mode).

This is useful for creating CSV responses automatically. For example:

```sql
create function raw_csv_response1() 
returns setof text
language sql
as 
$$
select trim(both '()' FROM sub::text) || E'\n' from (
values 
    (123, '2024-01-01'::timestamp, true, 'some text'),
    (456, '2024-12-31'::timestamp, false, 'another text')
)
sub (n, d, b, t)
$$;
comment on function raw_csv_response1() is '
raw
content-type: text/csv
';
```

Produces the following response:

```
HTTP/1.1 200 OK                                                  
Connection: close
Content-Type: text/csv
Date: Tue, 08 Aug 2024 14:25:26 GMT
Server: Kestrel
Transfer-Encoding: chunked

123,"2024-01-01 00:00:00",t,"some text"
456,"2024-12-31 00:00:00",f,"another text"
```

This value can also be set using custom parameters by setting true or false value to parameters with these names:
- `raw`
- `raw_mode`
- `raw_results`

## RequestHeadersMode

```console
request_headers_mode [ignore | context | parameter]
request_headers [ignore | context | parameter]
```

Set how request parameters are handled:
- `ignore`: do not send request headers.
- `context`: Request headers are set as the session variable under `request.headers` key.
- `parameter`: Request headers are set as a default parameter defined with `request_headers_parameter_name`.

## RequestHeadersParameterName

```console
request_headers_parameter_name [name]
request_headers_param_name [name]
request-headers-param-name [name]
```

When `request_headers_mode` is set to send request headers as a parameter, set the existing parameter name. The default is `headers`.

## RequestParamType

```console
request_param_type [[query_string | query] | [body_json | body]]
param_type [[query_string | query] | [body_json | body]]
```

Set how request parameters are sent - query string or JSON body.

## Response Headers

```console
key: value
```

Any line containing `:` character is interpreted as the request header key and value where the key is the left side and the value is the right side string. For example: `content-type: text/html`

To be valid header key, key part must consist of alphanumerics or `_`, `-` characters.

## ResponseNullHandling

```console
response_null_handling [empty_string | empty | null_literal | null | no_content | 204 | 204_no_content]
response_null [empty_string | empty | null_literal | null | no_content | 204 | 204_no_content]
```

Sets the response NULL handling option for a single function results other than JSON (text, number, etc...):

- `empty_string` or `empty`: Empty content response. This is the default.
- `null_literal` or `null`: Content is the `null` string.
- `no_content`, `204`, or `204_no_content`: Response is `HTTP 204 No Content` status response code.

## SecuritySensitive

```console
sensitive
security
security_sensitive
```

Marks the endpoint as security sensitive which will obfuscate any parameters before sending it to log.

## Separator

```console
separator [separator_value]
raw_separator [separator_value]
```

Defines a standard separator between raw value columns. It only applies when `raw` is on.

This value can also be set using custom parameters by setting text value to parameters with these names:
- `separator`
- `raw_separator`

## Tags

```console
for tag1, tag2, tag3 [, ...]
tag tag1, tag2, tag3 [, ...]
tags tag1, tag2, tag3 [, ...]
```

All annotations in lines below this line apply only to tags in the argument list.

## Upload

```console
upload
upload for handler_name1, handler_name2 [, ...]
upload param_name as metadata
```

Marks routines as Upload endpoint.

Optionally, set handler name (or multiple handlers names) - or set the upload metadata parameter name.

## UserContext

```console
user_context
```

Enables user context for this endpoint. This allows access to user-specific settings and configuration.

## UserParameters

```console
user_parameters
user_params
```

Enables user parameters for this endpoint. This allows passing additional user-specific parameters to the routine.

# Custom Parameters

```console
name = value
```

Any line containing `=` character is interpreted as the parameter name and value where the name is the left side and the value is the right side string. 

For example: `path = ./my_path`

To be a valid custom parameter name, name part must consist of alphanumerics or `_`, `-` characters. Custom parameters are used to set different parameters for the generated endpoint. 

## General Parameters

- `buffer_rows = number`: Sets the buffered amount of rows before they are written to the response for this endpoint.
- `buffer = number`: Sets the buffered amount of rows before they are written to the response for this endpoint.
- `columns = true|false`: If set to true, and if the endpoint is in "raw" mode, the endpoint will contain header names.
- `names = true|false`: If set to true, and if the endpoint is in "raw" mode, the endpoint will contain header names.
- `column_names = true|false`: If set to true, and if the endpoint is in "raw" mode, the endpoint will contain header names.
- `connection = name`: Defines an alternative connection name that must exist in the ConnectionStrings dictionary.
- `connection_name = name`: Defines an alternative connection name that must exist in the ConnectionStrings dictionary.
- `new_line = character(s)`: Defines the separator between raw value rows when raw mode is enabled.
- `raw_new_line = character(s)`: Defines the separator between raw value rows when raw mode is enabled.
- `raw = true|false`: Sets response to "raw" mode where HTTP response is written exactly as received from PostgreSQL.
- `raw_mode = true|false`: Sets response to "raw" mode where HTTP response is written exactly as received from PostgreSQL.
- `raw_results = true|false`: Sets response to "raw" mode where HTTP response is written exactly as received from PostgreSQL.
- `separator = character(s)`: Defines the separator between raw value columns when raw mode is enabled.
- `raw_separator = character(s)`: Defines the separator between raw value columns when raw mode is enabled.

## Upload Handlers Parameters

### Shared Parameters

- `stop_after_first_success = true|false`: When true, stops processing after the first successful upload handler.
- `included_mime_types = pattern or csv patterns`: Comma-separated list of MIME type patterns to include for upload processing.
- `excluded_mime_types = pattern or csv patterns`: Comma-separated list of MIME type patterns to exclude from upload processing.

### LargeObject Upload Handler Parameters

- `buffer_size = number`: Size of the buffer used for reading/writing large object data.
- `check_text = true|false`: When true, checks if the uploaded content is text format.
- `check_image = true|false`: When true, checks if the uploaded content is an image format.
- `test_buffer_size = number`: Size of the buffer used for testing file content type.
- `non_printable_threshold = number`: Threshold for determining if content contains non-printable characters.
- `oid = number`: PostgreSQL large object OID to use for storage.
- `large_object_included_mime_types = pattern or csv patterns`: MIME type patterns to include for large object upload processing.
- `large_object_excluded_mime_types = pattern or csv patterns`: MIME type patterns to exclude from large object upload processing.
- `large_object_buffer_size = number`: Buffer size specifically for large object operations.
- `large_object_oid = number`: Specific PostgreSQL large object OID for this handler.
- `large_object_check_text = true|false`: Enable text content checking for large object uploads.
- `large_object_check_image = true|false`: Enable image content checking for large object uploads.
- `large_object_test_buffer_size = number`: Test buffer size for large object content type detection.
- `large_object_non_printable_threshold = number`: Non-printable character threshold for large object content.

### FileSystem Upload Handler Parameters

- `buffer_size = number`: Size of the buffer used for reading/writing file system data.
- `check_text = true|false`: When true, checks if the uploaded content is text format.
- `check_image = true|false`: When true, checks if the uploaded content is an image format.
- `test_buffer_size = number`: Size of the buffer used for testing file content type.
- `non_printable_threshold = number`: Threshold for determining if content contains non-printable characters.
- `path = name`: File system path where uploaded files will be stored.
- `file = name`: Specific file name to use for the uploaded content.
- `unique_name = true|false`: When true, generates unique file names to avoid conflicts.
- `create_path = true|false`: When true, creates the directory path if it doesn't exist.
- `file_system_included_mime_types = pattern or csv patterns`: MIME type patterns to include for file system upload processing.
- `file_system_excluded_mime_types = pattern or csv patterns`: MIME type patterns to exclude from file system upload processing.
- `file_system_buffer_size = number`: Buffer size specifically for file system operations.
- `file_system_path = name`: Specific file system path for this handler.
- `file_system_file = name`: Specific file name for this handler.
- `file_system_unique_name = true|false`: Enable unique file naming for this handler.
- `file_system_create_path = true|false`: Enable automatic path creation for this handler.
- `file_system_check_text = true|false`: Enable text content checking for file system uploads.
- `file_system_check_image = true|false`: Enable image content checking for file system uploads.
- `file_system_test_buffer_size = number`: Test buffer size for file system content type detection.
- `file_system_non_printable_threshold = number`: Non-printable character threshold for file system content.

### CSV Upload Handler Parameters

- `test_buffer_size = number`: Size of the buffer used for testing CSV content type.
- `non_printable_threshold = number`: Threshold for determining if content contains non-printable characters.
- `check_format = true|false`: When true, validates the CSV format before processing.
- `delimiters = character(s)`: Characters used as field delimiters in CSV files (e.g., comma, semicolon).
- `has_fields_enclosed_in_quotes = true|false`: When true, expects CSV fields to be enclosed in quotes.
- `set_white_space_to_null = true|false`: When true, converts whitespace-only fields to NULL values.
- `row_command = command`: SQL command to execute for each CSV row during processing.
- `csv_included_mime_types = pattern or csv patterns`: MIME type patterns to include for CSV upload processing.
- `csv_excluded_mime_types = pattern or csv patterns`: MIME type patterns to exclude from CSV upload processing.
- `csv_check_format = true|false`: Enable CSV format validation for this handler.
- `csv_test_buffer_size = number`: Test buffer size for CSV content type detection.
- `csv_non_printable_threshold = number`: Non-printable character threshold for CSV content.
- `csv_delimiters = character(s)`: Field delimiters specifically for this CSV handler.
- `csv_has_fields_enclosed_in_quotes = true|false`: Quote handling for this CSV handler.
- `csv_set_white_space_to_null = true|false`: Whitespace to NULL conversion for this CSV handler.
- `csv_row_command = command`: Row processing command for this CSV handler.

### Excel Upload Handler Parameters

- `sheet_name = name`: Name of the specific Excel worksheet to process.
- `all_sheets = true|false`: When true, processes all worksheets in the Excel file.
- `time_format = format string`: Format string for parsing time values from Excel cells.
- `date_format = format string`: Format string for parsing date values from Excel cells.
- `datetime_format = format string`: Format string for parsing datetime values from Excel cells.
- `row_is_json = true|false`: When true, treats each Excel row as JSON data.
- `row_command = command`: SQL command to execute for each Excel row during processing.
- `excel_included_mime_types = pattern or csv patterns`: MIME type patterns to include for Excel upload processing.
- `excel_excluded_mime_types = pattern or csv patterns`: MIME type patterns to exclude from Excel upload processing.
- `excel_sheet_name = name`: Specific worksheet name for this Excel handler.
- `excel_all_sheets = true|false`: Enable processing of all worksheets for this handler.
- `excel_time_format = format string`: Time format specifically for this Excel handler.
- `excel_date_format = format string`: Date format specifically for this Excel handler.
- `excel_datetime_format = format string`: DateTime format specifically for this Excel handler.
- `excel_row_is_json = true|false`: JSON row processing for this Excel handler.
- `excel_row_command = command`: Row processing command for this Excel handler.

## SseEventsPath Parameters

Additional path appended as a subpath to the main endpoint path for SSE events streaming (null disables SSE events). If the endpoint path is `/path` and this value is set to `/info`, the streaming path will be `/path/info`.

**Note:** This can also be boolean. When set to `true`, the SSE streaming path will be the notice level name which will be added to the main path.

- `sse = [path | true | false]`
- `sse_path = [path | true | false]`
- `sse_events_path = [path | true | false]`

## SseEventsLevel Parameters

Sets the PostgreSQL notice level for SSE events. Determines which PostgreSQL notice messages will be streamed to the client.

- `sse_level = [info | notice | warning]`
- `sse_events_level = [info | notice | warning]`

## SseEventsScope Parameters

Scope that determines to whom events are streamed:

- **`self`** (default): Only the original endpoint initiator session, regardless of the security context.
- **`matching`**: Sessions with matching security context of the endpoint initiator. If the endpoint initiator requires authorization, all authorized sessions will receive these messages. If the endpoint initiator requires authorization for certain roles, all sessions requiring the same roles will receive these messages.
- **`authorize`**: Only authorized sessions will receive these messages. If the `SseEventsRoles` property contains a list of roles, only sessions with those roles will receive messages.
- **`all`**: All sessions regardless of the security context will receive these messages.

When using `authorize`, add an optional list of authorized roles.

- `sse_scope = [self | matching | authorize | all] | [authorize [role1, role2, role3 [, ...]]]`
- `sse_events_scope = [self | matching | authorize | all] | [authorize [role1, role2, role3 [, ...]]]`

## TsClient Parameters

- `tsclient = [false | off | disabled | disable | 0]` - disable tsclient code generation for the endpoint.
- `tsclient_events = [[false | off | disabled | disable | 0] | [true | on | enabled | enable | 1]]` - enable or disable SSE events parameter for endpoints with SSE events enabled.
- `tsclient_parse_url = [[false | off | disabled | disable | 0] | [true | on | enabled | enable | 1]]` - enable or disable SSE events parameter URL parsing.
- `tsclient_parse_request = [[false | off | disabled | disable | 0] | [true | on | enabled | enable | 1]]` - enable or disable SSE events parameter request parsing.
- `tsclient_status_code = [[false | off | disabled | disable | 0] | [true | on | enabled | enable | 1]]` - enable or disable status code in the return value.

# Tags

Tags are applied by different routine sources that can generate a valid endpoint. They are used with following comment annotations:

- Tag: `for tag1, tag2, tag3 [, ...]`
- Enabled: `enabled [tag1, tag2, tag3 [, ...]]`
- Disabled: `disabled [tag1, tag2, tag3 [, ...]]`

## Function or Procedure Source

This source generates tags based on routine volatility:

- `volatile`
- `stable`
- `immutable`
- `other`

## Table or View CRUD Source

### Select Routines

- `select`
- `read`
- `get`

### Update Returning Routines

- `update`
- `post`
- `update_returning`
- `returning`

### Delete Routines

- `delete`

### Delete Returning Routines

- `delete`
- `delete_returning`
- `returning`

### Insert Routines

- `insert`
- `put`
- `create`

### Insert On Conflict Do Nothing Routines

- `insert`
- `put`
- `create`
- `insert_on_conflict_do_nothing`
- `on_conflict_do_nothing`
- `on_conflict`

### Insert On Conflict Do Nothing Returning Routines

- `insert`
- `put`
- `create`
- `insert_on_conflict_do_nothing_returning`
- `on_conflict_do_nothing`
- `on_conflict`
- `returning`

### Insert On Conflict Do Update Routines

- `insert`
- `put`
- `create`
- `insert_on_conflict_do_update`
- `on_conflict`
- `on_conflict_do_update`

### Insert On Conflict Do Update Returning Routines

- `insert`
- `put`
- `create`
- `insert_on_conflict_do_update_returning`
- `on_conflict_do_update`
- `on_conflict`
- `returning`