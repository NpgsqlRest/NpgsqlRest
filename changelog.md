# Changelog

Note: The changelog for the older version can be found here: [Changelog Archive](https://github.com/NpgsqlRest/NpgsqlRest/blob/master/changelog-old.md)

---

TODOs:

- TODO add JIT version of Docker

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

- TODO add to TsClient diffent files based on parameters

- TODO fix TsClient, when returning setof or table and one of the columns is json, it should be "any" or "unknown" not "string"

- TODO fix claims to parameters or user context mapping - if empty string is passed, use null (make it optional with default true)

- TODO fix array parameter logging: e.g. -- $6 text[] = 'System.Collections.Generic.List`1[System.String]'

- TODO fix: when returning char(1) in JSON, value is not quoted!

## Version [3.0.0](https://github.com/NpgsqlRest/NpgsqlRest/tree/3.0.0) (date is TBD)

[Full Changelog](https://github.com/NpgsqlRest/NpgsqlRest/compare/2.36.0...2.36.1)

### Timeout Handling

- Timeouts are not retried automatically by NpgsqlRest anymore. 
- Timeout error policy can be set in `ErrorHandlingOptions` section of client configuration.
- Special error code mapping in default error code policy for timeout errors: `"timeout": {"StatusCode": 504, "Title": "Command execution timed out", "Details": null, "Type": null} // special timeout case -> 504 Gateway Timeout`
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
- Removed unnecessary type casting when routine source returns set with embedded composite type.

#### Login Endpoint Changes

- Changed option `NpgsqlRestAuthenticationOptions.MessageColumnName` to `NpgsqlRestAuthenticationOptions.BodyColumnName` (and corresponding client configuration option) to better reflect its purpose.
- Default value of `NpgsqlRestAuthenticationOptions.BodyColumnName` is now `body` instead of `message`.
- Added new option `NpgsqlRestAuthenticationOptions.ResponseTypeColumnName` (and corresponding client configuration option) to specify the response type column name for login endpoint. Default is `application/json`.

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
