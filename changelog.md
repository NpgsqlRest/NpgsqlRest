# Changelog

Note: The changelog for the older version can be found here: [Changelog Archive](https://github.com/NpgsqlRest/NpgsqlRest/blob/master/changelog-old.md)

---

## Version [3.1.0](https://github.com/NpgsqlRest/NpgsqlRest/tree/3.0.1) (2025-)

[Full Changelog](https://github.com/NpgsqlRest/NpgsqlRest/compare/3.1.0...3.0.1)

### Http Types

New feature that enables PostgreSQL functions to make HTTP requests to external APIs by using specially annotated composite types. When a function parameter uses a composite type with an HTTP definition comment, NpgsqlRest automatically invokes the HTTP request and populates the type fields with the response data before executing the function.

**Creating an HTTP Type:**

```sql
-- Create a composite type with response fields
create type weather_api as (
    body text,
    status_code int,
    headers json,
    content_type text,
    success boolean,
    error_message text
);

-- Add HTTP definition as a comment (RFC 7230 format)
comment on type weather_api is 'GET https://api.weather.com/v1/current?city={_city}
Authorization: Bearer {_api_key}
timeout 30s';
```

**Using the HTTP Type in a function:**

```sql
create function get_weather(
  _city text,
  _api_key text,
  _req weather_api
)
returns json
language plpgsql
as $$
begin
    if (_req).success then
        return (_req).body::json;
    else
        return json_build_object('error', (_req).error_message);
    end if;
end;
$$;
```

**HTTP Definition Format:**

The comment on the composite type follows a simplified HTTP message format similar to `.http` files:

```
METHOD URL [HTTP/version]
Header-Name: Header-Value
...

[request body]
```

Supported HTTP methods: `GET`, `POST`, `PUT`, `PATCH`, `DELETE`

**Timeout Directives:**

Timeout can be specified before the request line using various formats:

```
timeout 30
timeout 30s
timeout 00:00:30
@timeout 2 minutes
```

**Response Fields:**

The composite type fields are automatically populated based on their names (configurable via `HttpClientOptions`):

| Field Name      | Type            | Description                       |
|-----------------|-----------------|-----------------------------------|
| `body`          | `text`          | Response body content             |
| `status_code`   | `int` or `text` | HTTP status code (e.g., 200, 404) |
| `headers`       | `json`          | Response headers as JSON object   |
| `content_type`  | `text`          | Content-Type header value         |
| `success`       | `boolean`       | True for 2xx status codes         |
| `error_message` | `text`          | Error message if request failed   |

**Placeholder Substitution:**

URLs, headers, and request body in the type comment can contain placeholders in the format `{parameter_name}`. These placeholders are automatically replaced with the values of other function parameters that share the same name.

In the example above, the function `get_weather` has parameters `_city` and `_api_key`. The HTTP type comment contains placeholders `{_city}` and `{_api_key}` which are substituted with the actual parameter values when the HTTP request is made:

```sql
-- Type comment with placeholders
comment on type weather_api is 'GET https://api.weather.com/v1/current?city={_city}
Authorization: Bearer {_api_key}
timeout 30s';

-- Function with matching parameter names
create function get_weather(
  _city text,        -- Value substitutes {_city} placeholder
  _api_key text,     -- Value substitutes {_api_key} placeholder
  _req weather_api   -- HTTP type parameter (receives response)
)
...
```

When calling `GET /api/get-weather?_city=London&_api_key=secret123`, NpgsqlRest will:
1. Substitute `{_city}` with `London` and `{_api_key}` with `secret123`
2. Make the HTTP request to `https://api.weather.com/v1/current?city=London` with header `Authorization: Bearer secret123`
3. Populate the `_req` parameter fields with the response data
4. Execute the PostgreSQL function

**Configuration Options:**

Enable HTTP Types in `NpgsqlRestOptions.HttpClientOptions`:

```csharp
options.HttpClientOptions = new HttpClientOptions
{
    Enabled = true,
    IncludeSchemas = ["public"],
    ResponseBodyField = "body",
    ResponseStatusCodeField = "status_code",
    ResponseHeadersField = "headers",
    ResponseContentTypeField = "content_type",
    ResponseSuccessField = "success",
    ResponseErrorMessageField = "error_message"
};
```

### Other Changes and Fixes

- Fixed default value on `ErrorHandlingOptions.RemoveTraceId` configuration setting. Default is true as it should be.
- Fixed PostgreSQL parameter and result type mapping when default search path is not public.
- Fixed type on TypeScript client generation when returing error. Errors now return JSON object instead of string.
- Removed `options.md`, `annotations.md`, `client.md` and `login-endpoints.md` documentation files because dedicated website is now live: https://npgsqlrest.github.io/
- Added missing `CsvUploadKey` with value `"csv"` in `NpgsqlRest.UploadOptions.UploadHandlers` configuration.
- Moved authorization check after parameter parsing. This allows for endpoint to return proper 404 response codes when parameter is missing, instead of 400 when authorization fails.
- When using custom types in PostgreSQL function parameters (composite types, enums, etc), and those parameters are not supplied in the request, they will now default to NULL always. Previous behavior was 404 Not Found when parameter was missing.

## Version [3.0.1](https://github.com/NpgsqlRest/NpgsqlRest/tree/3.0.1) (2025-11-28)

[Full Changelog](https://github.com/NpgsqlRest/NpgsqlRest/compare/3.0.1...3.0.0)

- Fix: fix missing stack trace in AOT builds when exceptions are thrown.
- Fix: Fix failing Docker JIT image build.
- Change: removed error mapping for PostgreSQL error code 42883 (undefined_function) from HTTP 404 Not Found. Map it to default HTTP 500 Internal Server Error instead. This was confusing.

## Version [3.0.0](https://github.com/NpgsqlRest/NpgsqlRest/tree/3.0.0) (2025-11-27)

[Full Changelog](https://github.com/NpgsqlRest/NpgsqlRest/compare/3.0.0...2.36.1)

### Docker JIT Version

- New Docker image with .NET 10 JIT runtime: `npgsqlrest/npgsqlrest:3.0.0-jit`
- This image uses the standard .NET 10 runtime with JIT compilation instead of AOT compilation.
- Suitable for development and scenarios where AOT compilation is not required.
- JIT version can be faster to execute but slower startup time and larger image size compared to AOT version.

Image Size Comparison (approximate):

| Version | Size        |
|---------|-------------|
| AOT     | ~80-100 MB  |
| JIT     | ~200-250 MB |

### .NET 10 Target Framework 

- Upgraded target framework to .NET 10.
- Faster and more memory efficient.

### TsClient (Code Generation) Improvements

1) When return value is JSON or JSONB, generated TypeScript type is `any` instead of `string`.
2) New parameter annotation `tsclient_module`. Sets different module name for the generated TypeScript client file. For example: `tsclient_module = test` will create `test.ts` or `test.js` and add every and group endpoint to that module instead of the default.
3) Fixed and improved generated JSDoc comments for better IntelliSense support in IDEs. JavaScript JSDoc invlude proper types and TypeScript JSDoc will not include types to avoid duplication. All parameters comment now include description.
4) SSE generated parameters signature changed.

Fetch for SSE enabled endpoint now looks like this:

```typescript
/**
 * function test_sse()
 * returns table(
 *     id integer
 * )
 *
 * @remarks
 * comment on function test_sse is 'HTTP GET
 * authorize
 * upload for file_system
 * sse
 * tsclient_module = test';
 *
 * @param onMessage - Optional callback function to handle incoming SSE messages.
 * @param id - Optional execution ID for SSE connection. When supplied, only EventSource object with this ID in query string will will receive events.
 * @param closeAfterMs - Time in milliseconds to wait before closing the EventSource connection. Used only when onMessage callback is provided.
 * @param awaitConnectionMs - Time in milliseconds to wait after opening the EventSource connection before sending the request. Used only when onMessage callback is provided.
 * @returns {status: number, response: ITestSseResponse[]}
 *
 * @see FUNCTION test_sse
 */
export async function testSse(
    onMessage?: (message: string) => void,
    id: string | undefined = undefined,
    closeAfterMs = 1000,
    awaitConnectionMs: number | undefined = 0
) : Promise<{status: number, response: ITestSseResponse[]}> {
    const executionId = id ? id : window.crypto.randomUUID();
    let eventSource: EventSource;
    if (onMessage) {
        eventSource = createTestSseEventSource(executionId);
        eventSource.onmessage = (event: MessageEvent) => {
            onMessage(event.data);
        };
        if (awaitConnectionMs !== undefined) {
            await new Promise(resolve => setTimeout(resolve, awaitConnectionMs));
        }
    }
    try {
        const response = await fetch(baseUrl + "/api/test-sse", {
            method: "GET",
            headers: {
                "Content-Type": "application/json",
                "X-test-ID": executionId
            },
        });
        return {
            status: response.status,
            response: response.status == 200 ? await response.json() as ITestSseResponse[] : await response.text() as any
        };
    }
    finally {
        if (onMessage) {
            setTimeout(() => eventSource.close(), closeAfterMs);
        }
    }
}
```

### Info Events Streaming Changes (Server-Sent Events)

#### Name refactor: changed all "Info Events" related names to "SSE" to better reflect their purpose.

- Rename configuration key from `CustomServerSentEventsResponseHeaders` to `ServerSentEventsResponseHeaders`.
- Option from `CustomServerSentEventsResponseHeaders` to `SseResponseHeaders`.
- Comment annotations:
  - from `info_path`, `info_events_path`, `info_streaming_path` to `sse`, `sse_path`, `sse_events_path`
  - from `info_scope`, `info_events_scope`, `info_streaming_scope` to `sse_scope`, `sse_events_scope`

#### Removed Self scope level

- Removed `self` scope level for SSE events. Only `matching`, `authorize`, and `all` levels are supported now.
- Event will always be skipped if executing id is supplied in request header and in event source query parameter, and they don't match.

#### New Feature: Support for custom notice level

- New option and configuration:
    - configuration: `DefaultServerSentEventsEventNoticeLevel`
    - option: `public PostgresNoticeLevels DefaultSseEventNoticeLevel { get; set; } = PostgresNoticeLevels.INFO;`

Set the default notice level for SSE events when not specified in comment annotation. When SSE path is set, generate SSE events for PostgreSQL notice messages with this level or higher.

#### Other Comment Annotations Changes

- Setting SSE path (and optionally notice level) via comment annotations:

```
sse [ path ] [ on info | notice | warning ] 
sse_path [ path ] [ on info | notice | warning ]
sse_events_path [ path ] [ on info | notice | warning ]
```

Without argument, just `sse` or `sse_path` or `sse_events_path`, will set the path to default, which depends on default level (`info` for INFO level, `notice` for NOTICE level, etc).

Single argument is treated as path.

If path is followed by `on info` or `on notice` or `on warning`, it will set the notice level accordingly.

Note: you can also set sse path using parameter annotations syntax (`key = value`), for example `sse = /my_sse_path` or `sse_path = /my_sse_path`.

- New comment annotations to set custom SSE event notice level per endpoint:

```
sse_level [ info | notice | warning ]
sse_events_level [ info | notice | warning ]
```

Note: you can also set sse level using parameter annotations syntax (`key = value`), for example `sse_level = info`, etc.

- Scope annotations changed name to match new SSE naming:

```
sse_scope [ [ matching | authorize | all ] | [ authorize [ role_or_user1, role_or_user1, role_or_user1 [, ...] ] ] ] 
sse_events_scope [ [ matching | authorize | all ] | [ authorize [ role_or_user1, role_or_user1, role_or_user1 [, ...] ] ] ] 
```

### Timeout Handling

- Timeouts are not retried automatically by NpgsqlRest anymore. 
- Timeout error policy can be set in `ErrorHandlingOptions` section of client configuration.
- Default mapping for timeout errors: `"TimeoutErrorMapping": {"StatusCode": 504, "Title": "Command execution timed out", "Details": null, "Type": null}`
- Configuration option `CommandTimeout` is using PostgreSQL interval format (for example: '30 seconds' or '30s', '1 minute' or '1min', etc.) instead of integer seconds.
- Comment annotation is also now using PostgreSQL interval format (for example: '30 seconds' or '30s', '1 minute' or '1min', etc.) instead of integer seconds.
- Option `CommandTimeout` is now TimeSpan? instead of int.

### OpenAPI 3.0 Support

Added OpenAPI 3.0 support with the new `NpgsqlRest.OpenApi` plugin (available as a separate NuGet package as library plugin).

Also, added new client configuration section `OpenApiOptions` to configure OpenAPI generation and serving. 

New configuration:

```json
{
  "NpgsqlRest": {
    //
    // Enable or disable the generation of OpenAPI files for NpgsqlRest endpoints.
    //
    "OpenApiOptions": {
      "Enabled": false,
      //
      // File name for the generated OpenAPI file. Set to null to skip the file generation.
      //
      "FileName": "npgsqlrest_openapi.json",
      //
      // URL path for the OpenAPI endpoint. Set to null to skip the endpoint generation.
      //
      "UrlPath": "/openapi.json",
      //
      // Set to true to overwrite existing files.
      //
      "FileOverwrite": true,
      //
      // The title of the OpenAPI document. This appears in the "info" section of the OpenAPI specification.
      // If not set, the database name from the ConnectionString will be used.
      //
      "DocumentTitle": null,
      //
      // The version of the OpenAPI document. This appears in the "info" section of the OpenAPI specification.
      // When null, default is "1.0.0".
      //
      "DocumentVersion": "1.0.0",
      //
      // Optional description of the API. This appears in the "info" section of the OpenAPI specification.
      //
      "DocumentDescription": null,
      //
      // Include current server information in the "servers" section of the OpenAPI document.
      //
      "AddCurrentServer": true,
      //
      // Additional server entries to add to the "servers" section of the OpenAPI document.
      // Each server entry must have "Url" property and optional "Description" property.
      //
      "Servers": [/*{"Url": "https://api.example.com", "Description": "Production server"}*/],
      //
      // Security schemes to include in the OpenAPI document.
      // If not specified, a default Bearer authentication scheme will be added for endpoints requiring authorization.
      // Supported types: "Http" (for Bearer/Basic auth) and "ApiKey" (for Cookie/Header/Query auth).
      // Examples:
      // - Bearer token: {"Name": "bearerAuth", "Type": "Http", "Scheme": "Bearer", "BearerFormat": "JWT"}
      // - Cookie auth: {"Name": "cookieAuth", "Type": "ApiKey", "In": ".AspNetCore.Cookies", "ApiKeyLocation": "Cookie"}
      // - Basic auth: {"Name": "basicAuth", "Type": "Http", "Scheme": "Basic"}
      //
      "SecuritySchemes": [
        /*{
          "Name": "bearerAuth",
          "Type": "Http",
          "Scheme": "Bearer",
          "BearerFormat": "JWT",
          "Description": "JWT Bearer token authentication"
        },
        {
          "Name": "cookieAuth",
          "Type": "ApiKey",
          "In": ".AspNetCore.Cookies",
          "ApiKeyLocation": "Cookie",
          "Description": "Cookie-based authentication"
        }*/
      ]
    }
  }
}
```

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
  "ErrorHandlingOptions": {
    // Remove Type URL from error responses. Middleware automatically sets a default Type URL based on the HTTP status code that points to the RFC documentation.
    "RemoveTypeUrl": false,
    // Remove TraceId field from error responses. Useful in development and debugging scenarios to correlate logs with error responses.
    "RemoveTraceId": true,
    //
    // Default policy name to use from the ErrorCodePolicies section.
    //
    "DefaultErrorCodePolicy": "Default",
    //
    // Timeout error mapping when command timeout occurs (see NpgsqlRest CommandTimeout setting).
    //
    "TimeoutErrorMapping": {"StatusCode": 504, "Title": "Command execution timed out", "Details": null, "Type": null}, // timeout error case -> 504 Gateway Timeout
    //
    // Named policies for mapping of PostgreSQL error codes to HTTP Status Codes.
    //
    // If routine raises these PostgreSQL error codes, endpoint will return these HTTP Status Codes.
    // See https://www.postgresql.org/docs/current/errcodes-appendix.html
    // Exception is timeout, which is not a PostgreSQL error code, but a special case when command timeout occurs.
    //
    // - StatusCode: HTTP status code to return.
    // - Title: Optional title field in response JSON. When null, actual error message is used.
    // - Details: Optional details field in response JSON. When null, PostgreSQL Error Code is used.
    // - Type: Optional types field in response JSON. A URI reference [RFC3986] that identifies the problem type. Set to null to use default. Or RemoveTypeUrl to true to disable.
    //
    "ErrorCodePolicies": [{
      "Name": "Default",
      "ErrorCodes": {
        "42501": {"StatusCode": 403, "Title": "Insufficient Privilege", "Details": null, "Type": null},   // query_canceled      -> 403 Forbidden
        "57014": {"StatusCode": 205, "Title": "Cancelled", "Details": null, "Type": null},                // query_canceled      -> 205 Reset Content
        "P0001": {"StatusCode": 400, "Title": null, "Details": null, "Type": null},                       // raise_exception     -> 400 Bad Request
        "P0004": {"StatusCode": 400, "Title": null, "Details": null, "Type": null},                       // assert_failure      -> 400 Bad Request
        "42883": {"StatusCode": 404, "Title": "Not Found", "Details": null, "Type": null},                // undefined_function  -> 404 Not Found
      }
    }]
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
    
    public ErrorCodeMappingOptions? TimeoutErrorMapping { get; set; } = new()
    {
        StatusCode = 504,
        Title = "Command execution timed out"
    };

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
- Removed unnecessary type casting when routine source returns set with embedded composite type.
- Fix: fixed incorrect handling of types with modifier (e.g. varchar(100), numeric(10,2), etc). This causes type with modifiers to be serialized as incorrect type.
- Fix: fixed incorrect parameter logging when parameters were added from user claims as string array (roles, permissions, etc).
- Fix: user claims mapping to parameters or context will now by default be NULL when claim is null or empty string. Previous behavior was to map empty string as empty string.
- Remove two logging options: `LogEndpointCreatedInfo` and `LogAnnotationSetInfo`. By default, all command parameters and values are logged at Debug level.
- Refactor comment annotation paring for better maintainability.
- .NET10 Upgrade.

#### Login Endpoint Changes

- Changed option `NpgsqlRestAuthenticationOptions.MessageColumnName` to `NpgsqlRestAuthenticationOptions.BodyColumnName` (and corresponding client configuration option) to better reflect its purpose.
- Default value of `NpgsqlRestAuthenticationOptions.BodyColumnName` is now `body` instead of `message`.
- Added new option `NpgsqlRestAuthenticationOptions.ResponseTypeColumnName` (and corresponding client configuration option) to specify the response type column name for login endpoint. Default is `application/json`.
