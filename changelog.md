# Changelog

Note: The changelog for the older version can be found here: [Changelog Archive](https://github.com/NpgsqlRest/NpgsqlRest/blob/master/changelog-old.md)

---

TODOs:

- TODO rename info_* annotations to streaming_* or SSE_* or events_*
/*
private static readonly string[] InfoEventsStreamingPathKey = [
    "info_path",
    "info_events_path",
    "info_streaming_path"
];

private static readonly string[] InfoEventsStreamingScopeKey = [
    "info_scope",
    "info_events_scope",
    "info_streaming_scope",
];
*/

- TODO fix TsClient, when returning setof or table and one of the columns is json, it should be "any" or "unknown" not "string"

- TODO fix claims to parameters or user context mapping - if empty string is passed, use null (make it optional with default true)

- TODO fix array parameter logging: e.g. -- $6 text[] = 'System.Collections.Generic.List`1[System.String]'

- TODO request timeout policies: https://learn.microsoft.com/en-us/aspnet/core/performance/timeouts?view=aspnetcore-9.0

- TODO OpenAPI JSON automatic generation (just for the client)
/*
{
"openapi": "3.0.1",
"info": {
"title": "My API",
"version": "1.0.0"
},
"paths": {
"/api/users": {
"get": {
"operationId": "get_all_users",
"summary": "Get all users",
"responses": {
"200": {
"description": "Success"
}
}
}
}
}
}
*/

DOING:

Generated query that selects type like this:
select (response::mathmodule.math_expressions_response).math_id,(response::mathmodule.math_expressions_response).expr,(response::mathmodule.math_expressions_response).description,(response::mathmodule.math_expressions_response).created_by_name,(response::mathmodule.math_expressions_response).modified_by_name,(response::mathmodule.math_expressions_response).created_by_id,(response::mathmodule.math_expressions_response).modified_by_id,(response::mathmodule.math_expressions_response).created_at,(response::mathmodule.math_expressions_response).updated_at,count from mathmodule.search_math_expressions(_search=>$1,_only_my=>$2,_limit=>$3,_offset=>$4,_user_id=>$5)

Doesn't actually need to include the typecast everywhere:
select (response).math_id,(response).expr,(response).description,(response).created_by_name,(response).modified_by_name,(response).created_by_id,(response).modified_by_id,(response).created_at,(response).updated_at,count from mathmodule.search_math_expressions(_search=>$1,_only_my=>$2,_limit=>$3,_offset=>$4,_user_id=>$5)


## Version [3.0.0](https://github.com/NpgsqlRest/NpgsqlRest/tree/3.0.0) (date is TBD)

[Full Changelog](https://github.com/NpgsqlRest/NpgsqlRest/compare/2.36.0...2.36.1)

### Error Handling Improvements

Added comprehensive error handling improvements with standardized error responses using Problem Details (RFC 7807) format.

```json
{
  "title": "Error message or custom title",
  "status": 400,
  "detail": "P0001"
}
```

Old error handling options have been removed in favor of a more flexible and extensible error code policy system.

- Removed obsolete configuration options from client configuration:

```json
{
  "NpgsqlRest": {
    //
    // Set to true to return message from NpgsqlException on response body. Default is true.
    //
    "ReturnNpgsqlExceptionMessage": true,
    //
    // Map PostgreSql Error Codes (see https://www.postgresql.org/docs/current/errcodes-appendix.html) to HTTP Status Codes. Default is 57014 query_canceled to 205 Reset Content.
    //
    "PostgreSqlErrorCodeToHttpStatusCodeMapping": {
      "57014": 205,
      "P0001": 400,
      // PL/pgSQL raise exception
      "P0004": 400
      // PL/pgSQL assert failure
    }
  }
}
```

- Removed options:

```csharp
    /// <summary>
    /// Set to true to return message from NpgsqlException on response body. Default is true.
    /// </summary>
    public bool ReturnNpgsqlExceptionMessage { get; set; } = true;

    /// <summary>
    /// Map PostgreSql Error Codes (see https://www.postgresql.org/docs/current/errcodes-appendix.html) to HTTP Status Codes
    /// Default is 57014 query_canceled to 205 Reset Content.
    /// </summary>
    public Dictionary<string, int> PostgreSqlErrorCodeToHttpStatusCodeMapping { get; set; } = new()
    {
        { "57014", 205 }, //query_canceled -> 205 Reset Content
        { "P0001", 400 }, // raise_exception -> 400 Bad Request
        { "P0004", 400 }, // assert_failure -> 400 Bad Request
    };
```

- Added new configuration section in client configuration:

```json
{
  "ErrorHandling": {
    // Remove Type URL from error responses. Middleware automatically sets a default Type URL based on the HTTP status code that points to the RFC documentation.
    "RemoveTypeUrl": false,
    // Remove TraceId field from error responses. Useful in development and debugging scenarios to correlate logs with error responses.
    "RemoveTraceId": true,
    //
    // Named policies for mapping of PostgreSQL error codes to HTTP Status Codes.
    //
    // If routine raises these PostgreSQL error codes, endpoint will return these HTTP Status Codes.
    // See https://www.postgresql.org/docs/current/errcodes-appendix.html
    //
    // - StatusCode: HTTP status code to return.
    // - Title: Optional title field in response JSON. When null, actual error message is used.
    // - Details: Optional details field in response JSON. When null, PostgreSQL Error Code is used.
    // - Type: Optional types field in response JSON. A URI reference [RFC3986] that identifies the problem type. Set to null to use default. Or RemoveTypeUrl to true to disable.
    //
    "ErrorCodePolicies": [{
      "Name": "Default",
      "ErrorCodes": {
        "42501": {"StatusCode": 403, "Title": "Insufficient Privilege", "Details": null, "Type": null}, // query_canceled      -> 403 Forbidden
        "57014": {"StatusCode": 205, "Title": "Cancelled", "Details": null, "Type": null},              // query_canceled      -> 205 Reset Content
        "P0001": {"StatusCode": 400, "Title": null, "Details": null, "Type": null},                     // raise_exception     -> 400 Bad Request
        "P0004": {"StatusCode": 400, "Title": null, "Details": null, "Type": null},                     // assert_failure      -> 400 Bad Request
        "42883": {"StatusCode": 404, "Title": "Not Found", "Details": null, "Type": null}               // undefined_function  -> 404 Not Found
      }
    }],
    //
    // Default policy name to use from the ErrorCodePolicies section.
    //
    "DefaultErrorCodePolicy": "Default"
  }
}
```

- Added new options:

```csharp
    /// <summary>
    /// Map PostgreSql Error Codes (see https://www.postgresql.org/docs/current/errcodes-appendix.html) to HTTP Status Codes
    /// </summary>
    public ErrorHandlingOptions ErrorHandlingOptions { get; set; } = new();
```
```csharp
    public class ErrorHandlingOptions
    {
        public string? DefaultErrorCodePolicy { get; set; } = "Default";

        public Dictionary<string, Dictionary<string, ErrorCodeMappingOptions>> ErrorCodePolicies { get; set; } = new()
        {
            ["Default"] = new()
            {
                { "42501", new() { StatusCode = 403, Title = "Insufficient Privilege" } },
                { "57014", new() { StatusCode = 205, Title = "Cancelled" } },
                { "P0001", new() { StatusCode = 400 } },
                { "P0004", new() { StatusCode = 400 } },
                { "42883", new() { StatusCode = 404, Title = "Not Found" } },
            }
        };
    }
```

- Added new comment annotations to set error code policy per endpoint:

```
error_code_policy_name [ name ]
error_code_policy [ name ]
error_code [ name ]
```

For example:

```sql
comment on function my_function(json) is 'error_code_policy custom_policy_name';
-- or
comment on function my_function(json) is 'error_code_policy_name custom_policy_name';
-- or
comment on function my_function(json) is 'error_code custom_policy_name';
```

### Metadata Query Improvements

There two new options for Metadata queries support, that are also available in client configuration:

- `MetadataQueryConnectionName`: Specify a named connection from `ConnectionStrings` dictionary to use for metadata queries. When null, the default connection string or data source is used.
- `MetadataQuerySchema`: Set the PostgreSQL search path schema for metadata query functions. Useful when using non-superuser connection roles with limited schema access.

Options:

```csharp
/// <summary>
/// The connection name in ConnectionStrings dictionary that will be used to execute the metadata query. If this value is null, the default connection string or data source will be used.
/// </summary>
public string? MetadataQueryConnectionName { get; set; } = null;

/// <summary>
/// Set the search path to this schema that contains the metadata query function. Default is `public`.
/// </summary>
public string? MetadataQuerySchema { get; set; } = "public";
```

```json
{
  //
  // Additional connection settings and options.
  //
  "ConnectionSettings": {
    //
    // other ConnectionSettings settings
    //
    
    //
    // The connection name in ConnectionStrings configuration that will be used to execute the metadata query. If this value is null, the default connection string will be used.
    //
    "MetadataQueryConnectionName": null,
    //
    // Set the search path to this schema that contains the metadata query function. Default is `public`. Default is `public`. Set to null to avoid setting metadata query search path.
    //
    // This is needed when using non superuser connection roles with limited schema access and mapping the metadata function to a specific schema. 
    // If the connection string contains the same "Search Path=" it will be skipped.
    //
    "MetadataQuerySchema": "public"
  }
}
```

### Rate Limiter

Added comprehensive rate limiting support with integration into ASP.NET Core's built-in rate limiting middleware:

You can: 

- Configure rate limiting policies middleware manually (for library users). 
- Set rate limiter client configuration policies (for client app users). 

And then:

- Set default rate limiter policy for all generated endpoints.
- Set specific endpoint rate limiter policy.
- Use comment annotation to set endpoint rate limiter policy.

Client configuration:

```json
{
  //
  // Rate Limiter settings to limit the number of requests from clients.
  //
  "RateLimiterOptions": {
    "Enabled": false,
    "StatusCode": 429,
    "StatusMessage": "Too many requests. Please try again later.",
    "DefaultPolicy": null,
    // Policy types: FixedWindow, SlidingWindow, BucketWindow, Concurrency
    "Policies": [{
      // see https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit#fixed
      "Type": "FixedWindow",
      "Enabled": false,
      "Name": "fixed",
      "PermitLimit": 100,
      "WindowSeconds": 60,
      "QueueLimit": 10,
      "AutoReplenishment": true
    }, {
      // see https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit#sliding-window-limiter
      "Type": "SlidingWindow",
      "Enabled": false,
      "Name": "sliding",
      "PermitLimit": 100,
      "WindowSeconds": 60,
      "SegmentsPerWindow": 6,
      "QueueLimit": 10,
      "AutoReplenishment": true
    }, {
      // see https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit#token-bucket-limiter
      "Type": "TokenBucket",
      "Enabled": true,
      "Name": "bucket",
      "TokenLimit": 100,
      "ReplenishmentPeriodSeconds": 10,
      "QueueLimit": 10,
      "AutoReplenishment": true
    }, {
      // see https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit#concurrency-limiter
      "Type": "Concurrency",
      "Enabled": true,
      "Name": "concurrency",
      "PermitLimit": 10,
      "QueueLimit": 5,
      "OldestFirst": true
    }]
  }
}
```

- Option to set default policy for all endpoints:

```csharp
/// <summary>
/// Default rate limiting policy for all requests. Policy must be configured within application rate limiting options.
/// This can be overridden by comment annotations in the database or setting policy for specific endpoints.
/// </summary>
public string? DefaultRateLimitingPolicy { get; set; } = null;
```

- Endpoint property:

```csharp
public string? RateLimiterPolicy { get; set; } = null;
```

- Comment annotations:

```
rate_limiter_policy_name [ name ]
rate_limiter_policy [ name ]
rate_limiter [ name ]
```

### Other Changes and Fixes

- Major refactoring: generated endpoints moved from custom middleware to minimal APIs. This allows better integration with ASP.NET Core ecosystem (rate limiter, request timeout, etc).
- Breaking change caused by the above: API invocation to existing paths with wrong HTTP method will return 405 Method Not Allowed instead of 404 Not Found.
- More testing (concurrency).
- Fix: fixed excessive logging when retrying failed commands.
- Fix: missing command logging on void routines.
- Refactoring: static Options instead of passing Options parameter around.
- Refactoring: static Logger instead of passing Logger parameter around.
- NpgsqlRest core project library has set InternalsVisibleTo to NpgsqlRestTests for testability for tests using Options or Logger. 
- Refactoring: moved some files around to better structure the project.

## Version [2.36.1](https://github.com/NpgsqlRest/NpgsqlRest/tree/2.36.1) (2025-09-29)

[Full Changelog](https://github.com/NpgsqlRest/NpgsqlRest/compare/2.36.0...2.36.1)

This minor version improves `TSClient` plugin only (`ClientCodeGen` configuration in client).

Generated Typescript fetch modules are simplified: 
- Returned type is now `Promise<{status: number, response: IMyResponse[]}>` instead of previously `Promise<{status: number, response: IMyResponse[] | string}>`.
- When failed, response cast string to any: `response: response.status == 200 ? await response.json() as IMyResponse[] : await response.text() as any`.

This approach avoids casting to appropriate type on caller:

```ts
myCall({}).then(res => {
    if (res.status === 200) {
      // previously: 
      //let myField = (res.response[0] as IMyResponse).field;

      // now:
      let myField = res.response[0].field;
    }
});
```

If status is different then 200, then we need to cast to string.

Additionaly, new configuration option for `ClientCodeGen` configuration:

```jsonc
//
// When true, include PostgreSQL schema name in the generated type names to avoid name collisions. Set to false to simplify type names when no name collisions are expected.
// 
"IncludeSchemaInNames": true
```

## Version [2.36.0](https://github.com/NpgsqlRest/NpgsqlRest/tree/2.36.0) (2025-09-29)

[Full Changelog](https://github.com/NpgsqlRest/NpgsqlRest/compare/2.35.0...2.36.0)

- Added `AvailableClaims` to `StaticFiles.ParseContentOptions` client configuration section. 

This is fix. Previously, if the user was not authenticated, or claim was not found in the claims collection, the tag would not be replaced. Now it will be replaced with NULL. Without this, resulting template may be malformed, for example  if it was in JavaScript.

- Removed unused fields in [NpgsqlRest.csproj](NpgsqlRest/NpgsqlRest.csproj)

## Version [2.35.0](https://github.com/NpgsqlRest/NpgsqlRest/tree/2.35.0) (2025-09-18)

[Full Changelog](https://github.com/NpgsqlRest/NpgsqlRest/compare/2.34.0...2.35.0)

1. Fix logging config issues.
2. Fix startup message issue. 
3. Fix custom header lines configuration issue.

Added OpenTelemetry support. See Log configuration for more details.

## Version [2.34.0](https://github.com/NpgsqlRest/NpgsqlRest/tree/2.34.0) (2025-09-12)

[Full Changelog](https://github.com/NpgsqlRest/NpgsqlRest/compare/2.33.0...2.34.0)

- Improved connection retry mechanism (see ConnectionSettings.RetryOptions).
- Added command retry on all levels (see CommandRetryOptions).
- Fixed StartupMessage rendering and added named formats.
- Added cache settings (see CacheOptions) and Redis cache as second option (beside memory cache).

## Version [2.33.0](https://github.com/NpgsqlRest/NpgsqlRest/tree/2.33.0) (2025-08-29)

[Full Changelog](https://github.com/NpgsqlRest/NpgsqlRest/compare/client-2.27.1...2.33.0)

- Client and core library versions unified into a single versioning scheme for less confusion.

### Configuration System Improvements

- Added environment variable parsing for ALL configuration values (instead of just connection strings):

It is possible to add configuration value like this: `"Value"="{ENV_VAR_NAME}"` and it will be replaced with the value of the environment variable `ENV_VAR_NAME` on startup. Works for all configuration value types: string, int, bool, enum, arrays, dictionaries, etc.

If you need to set numerical value, use quotes: `"Value"="{ENV_VAR_NAME}"` and value will be parsed to numerical value. Boolean values are parsed case-insensitively from `true`, `false`, `1`, `0`, `yes`, `no`, `on`, `off`. Type conversion will raise error and exit on type mismatch.

This behavior is controlled by a new option `"Config.ParseEnvironmentVariables"` (default is true).

Classic environment variables binding is still supported: Environment variable key must match configuration key with `:` or `__` (double underscore) as separator For example, `NpgsqlRest:ConnectionStrings:Default` or `NpgsqlRest__ConnectionStrings__Default`.

This behavior is controlled by option `"Config.AddEnvironmentVariables"` (default is false).

This change allowed removing following options from the `ConnectionSettings` that became obsolete: `ParseConnectionStringWithEnvVars`, `UseEnvVars`, `MatchNpgsqlConnectionParameterNamesWithEnvVarNames`, `EnvVarsOverride`, `HostEnvVar`, `PortEnvVar`, `DatabaseEnvVar`, `UserEnvVar`, and `PasswordEnvVar`

### Command Line Switches

- Added command line switch `--config` to dump current configuration to console. 
- All switches now start with double dash `--` prefix.
- Full list of command line switches:

```
npgsqlrest [--key=value]                                Override the configuration with this key with a new value (case insensitive, use : to separate sections). 
                                                         
npgsqlrest -v, --version                                Show version information.
npgsqlrest -h, --help                                   Show command line help.
npgsqlrest --config                                     Dump current configuration to console and exit.
npgsqlrest --hash [value]                               Hash value with default hasher and print to console.
npgsqlrest --basic_auth [username] [password]           Print out basic basic auth header value in format 'Authorization: Basic base64(username:password)'.
npgsqlrest --encrypt [value]                            Encrypt string using default data protection and print to console.
npgsqlrest --encrypted_basic_auth [username] [password] Print out basic basic auth header value in format 'Authorization: Basic base64(username:password)' where password is encrypted with default data protection.
```

### Basic Auth Improvements

Naive implementation of Basic Auth was improved to support multiple users with different roles. See `NpgsqlRest:AuthenticationOptions:BasicAuth:Users` configuration section for more details.

## Version [client-2.27.1](https://github.com/NpgsqlRest/NpgsqlRest/tree/client-2.27.1) (2025-08-25)

[Full Changelog](https://github.com/NpgsqlRest/NpgsqlRest/compare/2.32.0...client-2.27.1)

- Fix default port issue in client app.
- Fix issue with multiple connection configuration.

## Version [2.32.0](https://github.com/NpgsqlRest/NpgsqlRest/tree/2.32.0) (2025-08-23)

[Full Changelog](https://github.com/NpgsqlRest/NpgsqlRest/compare/2.31.0...2.32.0)

- Added support for Basic Authentication. See `NpgsqlRest:AuthenticationOptions:BasicAuth` for more details.
- Simplified data protection configuration. See `NpgsqlRest:DataProtection` for more details.
- Simplified external authentication code.
- Improved logging for authentication and data protection (verbose level).
- Added new command line commands:

```
npgsqlrest hash [value]                               Hash value with default hasher and print to console.
npgsqlrest basic_auth [username] [password]           Print out basic basic auth header value in format 'Authorization: Basic base64(username:password)'.
npgsqlrest encrypt [value]                            Encrypt string using default data protection and print to console.
npgsqlrest encrypted_basic_auth [username] [password] Print out basic basic auth header value in format 'Authorization: Basic base64(username:password)' where password is encrypted with default data protection.
```

## Version [2.31.0](https://github.com/NpgsqlRest/NpgsqlRest/tree/2.31.0) (2025-08-18)

[Full Changelog](https://github.com/NpgsqlRest/NpgsqlRest/compare/2.30.0...2.31.0)

- Simplified Claims mappings. See `ParameterNameClaimsMapping` and `ContextKeyClaimsMapping`. Exact claim names used.
- Support for Connection Open Retry logic with identical behavior to EF Core. See `ConnectionSettings:RetryOptions` for more details.
- Support for conenction string format parser using enviorment varaibles. See `ConnectionSettings:ParseConnectionStringWithEnvVars` for more details.
- Support for Apple Silicon ARM64 builds.
- Added Log MinimalLevels configuration for NpgsqlRest. See `Log:MinimalLevels:NpgsqlRest`.
- Changed all NpgsqlRest informational logging to debug level.
- Added trace logging for routine provider queries. This is very useful for debugging. Verbose level needs to be set.
- Removed all links from comments, and recrated new readme pages.
- Moved project from vb-consulting to NpgsqlRest.
- Fixed typos in couple of options: `Languagues` -> `Languages`.

## Version [2.30.0](https://github.com/NpgsqlRest/NpgsqlRest/tree/2.30.0) (2025-08-09)

[Full Changelog](https://github.com/NpgsqlRest/NpgsqlRest/compare/2.29.0...2.30.0)

### Core Library

#### Breaking Changes

- **Removed Active Directory Federation Services Claim Types support**: Eliminated `UseActiveDirectoryFederationServicesClaimTypes` option and the entire `ClaimsDictionary` class that mapped claim names to AD FS URIs. Authentication now uses simple claim type names directly instead of attempting to resolve them to Microsoft AD FS claim type URIs.

- **Removed response parsing annotations**: Eliminated `parse` and `parse_response` comment annotations and the corresponding `ParseResponse` property from routine endpoints. This feature was used to parse single-column, single-row responses.

- **Simplified static file parsing**: Removed specific tag mappings (`UserIdTag`, `UserNameTag`, `UserRolesTag`, `CustomTagToClaimMappings`) from ParseContentOptions. Static file parsing now uses direct claim type names in `{claimType}` format instead of configurable tag names.

#### Bug Fixes

- **Authentication claim handling**: Fixed issues with claim type resolution by removing complex Active Directory Federation Services mapping and using direct claim types
- **Fix AuthorizePaths typo**: Corrected typo from `"AutorizePaths"` to `"AuthorizePaths"` in appsettings.json configuration file and updated corresponding references in App.cs and AppStaticFileMiddleware.cs
- **Fix Data Protection logging**: Fixed spacing issue in Data Protection logging message template for better log formatting

#### Improvements

- **Simplified authentication configuration**: 
  - Removed complex claim type mapping logic and `UseActiveDirectoryFederationServicesClaimTypes` option
  - Updated default claim type values to use simple names:
    - `DefaultUserIdClaimType`: changed from `"nameidentifier"` to `"id"`
    - `DefaultNameClaimType`: remains `"name"` (unchanged)  
    - `DefaultRoleClaimType`: changed from `"role"` to `"roles"`
  - Column names from login endpoint responses are now converted directly to claim types without any transformation or mapping

- **Streamlined static file content parsing**: Static files now use direct claim type substitution with `{claimType}` syntax, making the configuration much simpler and more straightforward

- **Enhanced info events broadcasting**: Improved Server-Sent Events (SSE) broadcasting functionality for info events:
  - Improved performance in `Broadcaster.cs` by removing unnecessary `.ToArray()`
  - Enhanced authorization scope checking in `NpgsqlRestNoticeEventSource.cs` to support user ID, name, and role claims for broadcast authorization
  - Broadcast authorization now checks against `DefaultUserIdClaimType`, `DefaultNameClaimType`, and not just only `DefaultRoleClaimType` claim types.
  - Authorization list now merges info hints and endpoint `InfoEventsRoles` settings.

### NpgsqlRest Client

#### Improvements

- **Enhanced connection logging**: Improved connection string logging in Builder.cs to handle cases where connection name is empty. Now properly logs "Using main connection string: {connection}" when no connection name is provided, instead of displaying an empty parameter
- **Expanded startup message**: Extended startup message to support additional placeholders:
  - `{3}` - Environment name (Development, Production, etc.)
  - `{4}` - Application name
  
  Updated Program.cs to pass these additional parameters, allowing for more detailed startup information like:
  ```
  "Started in {0}, listening on {1}, version {2}, environment {3}, application {4}"
  ```

---

## Version [2.29.0](https://github.com/NpgsqlRest/NpgsqlRest/tree/2.29.0) (2025-07-08)

[Full Changelog](https://github.com/NpgsqlRest/NpgsqlRest/compare/2.28.0...2.29.0)

### Core Library

#### Info Events Streaming Feature

This feature enables sending info events raised with [raise info statements](https://www.postgresql.org/docs/current/plpgsql-errors-and-messages.html) as SSE (Server-Side Events). When enabled, endpoints can stream real-time notifications to connected clients.

##### How It Works

When this feature is enabled, an endpoint will have an additional URL that can be used to connect as a new [`EventSource`](https://developer.mozilla.org/en-US/docs/Web/API/EventSource) which will receive `RAISE INFO` notifications:

```js
const source = new EventSource(url);
source.onmessage = event => {
    console.log(event.data);
};
```

##### Generated Client Code

This functionality is encapsulated into the generated TsClient (TypeScript and JavaScript code) as a function parameter that accepts a string:

```js
const createRaiseNoticesEventSource = (id = "") => new EventSource(baseUrl + "/api/raise-notices/info?" + id);

async function raiseNotices(
    info = msg => msg
) {
    const executionId = window.crypto.randomUUID();
    const eventSource = createRaiseNoticesEventSource(executionId);
    eventSource.onmessage = event => {
        info(event.data);
    };
    try {
        const response = await fetch(raiseNoticesUrl(), {
            method: "GET",
            headers: {
                "X-NpgsqlRest-ID": executionId
            },
        });
        return response.status;
    }
    finally {
        setTimeout(() => eventSource.close(), 1000);
    }
}
```

By default, only sessions that initiated the original code will receive these notifications by using the function parameter. This behavior is also configurable.

##### Configuration Properties

There are three new properties on every endpoint instance to support this feature:

```cs
public string? InfoEventsStreamingPath { get; set; } = null;
public InfoEventsScope InfoEventsScope { get; set; } = InfoEventsScope.Self;
public HashSet<string>? InfoEventsRoles { get; set; } = null;
```

##### InfoEventsStreamingPath
Additional path appended as a subpath to the main endpoint path (null disables info events). If the endpoint path is `/path` and this value is set to `/info`, the streaming path will be `/path/info`.

##### InfoEventsScope
Scope that determines to whom events are streamed:

- **`Self`** (default): Only the original endpoint initiator session, regardless of the security context.
- **`Matching`**: Sessions with matching security context of the endpoint initiator. If the endpoint initiator requires authorization, all authorized sessions will receive these messages. If the endpoint initiator requires authorization for certain roles, all sessions requiring the same roles will receive these messages.
- **`Authorize`**: Only authorized sessions will receive these messages. If the `InfoEventsRoles` property contains a list of roles, only sessions with those roles will receive messages.
- **`All`**: All sessions regardless of the security context will receive these messages.

##### InfoEventsRoles
List (hash set) of authorized roles that will receive messages when `InfoEventsScope` is set to `Authorize`.

##### Comment Annotations

There are two new sets of comment annotations to support this feature:

```
info_path [ path | true | false ]
info_events_path [ path | true | false ]
info_streaming_path [ path | true | false ]
info_scope [ self | matching | authorize | all ] | [ authorize [ role1, role2, role3 [, ...] ] ]
info_events_scope [ self | matching | authorize | all ] | [ authorize [ role1, role2, role3 [, ...] ] ]
info_streaming_scope [ self | matching | authorize | all ] | [ authorize [ role1, role2, role3 [, ...] ] ]
```

Note: these annotations are also available as comment annotation parameters (`key = value` format):

```
info_path = [ path | true | false ]
info_events_path = [ path | true | false ]
info_streaming_path = [ path | true | false ]
info_scope = [ self | matching | authorize | all ] | [ authorize [ role1, role2, role3 [, ...] ] ]
info_events_scope = [ self | matching | authorize | all ] | [ authorize [ role1, role2, role3 [, ...] ] ]
info_streaming_scope = [ self | matching | authorize | all ] | [ authorize [ role1, role2, role3 [, ...] ] ]
```

###### 1. Set the Info Streaming Path

```
info_path [ path | true | false ]
info_events_path [ path | true | false ]
info_streaming_path [ path | true | false ]
```

**Note:** This can also be boolean. When set to `true`, the info streaming path will be `/info` which will be added to the main path.

###### 2. Set the Info Streaming Scope

```
info_scope [ self | matching | authorize | all ] | [ authorize [ role1, role2, role3 [, ...] ] ]
info_events_scope [ self | matching | authorize | all ] | [ authorize [ role1, role2, role3 [, ...] ] ]
info_streaming_scope [ self | matching | authorize | all ] | [ authorize [ role1, role2, role3 [, ...] ] ]
```

Set the scope for sessions receiving info messages: `self` (default), `matching`, `authorize`, or `all`. When using `authorize`, add an optional list of authorized roles.

##### Per-Message Scope Control

Scope comment annotation can be set on individual messages as the `hint` parameter:

```sql
raise info 'Self messages will be received only by sessions that initiated the original endpoint. This is the default if not set otherwise.' using hint = 'self';

raise info 'Only for sessions with matching security context as the session that initiated the original endpoint.' using hint = 'matching';

raise info 'Only for authorized sessions.' using hint = 'authorize';

raise info 'Only for authorized sessions with roles role1 and role2.' using hint = 'authorize role1, role2';

raise info 'Message for all connected sessions.' using hint = 'all';
```

##### New Options

There are two new options to support this feature:

```cs
    /// <summary>
    /// Name of the request ID header that will be used to track requests. This is used to correlate requests with streaming connection ids.
    /// </summary>
    public string ExecutionIdHeaderName { get; set; } = "X-NpgsqlRest-ID";

    /// <summary>
    /// Collection of custom server-sent events response headers that will be added to the response when connected to the endpoint that is configured to return server-sent events.
    /// </summary>
    public Dictionary<string, StringValues> CustomServerSentEventsResponseHeaders { get; set; } = [];
```

###### ExecutionIdHeaderName

Default scope option (self) means that only sessions that initiated the call can receive these messages. To achieve that, generated client code injects a custom header on each request that has info streaming enabled with a random number value. This is the name of this header.

###### CustomServerSentEventsResponseHeaders

List of custom SSE (Server-Sent Events) response headers that will be added automatically. Some browsers or servers may require this to be customized.

#### Auth Changes

There are some slight breaking changes to how authentication and authorization works, specifically in claims handling. From this version, the library doesn't use Active Directory Federation Services Claim Types by default anymore.

The reason for this change is because Microsoft has been updating these values in newer versions which could break the authorization mechanism on updates. And since it is not really necessary, simple values are used instead.

Four different default options in AuthenticationOptions have changed values:

##### 1. UseActiveDirectoryFederationServicesClaimTypes

From true to false obviously. When this is set to true, the value of either of these options DefaultUserIdClaimType, DefaultNameClaimType, DefaultRoleClaimType will try to match the field name (ignoring case) of this table: https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claimtypes?view=net-9.0 and will have the corresponding value.

##### 2. DefaultUserIdClaimType

From `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier` to `nameidentifier`.

##### 3. DefaultNameClaimType

From `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name` to `name`.

##### 4. DefaultRoleClaimType

From `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/role` to `role`.

### NpgsqlRest Client

#### Fix Routine Source Initialization Bug

When configuration was missing empty `RoutineOptions` inside `NpgsqlRest` settings, routines wouldn't initialize. The quick fix was to add an empty `RoutineOptions` section like this:

```jsonc
{
  // ...
  "NpgsqlRest": {
    // ...
    "RoutineOptions": {
      "IncludeLanguages": null
    },
    // ...
  }
}
```

This is no longer required.

#### Kestrel Configuration Improvements

Previously, Kestrel configuration allowed configuring only `Endpoints` and `Certificates` sections. Now it allows configuring other Kestrel options like the `Limits` section and 6 other options. Full list:

```jsonc
{
  //...
  "Kestrel": {
      "Endpoints": {
          //...
      },
      "Certificates": {
          //...
      },

      // new settings
      "Limits": {
        "MaxConcurrentConnections": 100,
        "MaxConcurrentUpgradedConnections": 100,
        "MaxRequestBodySize": 30000000,
        "MaxRequestBufferSize": 1048576,
        "MaxRequestHeaderCount": 100,
        "MaxRequestHeadersTotalSize": 32768,
        "MaxRequestLineSize": 8192,
        "MaxResponseBufferSize": 65536,
        "KeepAliveTimeout": "00:02:00",
        "RequestHeadersTimeout": "00:00:30",
        "Http2": {
          "MaxStreamsPerConnection": 100,
          "HeaderTableSize": 4096,
          "MaxFrameSize": 16384,
          "MaxRequestHeaderFieldSize": 8192,
          "InitialConnectionWindowSize": 65535,
          "InitialStreamWindowSize": 65535,
          "MaxReadFrameSize": 16384,
          "KeepAlivePingDelay": "00:00:30",
          "KeepAlivePingTimeout": "00:01:00",
          "KeepAlivePingPolicy": "WithActiveRequests"
        },
        "Http3": {
          "MaxRequestHeaderFieldSize": 8192
        }
      },
      "DisableStringReuse": false,
      "AllowAlternateSchemes": false,
      "AllowSynchronousIO": false,
      "AllowResponseHeaderCompression": true,
      "AddServerHeader": true,
      "AllowHostHeaderOverride": false
  },
  //...
}
```

These are just the default values. 

Initial configuration is commented out so that the client uses the default versions of the framework which can change in newer versions. This opens the opportunity for Web Server optimizations like this, for example:

```jsonc
{
  "Kestrel": {
    "AddServerHeader": false, // remove unnecessary header
    "Limits": {
      // increase max limits for high workload
      "MaxConcurrentConnections": 10000,
      "MaxConcurrentUpgradedConnections": 10000
    }
  }
}
```

#### ThreadPool Configuration

You can also set thread pool parameters (min and max number of worker threads and completion port threads).

```jsonc
{
  "ThreadPool": {
    "MinWorkerThreads": null,
    "MinCompletionPortThreads": null,
    "MaxWorkerThreads": null,
    "MaxCompletionPortThreads": null
  }
}
```

If you are expecting higher workload, you can set the initial number of minimal threads to a higher number and not wait for them to scale up automatically (which affects latency):


```jsonc
{
  "ThreadPool": {
    "MinWorkerThreads": 1000,
    "MinCompletionPortThreads": 1000
  }
}
```

### TsClient

TsClient plugin code generator for TypeScript and JavaScript clients had a major revamp to support new features.

#### TsClient New Settings

There are 3 new settings supported as options and as configuration settings:

##### ExportEventSources

Set to true to export event sources create functions for streaming events. Default is true.

##### CustomImports

List of custom imports to add to the generated code. It adds a line to the file. Use full expression like `import { MyType } from './my-type';`. Default is an empty list.

##### CustomHeaders

Dictionary of custom headers to add to each request in generated code. Header key is automatically quoted if it doesn't contain quotes. Default is an empty dictionary.

#### TsClient Comment Annotation Parameters

TsClient now supports tweaking behavior with comment annotation parameters:

```
tsclient = [ false | off | disabled | disable | 0 ]
tsclient_events = [ [ false | off | disabled | disable | 0 ] | [ true | on | enabled | enable | 1 ] ]
tsclient_parse_url = [ [ false | off | disabled | disable | 0 ] | [ true | on | enabled | enable | 1 ] ]
tsclient_parse_request = [ [ false | off | disabled | disable | 0 ] | [ true | on | enabled | enable | 1 ] ]
tsclient_status_code = [ [ false | off | disabled | disable | 0 ] | [ true | on | enabled | enable | 1 ] ]
```

- `tsclient = [ false | off | disabled | disable | 0 ]` - disable tsclient code generation for the endpoint.
- `tsclient_events = [ [ false | off | disabled | disable | 0 ] | [ true | on | enabled | enable | 1 ] ]` - enable or disable info event parameter for endpoints with info events enabled.
- `tsclient_parse_url = [ [ false | off | disabled | disable | 0 ] | [ true | on | enabled | enable | 1 ] ]` - enable or disable info event parameter URL parsing.
- `tsclient_parse_request = [ [ false | off | disabled | disable | 0 ] | [ true | on | enabled | enable | 1 ] ]` - enable or disable info event parameter request parsing.
- `tsclient_status_code = [ [ false | off | disabled | disable | 0 ] | [ true | on | enabled | enable | 1 ] ]` - enable or disable status code in the return value.