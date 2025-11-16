# Changelog Archive

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

## Version [2.28.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.28.0) (2025-06-12)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.27.0...2.28.0)

### Core Library

#### Improved Error Handling

- All errors are handled properly now. Non-PostgreSQL errors will return 501 and all relevant data will be sent to the default logger.

#### Big Improvements in Upload Handlers

- All Upload Handlers will always return a JSON metadata which will be array of objects. Each object have following signature:

```ts
interface UploadMetadata {
  type: string; // handler key: large_object, file_system or csv
  fileName: string;
  contentType: string;
  size: number;
  success: boolean; // will be true for only for status=Ok
  status: string; // Empty, ProbablyBinary, InvalidImage, InvalidFormat, NoNewLines, InvalidMimeType, Ignored, Ok
  [key: string]: string | number | boolean; // depends on a handler type
}
```

There are 8 shared parameters:

- `stop_after_first_success`: Set to true to stop the upload after the first succesuful upload when multiple handlers are used. Subsequent files will be ignored (status: `Ignored`). Default is false.
- `included_mime_types`: CSV string containing mime type patters to include, set to null to ignore. Default is null.
- `excluded_mime_types`: CSV string containing mime type patters to exclude, set to null to ignore. Default is null.
- `buffer_size`: Size of a buffer in bytes when uploading a raw content to handlers like `large_object` and `file_system`. Default is null.
- `check_text`: Boolean value to check is a file a valid textual file or a binary. Set to true to accpet only text file. Default is false.
- `check_image`: Boolean value to check is a file a valid image. Set to true to accpet only images. It can also be a CSV text with allowed image types, such as `jpg`, `png`, `gif`, `bmp`, `tiff` or `webp`. Default is false.
- `test_buffer_size`: Buffer size in bytes when checking text files. Default is 4096.
- `non_printable_threshold`: Maximum count of non prinatble charactes allowed in test buffer to consider a valid text file. Default is 5.

Upload handler `large_object` accepts the following parameters:

- `stop_after_first_success`
- `included_mime_types`
- `excluded_mime_types`
- `buffer_size`
- `oid`
- `check_text`
- `check_image`
- `test_buffer_size`
- `non_printable_threshold`
- `large_object_included_mime_types`
- `large_object_excluded_mime_types`
- `large_object_buffer_size`
- `large_object_oid`
- `large_object_check_text`
- `large_object_check_image`
- `large_object_test_buffer_size`
- `large_object_non_printable_threshold`

Upload handler `file_system` accepts the following parameters:

- `stop_after_first_success`
- `included_mime_types`
- `excluded_mime_types`
- `buffer_size`
- `path`
- `file`
- `unique_name`
- `create_path`
- `check_text`
- `check_image`
- `test_buffer_size`
- `non_printable_threshold`
- `file_system_included_mime_types`
- `file_system_excluded_mime_types`
- `file_system_buffer_size`
- `file_system_path`
- `file_system_file`
- `file_system_unique_name`
- `file_system_create_path`
- `file_system_check_text`
- `file_system_check_image`
- `file_system_test_buffer_size`
- `file_system_non_printable_threshold`

Upload handler `csv` accepts the following parameters:

- `stop_after_first_success`
- `included_mime_types`
- `excluded_mime_types`
- `check_format`
- `test_buffer_size`
- `non_printable_threshold`
- `delimiters`
- `has_fields_enclosed_in_quotes`
- `set_white_space_to_null`
- `row_command`
- `csv_included_mime_types`
- `csv_excluded_mime_types`
- `csv_check_format`
- `csv_test_buffer_size`
- `csv_non_printable_threshold`
- `csv_delimiters`
- `csv_has_fields_enclosed_in_quotes`
- `csv_set_white_space_to_null`
- `csv_row_command`

### NpgsqlRest Client

#### Added UseCryptographicAlgorithms option to DataProtection

```jsonc
{
  // ...

  //
  // Data protection settings. Encryption keys for Auth Cookies and Antiforgery tokens.
  //
  "DataProtection": {

    // ...

    //
    // When disabled, data protection keys will be stored in an unencrypted form
    //
    "UseCryptographicAlgorithms": {
      "Enabled": false,
      // AES_128_CBC, AES_192_CBC, AES_256_CBC, AES_128_GCM, AES_192_GCM, AES_256_GCM
      "EncryptionAlgorithm": "AES_256_CBC",
      // HMACSHA256, HMACSHA512
      "ValidationAlgorithm": "HMACSHA256"
    }
  },

  // ...
}
```

#### Improved CORS Handling 

- Two more options: `"AllowCredentials": true` and `"PreflightMaxAgeSeconds": 600`

#### Excel Upload Handler 

Excel Upload Handler Implemented only in the Client Application. It is using a lightwieght ExcelDataReader library to handle Excel files.

It can accept any of these parameters:

- `stop_after_first_success`
- `included_mime_types`
- `excluded_mime_types`
- `sheet_name`
- `all_sheets`
- `time_format`
- `date_format`
- `datetime_format`
- `row_is_json`
- `row_command`
- `excel_included_mime_types`
- `excel_excluded_mime_types`
- `excel_sheet_name`
- `excel_all_sheets`
- `excel_time_format`
- `excel_date_format`
- `excel_datetime_format`
- `excel_row_is_json`
- `excel_row_command`

#### New Upload Options

- Defualt configuration now looks like this:

```jsonc
{
  // ...

  //
  // Data protection settings. Encryption keys for Auth Cookies and Antiforgery tokens.
  //
  "NpgsqlRest": {

    // ...

    "UploadOptions": {
      "Enabled": false,
      "LogUploadEvent": true,
      "LogUploadParameters": false,
      //
      // Handler that will be used when upload handler or handlers are not specified.
      //
      "DefaultUploadHandler": "large_object",
      //
      // Gets or sets a value indicating whether the default upload metadata parameter should be used.
      //
      "UseDefaultUploadMetadataParameter": false,
      //
      // Name of the default upload metadata parameter. This parameter is used to pass metadata to the upload handler. The metadata is passed as a JSON object.
      //
      "DefaultUploadMetadataParameterName": "_upload_metadata",
      //
      // Gets or sets a value indicating whether the default upload metadata context key should be used.
      //
      "UseDefaultUploadMetadataContextKey": false,
      //
      // Name of the default upload metadata context key. This key is used to pass the metadata to the upload handler. The metadata is passed as a JSON object.
      //
      "DefaultUploadMetadataContextKey": "request.upload_metadata",
      //
      // Upload handlers specific settings.
      //
      "UploadHandlers": {
        //
        // General settings for all upload handlers
        //
        "StopAfterFirstSuccess": false,
        // csv string containing mime type patters, set to null to ignore
        "IncludedMimeTypePatterns": null,
        // csv string containing mime type patters, set to null to ignore
        "ExcludedMimeTypePatterns": null,
        "BufferSize": 8192, // Buffer size for the upload handlers file_system and large_object, in bytes. Default is 8192 bytes (8 KB).
        "TextTestBufferSize": 4096, // Buffer sample size for testing textual content, in bytes. Default is 4096 bytes (4 KB).
        "TextNonPrintableThreshold": 5, // Threshold for non-printable characters in the text buffer. Default is 5 non-printable characters.
        "AllowedImageTypes": "jpeg, png, gif, bmp, tiff, webp", // Comma-separated list of allowed image types when checking images".

        //
        // Enables upload handlers for the NpgsqlRest endpoints that uses PostgreSQL Large Objects API
        //
        "LargeObjectEnabled": true,
        "LargeObjectKey": "large_object",
        "LargeObjectCheckText": false,
        "LargeObjectCheckImage": false,

        //
        // Enables upload handlers for the NpgsqlRest endpoints that uses file system
        //
        "FileSystemEnabled": true,
        "FileSystemKey": "file_system",
        "FileSystemPath": "/tmp/uploads",
        "FileSystemUseUniqueFileName": true,
        "FileSystemCreatePathIfNotExists": true,
        "FileSystemCheckText": false,
        "FileSystemCheckImage": false,

        //
        // Enables upload handlers for the NpgsqlRest endpoints that uploads CSV files to a row command
        //
        "CsvUploadEnabled": true,
        "CsvUploadCheckFileStatus": true,
        "CsvUploadDelimiterChars": ",",
        "CsvUploadHasFieldsEnclosedInQuotes": true,
        "CsvUploadSetWhiteSpaceToNull": true,
        //
        // $1 - row index (1-based), $2 - parsed value text array, $3 - result of previous row command, $4 - json metadata for upload
        //
        "CsvUploadRowCommand": "call process_csv_row($1,$2,$3,$4)",

        //
        // Enables upload handlers for the NpgsqlRest endpoints that uploads Excel files to a row command
        //
        "ExcelUploadEnabled": true,
        "ExcelKey": "excel",
        "ExcelSheetName": null, // null to use the first available
        "ExcelAllSheets": false,
        "ExcelTimeFormat": "HH:mm:ss",
        "ExcelDateFormat": "yyyy-MM-dd",
        "ExcelDateTimeFormat": "yyyy-MM-dd HH:mm:ss",
        "ExcelRowDataAsJson": false,
        //
        // $1 - row index (1-based), $2 - parsed value text array, $3 - result of previous row command, $4 - json metadata for upload
        //
        "ExcelUploadRowCommand": "call process_excel_row($1,$2,$3,$4)"
      }
    },

    // ...
  },

  // ...
}
```


## Version [2.27.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.27.0) (2025-05-19)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.26.0...2.27.0)

This is a big release that features several important changes.

### AuthenticationOptions Changes

Authentication options have been expanded to include new parameters. Specifically, to be able to bind user claims to parameters (user id, user name, user roles or custom) or to send them as PostgreSQL context.

Another important change is that beside `PasswordVerificationFailedCommand` option, there is also `PasswordVerificationSucceededCommand`. And both of these options contain a command expression that receives three optional text parameters:
- Authentication scheme used for the login
- User id used for the login.
- User name used for the login.

Parameters are positional: first is scheme, second is user id, and third is user name. User id and user name have switched places from the last version, since user id is usually more important.

Class `NpgsqlRestAuthenticationOptions` now looks like this:

```csharp
/// <summary>
/// Authentication options for the NpgsqlRest middleware.
/// </summary>
public class NpgsqlRestAuthenticationOptions
{
    /// <summary>
    /// Authentication type used with the Login endpoints to set the authentication type for the new `ClaimsIdentity` created by the login.
    ///
    /// This value must be set to non-null when using login endpoints, otherwise, the following error will raise: `SignInAsync when principal.Identity.IsAuthenticated is false is not allowed when AuthenticationOptions.RequireAuthenticatedSignIn is true.`
    /// 
    /// If the value is not set and the login endpoint is present, it will automatically get the database name from the connection string.
    /// </summary>
    public string? DefaultAuthenticationType { get; set; } = null;

    /// <summary>
    /// The default column name in the data reader which will be used to read the value to determine the success or failure of the login operation.
    /// 
    /// - If this column is not present, the success is when the endpoint returns any records.
    /// - If this column is present, it must be either a boolean to indicate success or a numeric value to indicate the HTTP Status Code to return.
    /// - If this column is present and retrieves a numeric value, that value is assigned to the HTTP Status Code and the login will authenticate only when this value is 200.
    /// </summary>
    public string? StatusColumnName { get; set; } = "status";

    /// <summary>
    /// The default column name in the data reader which will be used to read the value of the authentication scheme of the login process.
    /// 
    /// If this column is not present in the login response the default authentication scheme is used. Return new value to use a different authentication scheme with the login endpoint.
    /// </summary>
    public string? SchemeColumnName { get; set; } = "scheme";

    /// <summary>
    /// The default column name in the data reader which will return a text message with the login status.
    /// </summary>
    public string? MessageColumnName { get; set; } = "message";

    /// <summary>
    /// Any columns retrieved from the reader during login, which don't have a name in `StatusColumnName` or `SchemeColumnName` will be used to create a new identity  `Claim`:
    /// 
    /// Column name will be interpreted as the claim type and the associated reader value for that column will be the claim value.
    /// 
    /// When this value is set to true (default) column name will try to match the constant name in the [ClaimTypes class](https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claimtypes?view=net-8.0) to retrieve the value.
    /// 
    /// For example, column name `NameIdentifier` or `name_identifier` (when transformed by the default name transformer) will match the key `NameIdentifier` which translates to this: http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier
    /// </summary>
    public bool UseActiveDirectoryFederationServicesClaimTypes { get; set; } = true;

    /// <summary>
    /// Default claim type for user id.
    /// </summary>
    public string DefaultUserIdClaimType { get; set; } = ClaimTypes.NameIdentifier;

    private string _defaultNameClaimType = ClaimTypes.Name;
    internal bool UsingDefaultNameClaimType = true;

    /// <summary>
    /// Default claim type for user name.
    /// </summary>
    public string DefaultNameClaimType
    {
        get => _defaultNameClaimType;
        set
        {
            _defaultNameClaimType = value;
            UsingDefaultNameClaimType = string.Equals(value, ClaimTypes.Name, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Default claim type for user roles.
    /// </summary>
    public string DefaultRoleClaimType { get; set; } = ClaimTypes.Role;

    /// <summary>
    /// If true, return any response from auth endpoints (login and logout) if response hasn't been written by auth handler.
    /// For cookie auth, this will return full record to response as returned by the routine.
    /// For bearer token auth, this will be ignored because bearer token auth writes it's own response (with tokens).
    /// This option will also be ignored if message column is present (see MessageColumnName option).
    /// Default is false.
    /// </summary>
    public bool SerializeAuthEndpointsResponse { get; set; } = false;

    /// <summary>
    /// Don't write real parameter values when logging parameters from auth endpoints and obfuscate instead.
    /// This prevents user credentials including password to end up in application logs.
    /// Default is true.
    /// </summary>
    public bool ObfuscateAuthParameterLogValues { get; set; } = true;

    /// <summary>
    /// The default column name in the data reader which will be used to read the value of the hash of the password.
    /// If this column is present, the value will be used to verify the password from the password parameter.
    /// Password parameter is the first parameter which name contains the value of PasswordParameterNameContains.
    /// If verification fails, the login will fail and the HTTP Status Code will be set to 404 Not Found.
    /// </summary>
    public string HashColumnName { get; set; } = "hash";

    /// <summary>
    /// The default name of the password parameter. The first parameter which name contains this value will be used as the password parameter.
    /// This is used to verify the password from the password parameter when login endpoint returns a hash of the password (see HashColumnName).
    /// </summary>
    public string PasswordParameterNameContains { get; set; } = "pass";

    /// <summary>
    /// Default password hasher object. Inject custom password hasher object to add default password hasher.
    /// </summary>
    public IPasswordHasher? PasswordHasher { get; set; } = new PasswordHasher();

    /// <summary>
    /// Command that is executed when the password verification fails. There are three text parameters:
    ///     - authentication scheme used for the login (if exists)
    ///     - user id used for the login (if exists)
    ///     - user name used for the login (if exists)
    /// Please use PostgreSQL parameter placeholders for the parameters ($1, $2, $3).
    /// </summary>
    public string? PasswordVerificationFailedCommand { get; set; } = null;

    /// <summary>
    /// Command that is executed when the password verification succeeds. There are three text parameters:
    ///     - authentication scheme used for the login (if exists)
    ///     - user id used for the login (if exists)
    ///     - user name used for the login (if exists)
    /// Please use PostgreSQL parameter placeholders for the parameters ($1, $2, $3).
    /// </summary>
    public string? PasswordVerificationSucceededCommand { get; set; } = null;

    /// <summary>
    /// Set user context to true for all requests. 
    /// When this is set to true, user information (user id, user name and user roles) will be set to the context variables.
    /// You can set this individually for each request.
    /// </summary>
    public bool UseUserContext { get; set; } = false;

    /// <summary>
    /// User id context key that is used to set context variable for the user id.
    /// </summary>
    public string? UserIdContextKey { get; set; } = "request.user_id";

    /// <summary>
    /// User name context key that is used to set context variable for the user name.
    /// </summary>
    public string? UserNameContextKey { get; set; } = "request.user_name";

    /// <summary>
    /// User roles context key that is used to set context variable for the user roles.
    /// </summary>
    public string? UserRolesContextKey { get; set; } = "request.user_roles";

    /// <summary>
    /// IP address context key that is used to set context variable for the IP address.
    /// </summary>
    public string? IpAddressContextKey { get; set; } = "request.ip_address";

    /// <summary>
    /// When this value is set and user context is used, all user claims will be serialized to JSON value and set to the context variable with this name.
    /// </summary>
    public string? UserClaimsContextKey { get; set; } = null;

    /// <summary>
    /// Set user parameters to true for all requests. 
    /// When this is set to true, user information (user id, user name and user roles) will be set to the matching parameter names if available.
    /// You can set this individually for each request.
    /// </summary>
    public bool UseUserParameters { get; set; } = false;

    /// <summary>
    /// User id parameter name that is used to set parameter value for the user id.
    /// Parameter name can be original or converted and it has to be the text type.
    /// </summary>
    public string? UserIdParameterName { get; set; } = "_user_id";

    /// <summary>
    /// User name parameter name that is used to set parameter value for the user name.
    /// Parameter name can be original or converted and it has to be the text type.
    /// </summary>
    public string? UserNameParameterName { get; set; } = "_user_name";

    /// <summary>
    /// User roles parameter name that is used to set parameter value for the user roles.
    /// Parameter name can be original or converted and it has to be the text array type.
    /// </summary>
    public string? UserRolesParameterName { get; set; } = "_user_roles";

    /// <summary>
    /// IP address parameter name that is used to set parameter value for the IP address.
    /// Parameter name can be original or converted and it has to be the text type.
    /// </summary>
    public string? IpAddressParameterName { get; set; } = "_ip_address";

    /// <summary>
    /// All user claims will be serialized to JSON value and set to the parameter with this name.
    /// </summary>
    public string? UserClaimsParameterName { get; set; } = "_user_claims";
}
```

Previously, this logic was handled only in client application and inefficiently so. That means that there is a new configuration section in the client application for handling authentication:

```jsonc
{
  // ...
  "NpgsqlRest": {
    // ...

    //
    // Authentication options for NpgsqlRest endpoints
    //
    "AuthenticationOptions": {
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsdefaultauthenticationtype
      //
      "DefaultAuthenticationType": null,

      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsstatuscolumnname
      //
      "StatusColumnName": "status",

      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsschemecolumnname
      //
      "SchemeColumnName": "scheme",

      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsmessagecolumnname
      //
      "MessageColumnName": "message",
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsuseactivedirectoryfederationservicesclaimtypes
      //
      "UseActiveDirectoryFederationServicesClaimTypes": true,
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsdefaultuseridclaimtype
      //
      "DefaultUserIdClaimType": "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier",
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsdefaultnameclaimtype
      //
      "DefaultNameClaimType": "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsdefaultroleclaimtype
      //
      "DefaultRoleClaimType": "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/role",
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsserializeauthendpointsresponse
      //
      "SerializeAuthEndpointsResponse": false,
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsobfuscateauthparameterlogvalues
      //
      "ObfuscateAuthParameterLogValues": true,
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionshashcolumnname
      //
      "HashColumnName": "hash",
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionspasswordparameternamecontains
      //
      "PasswordParameterNameContains": "pass",
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionspasswordverificationfailedcommand
      //
      "PasswordVerificationFailedCommand": null,
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionspasswordverificationsucceededcommand
      //
      "PasswordVerificationSucceededCommand": null,
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsuseusercontext
      //
      "UseUserContext": false,
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsuseridcontextkey
      //
      "UserIdContextKey": "request.user_id",
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsusernamecontextkey
      //
      "UserNameContextKey": "request.user_name",
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsuserrolescontextkey
      //
      "UserRolesContextKey": "request.user_roles",
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsipaddresscontextkey
      //
      "IpAddressContextKey": "request.ip_address",
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsuserclaimscontextkey
      //
      "UserClaimsContextKey": null,
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsuseruserparameters
      //
      "UseUserParameters": false,
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsuseridparametername
      //
      "UserIdParameterName": "_user_id",
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsusernameparametername
      //
      "UserNameParameterName": "_user_name",
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsuserrolesparametername
      //
      "UserRolesParameterName": "_user_roles",
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsipaddressparametername
      //
      "IpAddressParameterName": "_ip_address",
      //
      // See https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsuserclaimsparametername
      //
      "UserClaimsParameterName": "_user_claims",
      //
      // Url path that will be used for the login endpoint. If NULL, the login endpoint will not be created.
      // See more on login endpoints at https://vb-consulting.github.io/npgsqlrest/login-endpoints
      //
      "LoginPath": null,
      //
      // Url path that will be used for the logout endpoint. If NULL, the logout endpoint will not be created.
      // See more on logout endpoints at https://vb-consulting.github.io/npgsqlrest/annotations/#logout
      //
      "LogoutPath": null,
      //
      // Routines that have this values set to true, will parse the response before it is returned to the client by using name placeholders from this section.
      //
      // Use curly braces to define the name placeholders in the response. For example, {user_id} will be replaced. 
      // The replacement value in this example is the value that would be assigned to he UserIdParameterName parameter if UseUserParameters was used. That means user id.
      //
      "ParseResponseUsingUserParameters": false
    }

    // ...
}
```

### UploadOptions Changes

Upload options in core library have also been improved. New options include:
- `LogUploadEvent` - should we log upload events.
- `DefaultUploadMetadataParameterName` and `UseDefaultUploadMetadataParameter` - set the default upload metadata parameter once for all.
- `DefaultUploadMetadataContextKey` and `UseDefaultUploadMetadataContextKey` - set metadata with PostgreSQL context first instead of parameter.

Here is the new `NpgsqlRestUploadOptions` class:

```csharp
/// <summary>
/// Upload options for the NpgsqlRest middleware.
/// </summary>
public class NpgsqlRestUploadOptions
{
    public bool Enabled { get; set; } = true;
    public bool LogUploadEvent { get; set; } = true;

    /// <summary>
    /// Default upload handler name. This value is used when the upload handlers are not specified.
    /// </summary>
    public string DefaultUploadHandler { get; set; } = "large_object";

    /// <summary>
    /// Default upload handler options. 
    /// Set this option to null to disable upload handlers or use this to modify upload handler options.
    /// </summary>
    public UploadHandlerOptions DefaultUploadHandlerOptions { get; set; } = new UploadHandlerOptions();

    /// <summary>
    /// Upload handlers dictionary map. 
    /// When the endpoint has set Upload to true, this dictionary will be used to find the upload handlers for the current request. 
    /// Handler will be located by the key values from the endpoint UploadHandlers string array property if set or by the default upload handler (DefaultUploadHandler option).
    /// Set this option to null to use default upload handler from the UploadHandlerOptions property.
    /// Set this option to empty dictionary to disable upload handlers.
    /// Set this option to a dictionary with one or more upload handlers to enable your own custom upload handlers.
    /// </summary>
    public Dictionary<string, Func<ILogger?, IUploadHandler>>? UploadHandlers { get; set; } = null;

    /// <summary>
    /// Gets or sets a value indicating whether the default upload metadata parameter should be used.
    /// </summary>
    public bool UseDefaultUploadMetadataParameter { get; set; } = false;

    /// <summary>
    /// Name of the default upload metadata parameter. 
    /// This parameter will be automatically assigned with the upload metadata JSON string when the upload is completed if UseDefaultUploadMetadataParameter is set to true.
    /// </summary>
    public string DefaultUploadMetadataParameterName { get; set; } = "_upload_metadata";

    /// <summary>
    /// Gets or sets a value indicating whether the default upload metadata context key should be used.
    /// </summary>
    public bool UseDefaultUploadMetadataContextKey { get; set; } = false;

    /// <summary>
    /// Name of the default upload metadata context key.
    /// This context key will be automatically assigned to context with the upload metadata JSON string when the upload is completed if UseDefaultUploadMetadataContextKey is set to true.
    /// </summary>
    public string DefaultUploadMetadataContextKey { get; set; } = "request.upload_metadata";
}
```

This also required change in client application configuration:

```jsonc
{
  // ...
  "NpgsqlRest": {
    // ...

    "UploadOptions": {
      "Enabled": true,
      "LogUploadEvent": true,
      //
      // Handler that will be used when upload handler or handlers are not specified.
      //
      "DefaultUploadHandler": "large_object",
      //
      // Gets or sets a value indicating whether the default upload metadata parameter should be used.
      //
      "UseDefaultUploadMetadataParameter": false,
      //
      // Name of the default upload metadata parameter. This parameter is used to pass metadata to the upload handler. The metadata is passed as a JSON object.
      //
      "DefaultUploadMetadataParameterName": "_upload_metadata",
      //
      // Gets or sets a value indicating whether the default upload metadata context key should be used.
      //
      "UseDefaultUploadMetadataContextKey": false,
      //
      // Name of the default upload metadata context key. This key is used to pass the metadata to the upload handler. The metadata is passed as a JSON object.
      //
      "DefaultUploadMetadataContextKey": "request.upload_metadata",
      //
      // Upload handlers specific settings.
      //
      "UploadHandlers": {
        //
        // Enables upload handlers for the NpgsqlRest endpoints that uses PostgreSQL Large Objects API
        //
        "LargeObjectEnabled": true,
        // csv string containing mime type patters, set to null to ignore
        "LargeObjectIncludedMimeTypePatterns": null,
        // csv string containing mime type patters, set to null to ignore
        "LargeObjectExcludedMimeTypePatterns": null,
        "LargeObjectKey": "large_object",
        "LargeObjectHandlerBufferSize": 8192,

        //
        // Enables upload handlers for the NpgsqlRest endpoints that uses file system
        //
        "FileSystemEnabled": true,
        // csv string containing mime type patters, set to null to ignore
        "FileSystemIncludedMimeTypePatterns": null,
        // csv string containing mime type patters, set to null to ignore
        "FileSystemExcludedMimeTypePatterns": null,
        "FileSystemKey": "file_system",
        "FileSystemHandlerPath": "/tmp/uploads",
        "FileSystemHandlerUseUniqueFileName": true,
        "FileSystemHandlerCreatePathIfNotExists": true,
        "FileSystemHandlerBufferSize": 8192,

        //
        // Enables upload handlers for the NpgsqlRest endpoints that uploads CSV files to a row command
        //
        "CsvUploadEnabled": true,
        // csv string containing mime type patters, set to null to ignore
        "CsvUploadIncludedMimeTypePatterns": null,
        // csv string containing mime type patters, set to null to ignore
        "CsvUploadExcludedMimeTypePatterns": null,
        "CsvUploadCheckFileStatus": true,
        "CsvUploadTestBufferSize": 4096,
        "CsvUploadNonPrintableThreshold": 5,
        "CsvUploadDelimiterChars": ",",
        "CsvUploadHasFieldsEnclosedInQuotes": true,
        "CsvUploadSetWhiteSpaceToNull": true,
        //
        // $1 - row index (1-based), $2 - parsed value text array, $3 - result of previous row command, $4 - json metadata for upload
        //
        "CsvUploadRowCommand": "call process_csv_row($1,$2,$3,$4)"
      }
    }

    // ...
}
```

### NpgsqlRest Client App Static File Parser

This is basically conceptual fix to the Client application static file parser. Files being parsed that are cached are defining the purpose so cache needs to be disabled. This is done with new static file configuration setting in the Client application:

```jsonc
{
    //...
    "StaticFiles": {
        //...
        "ParseContentOptions": {
            //...
            "Headers": [ "Cache-Control: no-store, no-cache, must-revalidate", "Pragma: no-cache", "Expires: 0" ],
        }
    }
    //...
}
```

### Comment Annotation System Changes

Comment annotation has seen some big changes. Added new parameters and annotations to reflect changes in Authentication and Upload options.

Breaking change: Comment annotations now support only snake_case naming. This is consistent with PostgreSQL naming convention and it reduces the number of annotations needed to support. 

### Fixes and Other Improvements

- Fixed a bug when endpoint was returning empty JSON array. Adequate parser wasn't invoked in case of JSON empty array and it returned `{}`. This is fixed to return `[]`.
- All commands and parameters are now using `MemberwiseClone` performance trick to reduce unnecessary allocations.

## Version [2.26.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.26.0) (2025-05-11)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.25.0...2.26.0)

### Core NpgsqlRest Library

#### Upload Handlers Improvements

##### CSV Upload Handler

New CSV Upload Handler with key `csv` is implemented and added to available handlers (Large Object and File System). It has following parameters:

- `included_mime_types`: List of allowed MIME type patterns. Pattern may include `*` or `?`. If empty or null, all MIME types are allowed. Default is null, this parameter is not used.
- `excluded_mime_types`: List of disallowed MIME type patterns. Pattern may include `*` or `?`. Default is null, this parameter is not used.
- `check_file`: Boolean flag indicating whether to perform content verification. Content verification looks for binary file markers in test buffer and does it contain new line delimiters. Default is true.
- `test_buffer_size`: Size of the buffer to read for content verification. Default is 8192 bytes.
- `non_printable_threshold`: Maximum number of non-printable characters allowed in the test buffer. Everything above is considered likely binary content. Default is 5.
- `delimiters`: Text where each character is considered as valid value delimiter. Default is comma character (`,`). Note: use standard escape sequence special characters like `\t` for tab character.
- `has_fields_enclosed_in_quotes`: Boolean flag indicating whether CSV fields might be enclosed in quotes. Default is true.
- `set_white_space_to_null`: Boolean flag indicating whether whitespace-only values should be converted to NULL when calling the row command. Default is true.
- `row_command`: The SQL command to execute for each CSV row. Required when parsing CSV content. Default is `call process_csv_row($1,$2,$3,$4)`

Row command is executed under transaction for every CSV row. It accepts 4 optional and positional parameters:
1) Parameter `$1` is always row number, type `integer`, starting from 1. If the CSV contains a header row, it will always be row number 1.
2) Parameter `$2` is always text array (type `text[]`) that contains entire row being processed.
3) Parameter `$3` is always the single result from the execution of the previous row command. Type is whatever type the row command returns. If the row command doesn't return anything, this parameter will always be NULL. If the row command does return value, this parameter will always be NULL in the first row.
4) Parameter `$4` is the JSON object containing metadata for this upload containing following fields:
- `type`: contains upload type key, default is `csv`
- `fileName`: upload file name
- `contentType`: upload mime type
- `size`: upload size in bytes
- `status`: result of content verification. It can be `Ok`, `Empty`, `ProbablyBinary` or `NoNewLines`, but in the row command context it is always `Ok` (even when verification is not used).

Same metadata JSON is sent to the main endpoint command parameter marked as upload metadata, with some differences:
1) Metadata is array of objects.
2) Field `status` can be any allowed value (`Ok`, `Empty`, `ProbablyBinary` or `NoNewLines`).
3) Contains one extra field: `lastResult`. This field will contain value of the last row command (if any).

Example:

```sql
-- table for uploads
create table csv_example_upload_table (
    index int,
    id int,
    name text,
    value int
);

-- row command
create procedure csv_example_upload_table_row(
    _index int,
    _row text[]
)
language plpgsql
as 
$$
begin
    insert into csv_example_upload_table (
        index,
        id, 
        name, 
        value
    ) 
    values (
        _index,
        _row[1]::int,
        _row[2],
        _row[3]::int
    );
end;
$$;

-- HTTP POST endpoint
create function csv_example_upload(
    _meta json = null
)
returns json 
language plpgsql
as 
$$
begin
    -- do something with metadata or raise exception to rollback this upload
    return _meta;
end;
$$;

comment on function csv_example_upload(json) is '
HTTP POST
upload for csv
param _meta is upload metadata
delimiters = ,;
row_command = call csv_example_upload_table_row($1,$2)
';
```

And now we can upload CSV using this upload endpoint:

```csharp
var fileName = "example-csv-upload.csv";
var sb = new StringBuilder();
sb.AppendLine("11,XXX,333");
sb.AppendLine("12;YYY;666");
sb.AppendLine("13;;999");
sb.AppendLine("14,,,");
var csvContent = sb.ToString();
var contentBytes = Encoding.UTF8.GetBytes(csvContent);
using var formData = new MultipartFormDataContent();
using var byteContent = new ByteArrayContent(contentBytes);
byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
formData.Add(byteContent, "file", fileName);

using var result = await test.Client.PostAsync("/api/csv-example-upload/", formData);
result.StatusCode.Should().Be(HttpStatusCode.OK);
```

This CSV upload uses two delimiters: `,` and `;`. Resulting table `csv_example_upload_table` after uploading content from this example will look like this:

| index | id | name | value |
| ----- | -- | ---- | ---- |
| 1 | 11 | XXX | 333 |
| 2 | 12 | YYY | 666 |
| 3 | 13 | NULL | 999 |
| 4 | 14 | NULL | NULL |

See the Test projects for more examples.

##### New Upload Parameters

All upload handlers now support additional common parameters:

- `included_mime_types`: List of allowed MIME type patterns. Pattern may include `*` or `?`. If empty or null, all MIME types are allowed. Default is null, this parameter is not used.
- `excluded_mime_types`: List of disallowed MIME type patterns. Pattern may include `*` or `?`. Default is null, this parameter is not used.

#### New Custom Parameters

New custom parameters were added to support existing functionalities. These values can be set with old comment annotation system. These are:

- Buffer rows parameters. Accept number.
`bufferrows`
`buffer_rows`
`buffer-rows`
`buffer`

- Raw parameters. Accept boolean.
`raw`
`rawmode`
`raw_mode`
`raw-mode`
`rawresults`
`raw_results`
`raw-results`

- Separator parameters. Accept text.
`separator`
`rawseparator`
`raw_separator`
`raw-separator`

- New line parameters. Accept text.
`newline`
`new_line`
`new-line`
`rawnewline`
`raw_new_line`
`raw-new-line`

- Column names parameters. Accept boolean.
`columnnames`
`column_names`
`column-names`

- Connection name parameters. Accept text.
`connection`
`connectionname`
`connection_name`
`connection-name`

#### Improved Error Handling

Added two more entries to `PostgreSqlErrorCodeToHttpStatusCodeMapping` options:

```csharp
public Dictionary<string, int> PostgreSqlErrorCodeToHttpStatusCodeMapping { get; set; } = new()
{
    { "57014", 205 }, //query_canceled -> 205 Reset Content
    { "P0001", 400 }, // raise_exception -> 400 Bad Request
    { "P0004", 400 }, // assert_failure -> 400 Bad Request
};
```

This configuration is now returning 400 Bad Request response on:
- Custom Pl/pgSQL exceptions (raise exception)
- Custom Pl/pgSQL asserts (assert failure)

Also, if endpoint is returning 400 Bad Request due the exception (raise exception, assert failure), the response string will contain a full message (exception message or assertion message) without trailing error code.

#### Fixes

- Fixed bug with wrong parameter default value detection when using stored procedures.
- Fixes on upload handlers: 
  - improved upload rollback and cleanup on error
  - fixed handling of multiple uploads
  - fixed upload logging
  - fixed issue with metadata parameter when all parameters using default value

### NpgsqlRest Client App

#### Fixes

- Fixed `CacheParsedFile` option in `StaticFiles.ParseContentOptions` client configuration. Previously, it was always set to true, even if set to false in the configuration.

---

## Version [2.25.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.25.0) (2025-05-06)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.24.0...2.25.0)

### Core NpgsqlRest Library

#### Support for Custom Parameters

Custom parameters support for created endpoint has been added.

For now, only upload handlers are using custom parameters:

- `large_object` upload handler have following parameters:

1) `oid`: set the custom `oid` number to be used. By default, new `oid` is assigned on each upload.
2) `buffer_size`: set the custom buffer size. Default is 8192 or 8K buffer size

- `file_system` upload handler ave following parameters:

1) `path`: set the custom path for upload. Default is current path `./`
2) `file`: set the custom file name for upload. Default is whatever name is received in request.
3) `unique_name`: boolean field that, if true, will automatically set file name to be unique (name is GUID and extension is the same). Can only have true or false values (case insensitive). Default is true.
4) `create_path`: boolean field that, if true, will check if the path exists, and create it if it doesn't. Can only have true or false values (case insensitive). Default is false.
5) `buffer_size`: set the custom buffer size. Default is 8192 or 8K buffer size.

- Custom parameters can be set programmatically in `CustomParameters` dictionary for each endpoint:

```csharp
app.UseNpgsqlRest(new(connectionString)
{
    EndpointCreated = endpoint =>
    {
        if (endpoint?.Routine.Name == "upload")
        {
            endpoint.CustomParameters = new() { ["unique_name"] = "false"};
        }
    }
});
```

- Custom parameters can be set by using comment annotations.
- Each comment annotations line that:
  - has equal character `=`
  - first part, before equal character is word (alphanumerics, `_`, `-`)
- For example:

```sql
create function my_upload(
    _meta json = null
)
returns json 
language plpgsql
as 
$$
begin
    -- return same upload metadata
    return _meta;
end;
$$;

comment on function my_upload(json) is '
upload for file_system
param _meta is upload metadata
path = ./uploads
unique_name = false
create_path = false
';
```

- Custom parameters annotations can be set from the parameter value, using default formatter (name enclosed with `{` and `}`). Parameter name can be original or parsed. For example:

```sql
create function my_upload(
    _path text,
    _file text,
    _unique_name boolean,
    _create_path boolean,
    _meta json = null
)
returns json 
language plpgsql
as 
$$
begin
    -- return same upload metadata
    return _meta;
end;
$$;

comment on function my_upload(text, text, boolean, boolean, json) is '
upload for file_system
param _meta is upload metadata
path = {_path}
file = {_file}
unique_name = {_unique_name}
create_path = {_create_path}
';
```

- Note: parameter name can be original name or parsed name. Default name parser is camel case, so the names like `path`, `file`, `uniqueName` or `createPath` are equally valid.
- This example can be used like this: 

```csharp
// csvContent is the string content
var fileName = "test-data.csv";
var contentBytes = Encoding.UTF8.GetBytes(csvContent);
using var formData = new MultipartFormDataContent();
using var byteContent = new ByteArrayContent(contentBytes);
byteContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
formData.Add(byteContent, "file", fileName);

var query = new QueryBuilder
{
    { "path", "./test" },
    { "file", fileName },
    { "uniqueName", "false" },
    { "createPath", "true" },
};

using var result = await test.Client.PostAsync($"/api/my-upload/{query}", formData);

//
// upload is saved as ./test/test-data.csv
//
```

#### Other Improvements

- Improved parameter parsing for header values set by comment annotations
- Now using high speed and well tested template parser to parse `{name}` formats
- When using header values set by comment annotations, header name must be a single word (alphanumerics, `_`, `-`).
- When using header values set by comment annotations that should be parsed from parameter values, parameter name can also be parsed name:

```sql
create function header_template_response1(_type text, _file text) 
returns table(n numeric, d timestamp, b boolean, t text)
language sql
as 
$$
select sub.* 
from (
values 
    (123, '2024-01-01'::timestamp, true, 'some text'),
    (456, '2024-12-31'::timestamp, false, 'another text')
)
sub (n, d, b, t)
$$;

comment on function header_template_response1(text, text) is '
raw
separator ,
newline \n
Content-Type: {_type}
Content-Disposition: attachment; filename={_file}
```

This is also valid, when using default name parser:

```sql
comment on function header_template_response1(text, text) is '
raw
separator ,
newline \n
Content-Type: {type}
Content-Disposition: attachment; filename={file}
```

### NpgsqlRest Client App

#### Added Data Protection

- Data protection mechanism is included from this version.
- Data protection helps securely store encryption keys for encrypted cookies and antiforgery tokens.
- When data protection is not enabled, and application is using authentication cookies, user will be invalidated (signed out) every time application restarts (redeployment scenario).
- Three modes are supported:
1) `Default`: Windows only.
2) `FileSystem`: Requires setting path in `FileSystemPath` for storing keys. Note: when using Docker, this path must be volume path to persist after restarts.
3) `Database`: Store keys in database require setting `GetAllElementsCommand` (expected to return rows with a single column of type text and have no parameters) and `StoreElementCommand` (receives two parameters: name and data of type text. Doesn't return anything).

- New configuration section:

```jsonc
{
  //
  // Data protection settings. Encryption keys for Auth Cookies and Antiforgery tokens.
  //
  "DataProtection": {
    "Enabled": true,
    //
    // Set to null to use the current "ApplicationName"
    //
    "CustomApplicationName": null,
    //
    // Sets the default lifetime in days of keys created by the data protection system.
    //
    "DefaultKeyLifetimeDays": 90,
    //
    // Data protection location: "Default", "FileSystem" or "Database"
    //
    // Note: When running on Linux, using Default keys will not be persisted. 
    // When keys are lost on restarts, Cookie Auth and Antiforgery tokens will also not work on restart.
    // Linux users should use FileSystem or Database storage.
    //
    "Storage": "Default",
    //
    // FileSystem storage path. Set to a valid path when using FileSystem.
    // Note: When running in Docker environment, the path must be a Docker volume path.
    //
    "FileSystemPath": "./data-protection-keys",
    //
    // GetAllElements database command. Expected to rows with a single column of type text.
    //
    "GetAllElementsCommand": "select get_data_protection_keys()",
    //
    // StoreElement database command. Receives two parameters: name and data of type text. Doesn't return anything.
    //
    "StoreElementCommand": "call store_data_protection_keys($1,$2)"
  }
}
```

#### Other Improvements

- Fixed `Auth.CookieValidDays` bug which prevented setting validation different than 90 days.

---

## Version [2.24.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.24.0) (2025-04-29)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.23.0...2.24.0)

### Core NpgsqlRest Library

- Added support for callback command when password verification fails on login.

### NpgsqlRest Client App

- Added support for callback command when password verification fails on login.
- Fixed data parameter for external login to send actual retrieved data.
- Fixed Docker image to have valid certificate.

---

## Version [2.23.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.23.0) (2025-04-28)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.22.0...2.23.0)

### TsClient Plugin Fixes

- Fixed TsClient plugin to handle booleans correctly.
- Added JsCode style comments for parameters and return values in TsClient plugin.
- Added upload support for TS and JS client.
- Added support for XsrfTokenHeaderName if used. This is used by the Upload endpoints.
- Smaller fixes in the TsClient plugin to handle some edge cases.

### Core NpgsqlRest Library

#### Simplified EndpointCreated Option Event

- From now on, this event doesn't require returning an endpoint, since it receives the endpoint instance as the parameter:

```csharp
/// <summary>
/// Callback function that is executed just after the new endpoint is created. Set the RoutineEndpoint to null to disable endpoint.
/// </summary>
public Action<RoutineEndpoint?>? EndpointCreated { get; set; } = null;
```

To change endpoint properties, simply change them on the parameter instance directly or set to null to disable if necessary:

```csharp
app.UseNpgsqlRest(new(connectionString)
{
    EndpointCreated = endpoint => 
    {
        if (endpoint?.Routine.Name == "restricted")
        {
            // disable the endpoint
            endpoint = null;
        }
    }
});
```

#### Added PATH to Comment Annotation Parser 

- New comment annotation: `PATH path`.
- Ability to set just HTTP path without method by using `PATH /my-path/`
- If HTTP tag has only two params and second param is not valid VERB (GET, POST, etc) - it is treated as the path.

#### Added SecuritySensitive Routine Option

- New Endpoint Option and Comment Annotation:
    `securitysensitive`
    `sensitive`
    `security`
    `security_sensitive`
    `security-sensitive`

- This will manually obfuscate all parameter values before sending them to log.

#### Hashing Capabilities

- This is a completely new feature. There is a default hasher class that can be injected into the system with the new option called `PasswordHasher`.

```csharp
/// <summary>
/// Default password hasher object. Inject custom password hasher object to add default password hasher.
/// </summary>
public IPasswordHasher PasswordHasher { get; set; } = new PasswordHasher();
```

- Default implementation in `PasswordHasher` class is using PBKDF2 (Password-Based Key Derivation Function 2) with SHA-256 and it incorporates 128-bit salt with 600,000 iterations (OWASP-recommended as of 2025). So it's secure, but there is an option to inject a different one.

##### Mark Parameter as Hash

- Ability to mark the parameter as hash and the value will be pre-hashed with the default hasher.

- New comment annotations: 
    - `param param_name1 is hash of param_name2` - the first parameter `param_name1` will have the hashed value of `param_name2`.
    - `param param_name is hash of param_name` - this parameter `param_name` will have the original value hashed.
    - Typical usage: `param _hashed_password is hash of _password` or just `param _password is hash of _password`.

- This can be set programmatically also: 

```csharp
app.UseNpgsqlRest(new(connectionString)
{
    EndpointCreated = endpoint => 
    {
        if (endpoint?.Routine.Name == "login")
        {
            // Set the second parameter to contain the hash instead of plain text
            endpoint.Routine.Parameters[1].HashOf = endpoint.Routine.Parameters[1];
        }
    }
});
```

##### Login Endpoints Can Verify Hashes

- New ability of the Login endpoints to compare and verify returned password hashes.

- Login endpoints can now return field `hash` (configurable) and when they do, it will be verified against a parameter that contains "pass" (configurable).

- If verification fails, login will: 
  - Automatically return status 404 with no message.
  - Relevant information will be logged as a warning to default logger: `Password verification failed for attempted login: path={path} userId={userId}, username={userName}`.
  - If the option `AuthenticationOptions.PasswordVerificationFailedCommand` is defined, this command will be executed with following optional parameters:
    - 1) scheme (text)
    - 2) user name (text)
    - 3) user id (text)

- New `AuthenticationOptions` values to support this feature are these:

1) `AuthenticationOptions.HashColumnName`

    - Type: `string`
    - Default: `"hash"`

    The default column name in the data reader that will contain password hash. If this column is present, value will be verified with the default hasher against the password parameter.

2) `AuthenticationOptions.PasswordParameterNameContains`

- Type: `string`
- Default: `"pass"`

When the login endpoint tries to verify supplied hash with the password parameter, this value will be used to locate the password parameter from the parameter collection. That will be the first parameter that contains this string in parameter name (either original or translated). 

3) `AuthenticationOptions.PasswordVerificationFailedCommand`

Text command with three optional parameters that is executed when automatic hash verification fails. Executing this command gives you chance to update appswrod attempts in database for example.

Command accepts three optional and positional parameters:
1) scheme (text)
2) user name (text)
3) user id (text)

Execution will be skipped if this option is null or empty.

#### Upload Support

- There is robust and flexible UPLOAD endpoint support from this version.

- There are three new options to support this feature:

```csharp
/// <summary>
/// Default upload handler options. 
/// Set this option to null to disable upload handlers or use this to modify upload handler options.
/// </summary>
public UploadHandlerOptions DefaultUploadHandlerOptions { get; set; } = new UploadHandlerOptions();

/// <summary>
/// Upload handlers dictionary map. 
/// When the endpoint has set Upload to true, this dictionary will be used to find the upload handlers for the current request. 
/// Handler will be located by the key values from the endpoint UploadHandlers string array property if set or by the default upload handler (DefaultUploadHandler option).
/// Set this option to null to use default upload handler from the UploadHandlerOptions property.
/// Set this option to empty dictionary to disable upload handlers.
/// Set this option to a dictionary with one or more upload handlers to enable your own custom upload handlers.
/// </summary>
public Dictionary<string, Func<IUploadHandler>>? UploadHandlers { get; set; } = null;

/// <summary>
/// Default upload handler name. This value is used when the upload handlers are not specified.
/// </summary>
public string DefaultUploadHandler { get; set; } = "large_object";
```

- Endpoints also have two new properties:

```csharp
public bool Upload { get; set; } = true;
public string[]? UploadHandlers { get; set; } = null;
```

- When the endpoint has Upload set to true, the request will first try to locate appropriate handlers.

- Endpoint can specify one or more handlers with `UploadHandlers` property (keys in `UploadHandlers` dictionary).

- When endpoint `UploadHandlers` property is null, Upload handler will use the one from the `DefaultUploadHandler` option ("large_object" by default).

- Option `Dictionary<string, Func<IUploadHandler>>? UploadHandlers` is initialized from the `DefaultUploadHandlerOptions` option which has these defaults:

```csharp
public bool UploadsEnabled { get; set; } = true;
public bool LargeObjectEnabled { get; set; } = true;
public string LargeObjectKey { get; set; } = "large_object";
public int LargeObjectHandlerBufferSize { get; set; } = 8192;
public bool FileSystemEnabled { get; set; } = true;
public string FileSystemKey { get; set; } = "file_system";
public string FileSystemHandlerPath { get; set; } = "./";
public bool FileSystemHandlerUseUniqueFileName { get; set; } = true;
public bool FileSystemHandlerCreatePathIfNotExists { get; set; } = true;
public int FileSystemHandlerBufferSize { get; set; } = 8192;
```

- Each upload handler returns a string text by convention, which usually represents JSON metadata for the upload.
  
- This metadata is then assigned to a routine parameter that has `UploadMetadata` set to true.

- That routine is executed on PostgreSQL after successful upload (handlers execution).
  
- If the routine fails, upload handlers automatically perform upload cleanup.
  
- There are currently two upload handlers implemented in the library:

1) PostgreSQL Large Object Upload Handler

- Key: `large_object`
- Description: uses [PostgreSQL Large Object API](https://www.postgresql.org/docs/current/largeobjects.html) to upload content directly to database.
- Metadata example: `{"type": "large_object", "fileName": "test.txt", "fileType": "text/plain", "size": 100, "oid": 1234}`

2) File System Upload Handler

- Key: `file_system`
- Description: Uploads files to the file system
- Metadata example: `{"type": "file_system", "fileName": "test.txt", "fileType": "text/plain", "size": 100, "filePath": "/tmp/uploads/ADF3B177-D0A5-4AA0-8805-FB63F8504ED8.txt"}`

- If the endpoint has multiple upload handlers, metadata parameter is expected to be array of text or array of JSON.

- Example of programmatic usage:

```csharp
app.UseNpgsqlRest(new(connectionString)
{
    DefaultUploadHandlerOptions = new() { FileSystemHandlerPath = "./images/" },
    EndpointCreated = endpoint =>
    {
        if (endpoint?.Url.EndsWith("upload") is true)
        {
            endpoint.Upload = true;

            if (endpoint?.Url.Contains("image") is true)
            {
                endpoint.UploadHandlers = ["file_system"];
            }
            else if (endpoint?.Url.Contains("csv") is true)
            {
                endpoint.UploadHandlers = ["large_object"];
            }
        }
    }
});
```

- This example will enable upload for all URL paths that end with "upload" text. 
- If the URL path contains "image", it will upload them to file system and to configured `./images/` path. 
- If the URL path contains "csv", it will be uploaded as the PostgreSQL large object.

- There is also robust comment annotation support for this feature:
  - `upload` - mark routine as upload (uses default handlers)
  - `upload for handler_name1, handler_name2 [, ...]` mark routine as upload and set handler key to be used (e.g. `upload for file_system`).
  - `upload param_name as metadata` mark routine as upload (uses default handlers) and sets `param_name` as metadata parameter. 
  - Note: mixing these two is not (yet) possible, `upload for file_system param_name as metadata` or `upload param_name as metadata for file_system` will not work.
  - `param param_name1 is upload metadata` set `param_name1` as the upload metadata.

- Examples:

```sql
create procedure simple_upload(
    _meta json = null
)
language plpgsql
as 
$$
begin
    raise info 'upload metadata: %', _meta;
end;
$$;

comment on procedure simple_upload(json) is 'upload'; -- does not send _meta parameter
-- or --
comment on procedure simple_upload(json) is 'upload _meta as metadata' -- sends _meta parameter
-- or --
comment on procedure simple_upload(json) is '
upload
param _meta is upload metadata
'; 
-- or --
comment on procedure simple_upload(json) is '
upload for file_system
param _meta is upload metadata
';
-- or --
comment on procedure simple_upload(json) is '
upload for large_object
param _meta is upload metadata
';
```

- In case of multiple handlers, parameter has to be an array:

```sql
create procedure simple_upload(
    _meta[] json = null
)
language plpgsql
as 
$$
begin
    raise info 'upload metadata: %', _meta;
end;
$$;

-- multiple handlers
comment on procedure simple_upload(json) is '
upload for large_object, file_system
param _meta is upload metadata
';
```

#### Other Improvements

1) Fixed issue with endpoint with default parameters. When they receive non-existing parameters in same number as default parameters, the endpoint now returns 404 instead of 200 error as it should be.

2) Fixed serialization of binary data in the response. From now on, endpoints that return either:
 - single value of type bytea (binary)
 - single column of type setof bytea (binary)
 - will be written raw directly to response. 
 - This allows, for example, displaying images directly from the database.

### NpgsqlRest Client App

#### External Login Fixes and Improvements

- External login was fundamentally broken

## Version [2.18.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.18.0 (2025-02-23)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.18.0...2.17.0)

Improve `ValidateParameters` and `ValidateParametersAsync` callbacks:

From this version they will be triggered even for paramaters that exits with default value but they are not supplied. 

In this case, parameter value will be `null` and if we want default value, we can leave it as `null`. Otherwise it is possible to assign a new value.

## Version [2.17.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.17.0 (2024-01-09)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.16.1...2.17.0)

Fix refresh path metadata logic. Refreshing metdata now also runs all plugins for source generation.

## Version [2.16.1](https://github.com/vb-consulting/NpgsqlRest/tree/2.16.1 (2024-01-06)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.16.0...2.16.1)

Fixed rare issue with path resolve logic.

## Version [2.16.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.16.0 (2024-12-30)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.15.0...2.16.0)

Internal optimizations:
- Using sequential access by default for all reader operations.
- Optimized parameter parser to use only single loop.

Added `string[][]? CommentWordLines` get property to `RoutineEndpoint` data structure. This representes parsed words from a routine comment which will make easier to parse comments in plugins.

## Version [2.15.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.15.0 (2024-12-21)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.14.0...2.15.0)

This version if full rewrite of the library bringing code simplification and many perfomance optimizations.

If has one new feature that allows refreshing medata without program restart. To facilitate this functionality, there are three new options:

### RefreshEndpointEnabled

- Type: `bool`
- Default: `false`

Enables or disables refresh endpoint. When refresh endpoint is invoked, the entire metadata for NpgsqlRest endpoints is refreshed. When metadata is refreshed, endpoint returns status 200.

### RefreshMethod

- Type: `string`
- Default: `"GET"`

### RefreshPath

- Type: `string`
- Default: `"/api/npgsqlrest/refresh"`

## Version [2.14.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.14.0 (2024-11-25)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.13.1...2.14.0)

Improved connection handling:

- Added new option: `public NpgsqlDataSource? DataSource { get; set; }`

When `DataSource` is set, `ConnectectionString` is ignored. `DataSource` is then used to create new connections for each request by calling `CreateConnection()`. This should be much faster then creating a new connection with `new NpgsqlConnection(connectionString)`.

- Added new option: `public ServiceProviderObject ServiceProviderMode { get; set; } `

`ServiceProviderObject` is enum that defines how handle service provider objects when fetching connection from service provdiders:

```csharp
public enum ServiceProviderObject
{
    /// <summary>
    /// Connection is not provided in service provider. Connection is supplied either by ConnectionString or by DataSource option.
    /// </summary>
    None,
    /// <summary>
    /// NpgsqlRest attempts to get NpgsqlDataSource from service provider (assuming one is provided).
    /// </summary>
    NpgsqlDataSource,
    /// <summary>
    /// NpgsqlRest attempts to get NpgsqlConnection from service provider (assuming one is provided).
    /// </summary>
    NpgsqlConnection
}
```

Default is `ServiceProviderObject.None`.

- Removed `public bool ConnectionFromServiceProvider { get; set; }`. Replaced by `ServiceProviderMode`.


## Version [2.13.1](https://github.com/vb-consulting/NpgsqlRest/tree/2.13.1 (2024-11-23)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.13.0...2.13.1)

Upgrade Npgsql to 9.0.1

## Version [2.12.1](https://github.com/vb-consulting/NpgsqlRest/tree/2.13.0 (2024-11-17)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.12.1...2.13.0)

- Upgrade all projects and libraries to .NET 9
- Improve the performance of the middlweware by using:
  - GetAlternateLookup extensions method to get the value from the dictionary.
  - System.IO.Pipelines.PipeWriter instead of writing directly to the response stream.
  - Spans where possible.
  
Performance benchmarks show up to 50% improvement in the middleware performance. 

The [benchmark projects](https://github.com/vb-consulting/pg_function_load_tests) is being built and will be available soon. 

## Version [2.12.1](https://github.com/vb-consulting/NpgsqlRest/tree/2.12.1 (2024-11-06)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.12.0...2.12.1)

Reverted changes in reader logic (Reader Optimizations and Provider-Specific Values). Detailed perfomance load tests and examining the Npgsql sozrce does not justify these changes.

---

## Version [2.12.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.12.0) (2024-10-29)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.11.0...2.12.0)

### Login Endpoint Custom Claims Maintain the Original Field Names

This is a small but breaking change that makes the system a bit more consistent.

For example, if you have a login endpoint that returns fields that are not mapped to any known [AD Federation Services Claim Types](https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claimtypes?view=net-8.0#fields) like this:

```sql
create function auth.login(
    _username text,
    _password text
) 
returns table (
    status int,             -- indicates login status 200, 404, etc
    name_identifier text,   -- name_identifier when converted with default name converter becomes name_identifier and mapps to http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier
    name text,              -- http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name
    role text[],            -- http://schemas.microsoft.com/ws/2008/06/identity/claims/role
    company_id int             -- company_id can't be mapped to AD Federation Services Claim Types
)

--
-- ... 
--

comment on function auth.login(text, text) is '
HTTP POST
Login
';
```

So, in this example, `company_id` can't be mapped to any AD Federation Services Claim Types, and a claim with a custom claim name will be created. In the previous version, that name was parsed with name converter, and the default name converter is the camel case converter, and the newly created claim name, in this case, was `companyId`.

This is now changed to use the original, unconverted column named, and the newly created claim name, in this case, is now `company_id`.

This is important with the client configuration when mapping a custom claim to a parameter name, like this:

```jsonc
{
    //...
    "NpgsqlRest": {
        //...
        "AuthenticationOptions": {
            //...
            "CustomParameterNameToClaimMappings": {
                "_company_id": "company_id"
            }
        }
    }
}
```

### Reader Optimizations and Provider-Specific Values

From this version, all command readers are using `GetProviderSpecificValue(int ordinal)` method instead of `GetValue(int ordinal)`.

This change ensures that data is retrieved in its native PostgreSQL format, preserving any provider-specific types. This change enhances accuracy and eliminates the overhead of automatic type conversion to standard .NET types.

Also, when reading multiple rows, the reader will now take advantage of the `GetProviderSpecificValues(Object[])` method to initialize the current row into an array. Previously, the entire row was read with `GetValue(int ordinal)` one by one. This approach **improves performances when reading multiple rows by roughly 10%** and slightly increases memory consumption.

### Updated Npgsql Reference

Npgsql reference updated from 8.0.3 to 8.0.5:

- 8.0.4 list of changes: https://github.com/npgsql/npgsql/milestone/115?closed=1
- 8.0.5 list of changes: https://github.com/npgsql/npgsql/milestone/118?closed=1

Any project using NpgsqlRest library will have to upgrade Npgsql minimal version to 8.0.5.

### Custom Types Support

From this version PostgreSQL custom types can be used as:

1) Parameters (single paramtere or combined).
2) Return types (single type or combined as a set).

Example:

```sql
create type my_request as (
    id int,
    text_value text,
    flag boolean
);

create function my_service(
    request my_request
)
returns void
language plpgsql as 
$$
begin
    raise info 'id: %, text_value: %, flag: %', request.id, request.text_value, request.flag;
end;
$$;
```

Normally, to call this function calling a `row` constructor is required and then casting it back to `my_request`:
```sql
select my_service(row(1, 'test', true)::my_request);
```

NpgslRest will construnct a valid endpoint where parameters names are constructed by following rule:
- Parameter name (in this example `request`) plus `CustomTypeParameterSeparator` value (default is underscore) plus custom type field name.

This gives as these three parameters:

1) `request_id`
2) `request_text_value`
3) `request_flag`

And when translated by the default name translator (camle case), we will get these three parameters:

1) `requestId`
2) `requestTextValue`
3) `requestFlag`

So, now, we have valid REST endpoint:

```csharp
using var body = new StringContent("""
{  
    "requestId": 1,
    "requestTextValue": "test",
    "requestFlag": true
}
""", Encoding.UTF8, "application/json");

using var response = await test.Client.PostAsync("/api/my-service", body);
response?.StatusCode.Should().Be(HttpStatusCode.NoContent); // void functions by default are returning 204 NoContent
```

Custom type parameters can be mixed with normal parameters:

```sql
create function my_service(
    a text,
    request my_request,
    b text
)
returns void
/*
... rest of the function
*/
```

In this example parameters `a` and `b` will behace as before, just three new parameters from the custom types will also be present.

Custom types can now also be a return value:

```sql
create function get_my_service(
    request my_request
)
returns my_request
language sql as 
$$
select request;
$$;
```

This will produce a valid json object, where properties are custom type fields, as we see in the client code test:

```csharp
var query = new QueryBuilder
{
    { "requestId", "1" },
    { "requestTextValue", "test" },
    { "requestFlag", "true" },
};
using var response = await test.Client.GetAsync($"/api/get-my-service/{query}");
var content = await response.Content.ReadAsStringAsync();
response?.StatusCode.Should().Be(HttpStatusCode.OK);
content.Should().Be("{\"id\":1,\"textValue\":\"test\",\"flag\":true}");
```

If return a set of, instead of single array, we will get an aray as expected:

```sql
create function get_setof_my_requests(
    request my_request
)
returns setof my_request
language sql as 
$$
select request union all select request;
$$;
```
```csharp
var query = new QueryBuilder
{
    { "requestId", "1" },
    { "requestTextValue", "test" },
    { "requestFlag", "true" },
};
using var response = await test.Client.GetAsync($"/api/get-setof-my-requests/{query}");
var content = await response.Content.ReadAsStringAsync();

response?.StatusCode.Should().Be(HttpStatusCode.OK);
content.Should().Be("[{\"id\":1,\"textValue\":\"test\",\"flag\":true},{\"id\":1,\"textValue\":\"test\",\"flag\":true}]");
```

The same, completely identical result - we will get if we return a table with a single custom type:

```sql
create function get_table_of_my_requests(
    request my_request
)
returns table (
    req my_request
)
language sql as 
$$
select request union all select request;
$$;
```
```csharp
var query = new QueryBuilder
{
    { "requestId", "1" },
    { "requestTextValue", "test" },
    { "requestFlag", "true" },
};
using var response = await test.Client.GetAsync($"/api/get-table-of-my-requests/{query}");
var content = await response.Content.ReadAsStringAsync();

response?.StatusCode.Should().Be(HttpStatusCode.OK);
content.Should().Be("[{\"id\":1,\"textValue\":\"test\",\"flag\":true},{\"id\":1,\"textValue\":\"test\",\"flag\":true}]");
```

In this case, the actual table field name `req` is ignored, and a single field is replace with trhee fields from the custom type.

When returning a table type, we can mix custom types with normal fields, for example:

```sql
create function get_mixed_table_of_my_requests(
    request my_request
)
returns table (
    a text,
    req my_request,
    b text
)
language sql as 
$$
select 'a1', request, 'b1'
union all 
select 'a2', request, 'b2'
$$;
```
```csharp
var query = new QueryBuilder
{
    { "requestId", "1" },
    { "requestTextValue", "test" },
    { "requestFlag", "true" },
};
using var response = await test.Client.GetAsync($"/api/get-mixed-table-of-my-requests/{query}");
var content = await response.Content.ReadAsStringAsync();

response?.StatusCode.Should().Be(HttpStatusCode.OK);
content.Should().Be("[{\"a\":\"a1\",\"id\":1,\"textValue\":\"test\",\"flag\":true,\"b\":\"b1\"},{\"a\":\"a2\",\"id\":1,\"textValue\":\"test\",\"flag\":true,\"b\":\"b2\"}]");
```

### Table Types Support

Table types were supported before, bot now, everything that is true for custom types is also true for table types. They can be used as:

1) Parameters (single paramtere or combined).
2) Return types (single type or combined as a set).

Examples:

```sql
create table my_table (
    id int,
    text_value text,
    flag boolean
);

create function my_table_service(
    request my_table
)
returns void
language plpgsql as 
$$
begin
    raise info 'id: %, text_value: %, flag: %', request.id, request.text_value, request.flag;
end;
$$;
```
```csharp
using var body = new StringContent("""
{  
    "requestId": 1,
    "requestTextValue": "test",
    "requestFlag": true
}
""", Encoding.UTF8, "application/json");

using var response = await test.Client.PostAsync("/api/my-table-service", body);
response?.StatusCode.Should().Be(HttpStatusCode.NoContent);
```

```sql
create function get_my_table_service(
    request my_table
)
returns my_table
language sql as 
$$
select request;
$$;
```
```csharp
var query = new QueryBuilder
{
    { "requestId", "1" },
    { "requestTextValue", "test" },
    { "requestFlag", "true" },
};
using var response = await test.Client.GetAsync($"/api/get-my-table-service/{query}");
var content = await response.Content.ReadAsStringAsync();

response?.StatusCode.Should().Be(HttpStatusCode.OK);
content.Should().Be("{\"id\":1,\"textValue\":\"test\",\"flag\":true}");
```

```sql
create function get_setof_my_tables(
    request my_table
)
returns setof my_table
language sql as 
$$
select request union all select request;
$$;
```
```csharp
var query = new QueryBuilder
{
    { "requestId", "1" },
    { "requestTextValue", "test" },
    { "requestFlag", "true" },
};
using var response = await test.Client.GetAsync($"/api/get-setof-my-tables/{query}");
var content = await response.Content.ReadAsStringAsync();

response?.StatusCode.Should().Be(HttpStatusCode.OK);
content.Should().Be("[{\"id\":1,\"textValue\":\"test\",\"flag\":true},{\"id\":1,\"textValue\":\"test\",\"flag\":true}]");
```

```sql
create function get_table_of_my_tables(
    request my_table
)
returns table (
    req my_table
)
language sql as 
$$
select request union all select request;
$$;
```
```csharp
var query = new QueryBuilder
{
    { "requestId", "1" },
    { "requestTextValue", "test" },
    { "requestFlag", "true" },
};
using var response = await test.Client.GetAsync($"/api/get-table-of-my-tables/{query}");
var content = await response.Content.ReadAsStringAsync();

response?.StatusCode.Should().Be(HttpStatusCode.OK);
content.Should().Be("[{\"id\":1,\"textValue\":\"test\",\"flag\":true},{\"id\":1,\"textValue\":\"test\",\"flag\":true}]");
```

```sql
create function get_mixed_table_of_my_tables(
    request my_table
)
returns table (
    a text,
    req my_table,
    b text
)
language sql as 
$$
select 'a1', request, 'b1'
union all 
select 'a2', request, 'b2'
$$;
```
```csharp
var query = new QueryBuilder
{
    { "requestId", "1" },
    { "requestTextValue", "test" },
    { "requestFlag", "true" },
};
using var response = await test.Client.GetAsync($"/api/get-mixed-table-of-my-tables/{query}");
var content = await response.Content.ReadAsStringAsync();

response?.StatusCode.Should().Be(HttpStatusCode.OK);
content.Should().Be("[{\"a\":\"a1\",\"id\":1,\"textValue\":\"test\",\"flag\":true,\"b\":\"b1\"},{\"a\":\"a2\",\"id\":1,\"textValue\":\"test\",\"flag\":true,\"b\":\"b2\"}]");
```

### RoutineSources Options Property

The `RoutineSources` options property access was changed from internal to public. This property defines routine sources (functions and procedures and/or CRUD table source) that will be used to create REST API endpoints. Default is functions and procedures.

This change was made to facilitate easier client configuration.

### Routine Source Options

`RoutineSource` class, which is used to build REST endpoints from PostgreSQL functions and procedures have some new options. These were added to serve client applications better. These new options are:

- `CustomTypeParameterSeparator`

This values is used as a separator between parameter name and a custom type (or table type) field name. See changes in parameter creation above. Default is underscore `_`. 

- `IncludeLanguagues`

Include functions and procedures with these languagues (case insensitive). Default is null (all are included). 

- `ExcludeLanguagues`

Exclude functions and procedures with these languagues (case insensitive). Default is C and internal (array `['c', 'internal']`).

### Reference Update

Npgsql reference from 8.0.3 to 8.0.5.

---

## Version [2.11.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.11.0) (2024-09-03)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.10.0...2.11.0)

1) Default value for the `CommentsMode` is changed. For now on, default value for this option is more restrictive `OnlyWithHttpTag` instead of previously `ParseAll`.
2) New routine endpoint option `bool RawColumnNames` (default false) with the following annotation `columnnames` or `column_names` or `column-names`. If this option is set to true (in code or with comment annotation) - and if the endpoint is int the "raw" mode - the endpoint will contain a header names. If separators are applied, they will be used also.

---

## Version [2.10.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.10.0) (2024-08-06)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.9.0...2.10.0)

### New Routine Endpoint Options With Annotations

These two options will only apply when [`raw`](https://vb-consulting.github.io/npgsqlrest/annotations/#raw) is on. Also, comment annotation values will accept standard escape sequences such as `\n`, `\r` or `\t`, etc.

These are:

- `RoutineEndpoint` option `public string? RawValueSeparator { get; set; } = null;` that maps to new comment annotation `separator`

Defines a standard separator between raw values.

- `RoutineEndpoint` option `public string? RawNewLineSeparator { get; set; } = null;` that maps to new comment annotation `newline`

Defines a standard separator between raw value columns.

### Dynamic Custom Header Values

In this version, when you define custom headers, either trough:

- [`CustomRequestHeaders` option](https://vb-consulting.github.io/npgsqlrest/options/#customrequestheaders) for all requests.
- On [`EndpointsCreated` option event](https://vb-consulting.github.io/npgsqlrest/options/#endpointscreated) as `ResponseHeaders` on a specific endpoint.
- Or, as [comment annotation](https://vb-consulting.github.io/npgsqlrest/annotations/#headers) on a PostgreSQL routine.

Now, you can set header value from a routine parameter. For, example, if the header value has a name in curly brackets like this `Content-Type: {_type}`, then a `Content-Type` header will have to value of the `_type` routine parameter (if the parameter exists).

See CSV example:

### CSV Example:

- Routine:

```sql
create function header_template_response1(_type text, _file text) 
returns table(n numeric, d timestamp, b boolean, t text)
language sql
as 
$$
select sub.* 
from (
values 
    (123, '2024-01-01'::timestamp, true, 'some text'),
    (456, '2024-12-31'::timestamp, false, 'another text')
)
sub (n, d, b, t)
$$;

comment on function header_template_response1(text, text) is '
raw
separator ,
newline \n
Content-Type: {_type}
Content-Disposition: attachment; filename={_file}
';
```

Request on this enpoint with parameters:
- _type = 'text/csv'
- _file = 'test.csv'

Will produce a download response to a 'test.csv' file with content type 'test.csv'. Raw values separator will be `,` character and row values separator will be new line.

---

## Version [2.9.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.9.0) (2024-08-02)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.8.5...2.9.0)

Added `raw` endpoint options and comment annotation:

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
Content-Type: text/csv
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

---

## Version [2.8.5](https://github.com/vb-consulting/NpgsqlRest/tree/2.8.5) (2024-06-25)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.8.4...2.8.5)

Fix default parameter validation callback.

---

## Version [2.8.4](https://github.com/vb-consulting/NpgsqlRest/tree/2.8.4) (2024-06-22)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.8.3...2.8.4)

Slight improvements in logging and enabling it to work with the client application.

---

## Version [2.8.3](https://github.com/vb-consulting/NpgsqlRest/tree/2.8.3) (2024-06-11)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.8.2...2.8.3)

Fix inconsistency with sending request parameters by routine parameters.

Previously it was possible to send request parameters to parameters without default values. To use request parameters in routine parameters, that parameter has to have a default value always.

This inconsistency is actually a bug in cases when the request header parameter name wasn't provided a value.

This is fixed now.

TsClient 1.8.1:

- If all routines are skipped, don't write any files.

---

## Version [2.8.2](https://github.com/vb-consulting/NpgsqlRest/tree/2.8.2) (2024-06-09)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.8.1...2.8.2)

### Fixed bug with default parameters

Using a routine that has default parameters and supplying one of the parameters would sometimes cause mixing of the parameter order. For example, if the routine is defined like this:

```sql
create function get_two_default_params(
    _p1 text = 'abc', 
    _p2 text = 'xyz'
) 
returns text 
language sql
as 
$$
select _p1 || _p2;
$$;
```

Invoking it with only the second parameter parameter (p2) would mix p1 and p2 and wrongly assume that the first parameter is the second parameter and vice versa.

This is now fixed and the parameters are correctly assigned.

### Fixed bug with default parameters for PostgreSQL roles that are not super-users.

When using a PostgreSQL role that is not a super-user, the default parameters were not correctly assigned.

This may be a bug in PostgreSQL itself (reported), column `parameter_default` in system table `information_schema.parameters` always returns `null` for roles that are not super-users.

Workaround is implemented and tested to use the `pg_get_function_arguments` function and then to parse the default values from the function definition.

---

## Version [2.8.1](https://github.com/vb-consulting/NpgsqlRest/tree/2.8.1) (2024-05-10)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.8.0...2.8.1)

- Upgrade Npgsql from 8.0.0 to 8.0.3
- Fix null dereference of a possibly null build warning.

---

## Version [2.8.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.8.0) (2024-05-02)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.7.1...2.8.0)

- New Option: `Dictionary<string, StringValues> CustomRequestHeaders`

Custom request headers dictionary that will be added to NpgsqlRest requests. 

Note: these values are added to the request headers dictionary before they are sent as a context or parameter to the PostgreSQL routine and as such not visible to the browser debugger.

### TsClient Version 1.7.0

- Sanitaze generated TypeScript names.
- Add `SkipRoutineNames`, `SkipFunctionNames` and `SkipPaths` options.

---

## Version [2.7.1](https://github.com/vb-consulting/NpgsqlRest/tree/2.7.1) (2024-04-30)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.7.0...2.7.1)

- Small fix on the Login endpoint that fixed the problem with the custom message not being written to the response in some rare circumstances.
- Redesigned the auth module and changed the access modifiers to the public of the ClaimTypes Dictionary to be used with the client application.

---

## Version [2.7.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.7.0) (2024-04-17)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.6.1...2.7.0)

New callback option: `Action<NpgsqlConnection, Routine, RoutineEndpoint, HttpContext>? BeforeConnectionOpen`.

This is used to set the application name parameter (for example) without having to use the service provider. It executes before the new connection is open for the request. For example:

```csharp
app.UseNpgsqlRest(new()
{
    ConnectionString = connectionString,
    BeforeConnectionOpen = (NpgsqlConnection connection, Routine routine, RoutineEndpoint endpoint, HttpContext context) =>
    {
        var username = context.User.Identity?.Name;
        connection.ConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = string.Concat(
                    "{\"app\":\"",
                    builder.Environment.ApplicationName,
                    username is null ? "\",\"user\":null}" : string.Concat("\",\"user\":\"", username, "\"}"))
        }.ConnectionString;
    }
}
```

---

## Version [2.6.1](https://github.com/vb-consulting/NpgsqlRest/tree/2.6.1) (2024-04-16)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.6.0...2.6.1)

Don't write the response body on status 205, which is forbidden.

## Version [2.6.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.6.0) (2024-04-16)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.5.0...2.6.0)

Improved error handling. Two new options are available:

### `ReturnNpgsqlExceptionMessage`:

- Set to true to return message property on exception from the `NpgsqlException` object on response body. The default is true. 

- Set to false to return empty body on exception.

### `PostgreSqlErrorCodeToHttpStatusCodeMapping`

Dictionary setting that maps the PostgreSQL Error Codes (see the [errcodes-appendix](https://www.postgresql.org/docs/current/errcodes-appendix.html) to HTTP Status Codes. 

Default is `{ "57014", 205 }` which maps PostgreSQL `query_canceled` error to HTTP `205 Reset Content`. If the mapping doesn't exist, the standard HTTP  `500 Internal Server Error` is returned.

---

## Version [2.5.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.5.0) (2024-04-15)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.4.2...2.5.0)

- New endpoint parameter option `BufferRows`.

Now it's possible to set the number of buffered rows that are returned before they are written to HTTP response on an endpoint level.

It is also possible to set this endpoint parameter as a comment annotation:

```sql
comment on function my_streaming_function() is 'HTTP GET
bufferrows 1';
```

See the full comment [annotation list here](https://github.com/vb-consulting/NpgsqlRest/blob/master/annotations.md).

Setting this parameter to 1 is useful in the HTTP streaming scenarios.

- New TsClient plugin options and fixes

```csharp
/// <summary>
/// Module name to import "baseUrl" constant, instead of defining it in a module.
/// </summary>
public string? ImportBaseUrlFrom { get; set; } = importBaseUrlFrom;

/// <summary>
/// Module name to import "pasreQuery" function, instead of defining it in a module.
/// </summary>
public string? ImportParseQueryFrom { get; set; } = importParseQueryFrom;
```

---

## Version [2.4.2](https://github.com/vb-consulting/NpgsqlRest/tree/2.4.2) (2024-04-14)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.4.1...2.4.2)

- Fix double logging the same message on the connection notice.

---

## Version [2.4.1](https://github.com/vb-consulting/NpgsqlRest/tree/2.4.1) (2024-04-12)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.4.0...2.4.1)

- Fix missing Text type for types in need of JSON escaping.

---

## Version [2.4.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.4.0) (2024-04-08)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.3.1...2.4.0)

- Remove `AotTemplate` subproject directory. AOT Template is now `NpgsqlRestTestWebApi` with full configuration for the entire application.
- The auth handler doesn't complete the response if it doesn't have to on login and logout.
- Changed wording `Schema` to `Scheme` everywhere because that appears to be standard with the auth (unlike with databases).
- Changed `IRoutineSource` interface to:
  -  Have `CommentsMode` mode fully exposed with getters and setters.
  -  Have `Query` property exposed.
  -  Every parameter for the query exposed (`SchemaSimilarTo`, `SchemaNotSimilarTo`, etc).
- Replaced interfaces with concrete implementation for better performance.
- Fixed bug with service scope disposal when using `ConnectionFromServiceProvider`.
- Obfuscated auth parameters in logs (with `ObfuscateAuthParameterLogValues` option).
- Implemented `SerializeAuthEndpointsResponse` to serialize the auth (log in or log out) when it's possible.
- Fixed `ParameterValidationValues` to use `NpgsqlRestParameter` instead of `NpgsqlParameter` to expose the actual parameter name.
- Added `IsAuth` read-only property to the endpoint.
- Fixed automatic port detection in the code-gen plugins.
- Added `CommentHeaderIncludeComments` to the `TsClient` plugin.

---

## Version [2.3.1](https://github.com/vb-consulting/NpgsqlRest/tree/2.3.1) (2024-04-05)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.3.0...2.3.1)

* Fix the "Headers are read-only, response has already started." error during the logout execution.

---

## Version [2.3.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.3.0) (2024-04-04)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.2.0...2.3.0)

- Login Endpoints can return text messages.
- A new option that supports this feature: `AuthenticationOptions.MessageColumnName`.
- Login endpoints always return text.
- Interface `IRoutineSource` exposes `string Query { get; set; }`. If the value doesn't contain blanks it is interpreted as the function name.
- TsClient plugin new version  (1.2.0):
  - New TsClweint option BySchema. If true, create a file by schema. The default is false.
  - Fix handling login endpoints.
  - Bugfixes.

---

## Version [2.2.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.2.0) (2024-04-02)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.1.0...2.2.0)

- Login endpoints
- Logout endpoints
- Small name refactoring (ReturnRecordNames -> ColumnNames)

To enable authentication, the authentication service first needs to be enabled in the application:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication().AddCookie();
```

The login-enabled endpoints must return a named record.

The program will result in the `ArgumentException` exception if the login-enabled routine is either of these:
- void
- returns simple value
- returns set of record unnamed records

The login operation will be interpreted as an unsuccessful login attempt and return the status code `401 Unauthorized` without creating a user identity if either:
- The routine returns an empty record.
- The returned record includes a [status column](https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsschemacolumnname), and the value of the status column name is:
  - False for boolean types.
  - Not 200 for numeric types.

The login operation will be interpreted as a successful login attempt and return the status code `200 OK` with creating a new user identity if either:
- The routine returns a record without status [status column](https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsschemacolumnname).
- The returned record includes a [status column](https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsschemacolumnname), and the value of the status column name is:
  - True for boolean types.
  - 200 for numeric types.

To authorize a different authorization scheme, return a [schema column name](https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsschemacolumnname) with the value of that schema.

Any other records will be set as new claims for the created user identity on successful login, where:
- The column name is claim type. This type will by default try to match the constant name in the [ClaimTypes class](https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claimtypes?view=net-8.0) to retrieve the value. Use the [`UseActiveDirectoryFederationServicesClaimTypes` Option][https://vb-consulting.github.io/npgsqlrest/options/#authenticationoptionsuseactivedirectoryfederationservicesclaimtypes] to control this behavior.
- The record value (as string) is the claim value.

---

## Version [2.1.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.1.0) (2024-03-29)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/2.0.0...2.1.0)

### Role-Based Security

This version supports the **Roles-Based Security** mechanism.

The Endpoint can be authorized for only certain roles. 

For example, all endpoints must be in `admin` or `superadmin` roles:

```csharp
app.UseNpgsqlRest(new()
{
    ConnectionString = connectionString,
    EndpointCreated = (routine, endpoint) => endpoint with { AuthorizeRoles = ["admin", "super"] }
});
```

Same thing, only for the function with name `protected_func`:

```csharp
app.UseNpgsqlRest(new()
{
    ConnectionString = connectionString,
    EndpointCreated = (routine, endpoint) => routine.Name == "protected_func" ? endpoint with { AuthorizeRoles = ["admin", "super"] } : endpoint
});
```

There is also support for the comment annotations. Add a list of roles after the `RequiresAuthorization` annotation tag:

```sql
comment on function protected_func() is 'authorize admin, superadmin';
```

See more details on the [`RequiresAuthorization` annotation tag](https://vb-consulting.github.io/npgsqlrest/annotations/#requiresauthorization).

Note: If the user is authorized but not in any of the roles required by the authorization, the endpoint will return the status code `403 Forbidden`.

### TsClient IncludeStatusCode

The New version of the `NpgsqlRest.TsClient` (1.1.0) plugin now includes the `IncludeStatusCode` option.

When set to true (default is false), the resulting value will include the response code in the function result, for example:

```ts
export async function getDuplicateEmailCustomers() : Promise<{status: number, response: IGetDuplicateEmailCustomersResponse[]}> {
    const response = await fetch(_baseUrl + "/api/get-duplicate-email-customers", {
        method: "GET",
        headers: { "Content-Type": "application/json" },
    });
    return {status: response.status, response: await response.json() as IGetDuplicateEmailCustomersResponse[]};
}
```

---

## Version [2.0.0](https://github.com/vb-consulting/NpgsqlRest/tree/2.0.0) (2024-03-10)

Version 2.0.0 is the major redesign of the entire library. This version adds extendibility by introducing the **concept of plugins.**

There are two types of plugins:

### 1) Routine Source Plugins

The concept of a routine in NpgsqlRest refers to an action that is executed on the PostgreSQL server when an API endpoint is called. This can include PostgreSQL functions, procedures, custom queries, or commands.

In previous versions of the library, only PostgreSQL functions and procedures were considered routine sources. The REST API was built on the available functions and procedures in the PostgreSQL database and provided configuration.

However, in the latest version, the routine source has been abstracted, allowing for the addition of new routine sources as plugins. This provides extendibility and flexibility in building the REST API based on different sources of routines.

For example, a plugin that can build CRUD (create, read, update, delete) support for tables and views is published as an independent, standalone package: **[NpgsqlRest.CrudSource](https://vb-consulting.github.io/npgsqlrest/crudsource)**.

To add CRUD support to API generation, first, reference `NpgsqlRest.CrudSource` plugin library with:

```console
dotnet add package NpgsqlRest.CrudSource --version 1.0.0
```

And then include the `CrudSource` source into existing sources:

```csharp
using NpgsqlRest;
using NpgsqlRest.CrudSource;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseNpgsqlRest(new()
{
    ConnectionString = connectionString,
    SourcesCreated = sources => sources.Add(new CrudSource())
});

app.Run();
```

Note that the routine source for functions and procedures is already present in the basic library. It's part of the basic functionality and is not separated into a plugin package.

### 2) Code Generation Plugins

The second type is the code generator plugins, capable of generating a client code that can call those generated REST API endpoints.

In the previous version, there is support for the automatic generation of [HTTP files](https://learn.microsoft.com/en-us/aspnet/core/test/http-files?view=aspnetcore-8.0). This support is now moved to a separate plugin library **[NpgsqlRest.HttpFiles](https://vb-consulting.github.io/npgsqlrest/httpfiles/)**.

To use the HTTP files support, first, reference `NpgsqlRest.HttpFiles` plugin library with:

```console
dotnet add package NpgsqlRest.HttpFiles --version 1.0.0
```

And then include the `HttpFiles` in the list of generators in the `EndpointCreateHandlers` list option:

```csharp
using NpgsqlRest;
using NpgsqlRest.HttpFiles;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseNpgsqlRest(new()
{
    ConnectionString = connectionString,
    EndpointCreateHandlers = [new HttpFile(), /* other gnerator plugins */],
});

app.Run();
```

There is also a client code generator for Typescript that can generate a Typescript module to call the generated API: **[NpgsqlRest.TsClient](https://vb-consulting.github.io/npgsqlrest/tsclient/)**


To include Typesscipt client:

```console
dotnet add package NpgsqlRest.TsClient --version 1.0.0
```

And add `TsClient` to the list:

```csharp
using NpgsqlRest;
using NpgsqlRest.TsClient;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseNpgsqlRest(new()
{
    ConnectionString = connectionString,
    EndpointCreateHandlers = [new TsClient("../Frontend/src/api.ts")],
});

app.Run();
```

### System Design

System design can be illustrated with the following diagram:

<p style="text-align: center; width: 100%">
    <img src="/npgsqlrest-v2.png" style="width: 70%;"/>
</p>

The initial bootstrap with all available plugins looks like this:

```csharp
using NpgsqlRest;
using NpgsqlRest.CrudSource;
using NpgsqlRest.HttpFiles;
using NpgsqlRest.TsClient;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseNpgsqlRest(new()
{
    ConnectionString = connectionString,
    EndpointCreateHandlers = [new HttpFile(), new TsClient("../Frontend/src/api.ts")],
    SourcesCreated = sources => sources.Add(new CrudSource())
});

app.Run();
```

### Other Changes

Other changes include:

- Optimizations
- Bugfixes for edge cases

Full list of available options and annotations for version 2:

- **[See Options Reference](https://vb-consulting.github.io/npgsqlrest/options/)**
- **[See Comment Annotations Reference](https://vb-consulting.github.io/npgsqlrest/annotations/)**

## Version [1.6.3](https://github.com/vb-consulting/NpgsqlRest/tree/1.6.3) (2024-02-19)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/1.6.2...1.6.3)

## Uptime Improved

The query that returns metadata for existing routines is heavily optimized. There were some problematic subqueries that caused execution to run more than one second.

As a result, uptime is slashed from more than one second to milliseconds.

## Npgsql Reference Update

- Npgsql 8.0.1 -> 8.0.2

Any dependency shall require an upgrade too.

## Version [1.6.2](https://github.com/vb-consulting/NpgsqlRest/tree/1.6.2) (2024-02-03)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/1.6.1...1.6.2)

- Fixed and tested full support for PostgreSQL stored procedures (as opposed to user-defined functions which are used primarily).

-----------

## Version [1.6.1](https://github.com/vb-consulting/NpgsqlRest/tree/1.6.1) (2024-02-02)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/1.6.0...1.6.1)

### 1) Smaller Options Tweaks

The following options will be ignored under these conditions:

- `SchemaSimilarTo`: ignore if the string is empty.
- `SchemaNotSimilarTo`: ignore if the string is empty.
- `IncludeSchemas`: ignore if the array is empty.
- `ExcludeSchemas`: ignore if the array is empty.
- `NameSimilarTo`: ignore if the string is empty.
- `NameNotSimilarTo`: ignore if the string is empty.
- `IncludeNames`: ignore if the array is empty.
- `ExcludeNames`: ignore if the array is empty.

Previously, they were ignored only when they were NULL.

### 2) Logging Improvements

NpgsqlRest default logger is now created at the build stage by the default application logger factory. That means when the default ASP.NET application is configured then:

a) The Logger name is now by default the same as the default namespace of the library which is `NpgsqlRest`.

Previously, the default application logger was used and the default name was equal to your application default logger which made it harder to distinguish and configure.

It's possible to set a custom logger name with the new configuration option: `LoggerName`.
The `LoggerName` when set to null (default) will use the default logger name which is `NpgsqlRest` (the default namespace).

b) Since the logger is created with a different name, now it's possible to apply configuration from the configuration file, such as `appsettings.json` file:

```json
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "NpgsqlRest": "Warning"
    }
  }
```

This example configures the `NpgsqlRest` logger to the warning level only.

### 3) LogCommands Includes Request Info

When logging commands (`LogCommands` option set to true), request info (method and the URL) will be included in log output:

```console
info: NpgsqlRest[0]
      -- POST http://localhost:5000/api/perf-test
      -- $1 integer = 1
      -- $2 text = 'XYZ'
      -- $3 integer = 3
      -- $4 timestamp without time zone = '2024-04-04T03:03:03'
      -- $5 boolean = false
      select id1, foo1, bar1, datetime1, id2, foo2, bar2, datetime2, long_foo_bar, is_foobar from public.perf_test($1, $2, $3, $4, $5)

info: NpgsqlRest[0]
      -- GET http://localhost:5000/api/get-csv-data
      select id, name, date, status from public.get_csv_data()
```

### 3) Fix Using Identifiers

PostgreSQL allows language identifiers to be used as names with double quotes. For example, a function that uses `select`, `group`, `order`, `from`, `join` as names:

```sql
create function "select"("group" int, "order" int) 
returns table (
    "from" int,
    "join" int
)
language plpgsql
as 
$$
begin
    return query 
    select t.*
    from (
        values 
        ("group", "order")
    ) t;
end;
$$;
```

This type of function would cause errors in previous versions. This is now fixed and it works normally.


### 4) Other Changes

- `DefaultUrlBuilder` and `DefaultNameConverter` static classes made public.
- Removed a need for suppression of AOT warning in an HTTP file handler module when getting the host from the configuration.

-----------

## Version [1.6.0](https://github.com/vb-consulting/NpgsqlRest/tree/1.6.0) (2024-28-01)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/1.5.1...1.6.0)

### 1) HTTP File Improvements

- HTTP file generation will now consider if the endpoint has configured a `BodyParameterName` - the name of the parameter that is configured to be sent by the request body.

For example, if the function `body_param` has two parameters (`_i` and `_p`), and `_p` is configured to be sent by the request body, the HTTP file builder will create this callable example:

```console
POST {{host}}/api/body-param?i=1

XYZ
```

Note, that parameter `_p` is set to request body as `XYZ`.

- HTTP file endpoints with comment header will now optionally include a routine database comment. In the example above comment header is:

```console
HTTP
body-param-name _p
```

And resulting HTTP file generated callable endpoint with comment will now look like this:

```console
// function public.body_param(
//     _i integer,
//     _p text
// )
// returns text
//
// comment on function public.body_param is '
// HTTP
// body-param-name _p';
POST {{host}}/api/body-param?i=1

XYZ
```

This also applies to the full header comments.

This feature can be changed with `CommentHeaderIncludeComments` boolean option, in the `HttpFileOptions` options (default is true). Example:

```csharp
app.UseNpgsqlRest(new(connectionString)
{
    HttpFileOptions = new() 
    { 
        // turn off http file comments in header
        CommentHeaderIncludeComments = false
    }
});
```

### 2) Comment Annotation Breaking Change

Comment annotations that are not `HTTP` (for example authorize, etc) don't require `HTTP` line above. 

For example, before:

```sql
comment on function comment_authorize() is '
HTTP
Authorize';
```

And now it is enough to do:

```sql
comment on function comment_authorize() is 'Authorize';
```

Reason: it was really stupid, this is much better.

### 3) Logging Commands

There is a new option named `LogCommands` that, (well, obviously duh), enables logging command text before the execution, together with parameter values. The default is false (since it has a slight performance impact).

```csharp
app.UseNpgsqlRest(new(connectionString)
{
    LogCommands = true,
});
```

Examples:

```console
// function public.case_get_default_params(
//     _p1 integer,
//     _p2 integer DEFAULT 2,
//     _p3 integer DEFAULT 3,
//     _p4 integer DEFAULT 4
// )
// returns json
GET {{host}}/api/case-get-default-params?p1=1&p2=2&p3=3&p4=4
```

Execution produces the following log:

```console
info: NpgsqlRestTestWebApi[0]
      -- $1 integer = 1
      -- $2 integer = 2
      -- $3 integer = 3
      -- $4 integer = 4
      select public.case_get_default_params($1, $2, $3, $4)
```

And of course, complicated and complex types are also supported:

```console
info: NpgsqlRestTestWebApi[0]
      -- $1 smallint = 1
      -- $2 integer = 2
      -- $3 bigint = 3
      -- $4 numeric = 4
      -- $5 text = 'XYZ'
      -- $6 character varying = 'IJK'
      -- $7 character = 'ABC'
      -- $8 json = '{}'
      -- $9 jsonb = '{}'
      -- $10 smallint[] = {10,11,12}
      -- $11 integer[] = {11,12,13}
      -- $12 bigint[] = {12,13,14}
      -- $13 numeric[] = {13,14,15}
      -- $14 text[] = {'XYZ','IJK','ABC'}
      -- $15 character varying[] = {'IJK','ABC','XYZ'}
      -- $16 character[] = {'ABC','XYZ','IJK'}
      -- $17 json[] = {'{}','{}','{}'}
      -- $18 jsonb[] = {'{}','{}','{}'}
      select public.case_get_multi_params1($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18)
```
```console
info: NpgsqlRestTestWebApi[0]
      -- $1 real = 1.2
      -- $2 double precision = 2.3
      -- $3 jsonpath = '$.user.addresses[0].city'
      -- $4 timestamp without time zone = '2024-04-04T03:03:03'
      -- $5 timestamp with time zone = '2024-05-05T04:04:04.0000000Z'
      -- $6 date = '2024-06-06'
      -- $7 time without time zone = '06:06'
      -- $8 time with time zone = '07:07:00'
      -- $9 interval = '8 minutes 9 seconds'
      -- $10 boolean = true
      -- $11 uuid = '00000000-0000-0000-0000-000000000000'
      -- $12 bit varying = '101'
      -- $13 varbit = '010'
      -- $14 inet = '192.168.5.13'
      -- $15 macaddr = '00-B0-D0-63-C2-26'
      -- $16 bytea = '\\xDEADBEEF'
      select public.case_get_multi_params2($1, $2, $3, $4, $5, $6, $7, $8, $9::interval, $10, $11, $12::bit varying, $13::varbit, $14::inet, $15::macaddr, $16::bytea)
```

### 4) Command Callback

New feature: 
Now it's possible to define a lambda callback in options, that, if not null - will be called after every command is created and before it is executed.

This option has the following signature:

```csharp
/// <summary>
/// Command callback, if not null, will be called after every command is created and before it is executed.
/// Setting the the HttpContext response status or start writing response body will the default command execution.
/// </summary>
public Func<(Routine routine, NpgsqlCommand command, HttpContext context), Task>? CommandCallbackAsync { get; set; } = commandCallbackAsync;
```

- The input parameter is a **named tuple** `(Routine routine, NpgsqlCommand command, HttpContext context)`.
- The expected result is `Task` since it must be an async function.

If something is written into the `HttpContext` response, execution will skip the default behavior and exit immediately. This provides a chance to write a custom implementation logic.

For example, we have an ordinary function that returns some table:

```sql
create function get_csv_data() 
returns table (id int, name text, date timestamp, status boolean)
language sql as 
$$
select * from (
    values 
    (1, 'foo', '2024-01-31'::timestamp, true), 
    (2, 'bar', '2024-01-29'::timestamp, true), 
    (3, 'xyz', '2024-01-25'::timestamp, false)
) t;
$$;
```

And only for this function, we don't want JSON, we want CSV. We can do this now:

```csharp
app.UseNpgsqlRest(new(connectionString)
{
    CommandCallbackAsync = async p =>
    {
        if (string.Equals(p.routine.Name , "get_csv_data"))
        {
            p.context.Response.ContentType = "text/csv";
            await using var reader = await p.command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var line = $"{reader[0]},{reader[1]},{reader.GetDateTime(2):s},{reader.GetBoolean(3).ToString().ToLowerInvariant()}\n";
                await p.context.Response.WriteAsync(line);
            }
        }
    }
});
```

And, when we do the `GET {{host}}/api/get-csv-data`, the response will be:

```console
HTTP/1.1 200 OK
Content-Type: text/csv
Date: Sun, 28 Jan 2024 13:21:57 GMT
Server: Kestrel
Transfer-Encoding: chunked

1,foo,2024-01-31T00:00:00,true
2,bar,2024-01-29T00:00:00,true
3,xyz,2024-01-25T00:00:00,false
```

This is a bit advanced feature, but a useful one.

-----------

## Version [1.5.1](https://github.com/vb-consulting/NpgsqlRest/tree/1.5.1) (2024-27-01)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/1.5.0...1.5.1)

Small fix for JSON returning time stamps in an array. 

Previously, the JSON response didn't have T between date and time, which is required by the  ISO 8601 standard.

```
"{\"2024-01-24 00:00:00\",\"2024-01-20 00:00:00\",\"2024-01-21 00:00:00\"}"
```

Now it's fixed:

```
"{\"2024-01-24T00:00:00\",\"2024-01-20T00:00:00\",\"2024-01-21T00:00:00\"}"
```

-----------

## Version [1.5.0](https://github.com/vb-consulting/NpgsqlRest/tree/1.5.0) (2024-27-01)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/1.4.0...1.5.0)

### 1) New Feature: Strict Function Support

Functions in strict mode are functions that are declared with the `STRICT` or with `RETURNS NULL ON NULL INPUT` keyword. 

These functions are **not executed** at all if any of the parameters are NULL.

```sql
create function strict_function(_p1 int, _p2 int) 
returns text 
strict
language plpgsql
as 
$$
begin
    raise info '_p1 = %, _p2 = %', _p1, _p2;
    return 'strict';
end;
$$;

create function returns_null_on_null_input_function(_p1 int, _p2 int)
  returns text
  returns null on null input
  language plpgsql
as 
$$
begin
    raise info '_p1 = %, _p2 = %', _p1, _p2;
return 'returns null on null input';
end;
$$;
```

If any of these functions were called with NULL:

```sql
select strict_function(1,null);
select strict_function(null,1);
select strict_function(null,null);
select returns_null_on_null_input_function(1,null);
select returns_null_on_null_input_function(null,1);
select returns_null_on_null_input_function(null,null);
```

Then function will not be executed at all no info will be emitted with `raise info` since nothing is executed. 

In this version, NpgsqlRest will return `HTTP 204 NoContent` response if any of the parameters are NULL. and it will avoid calling database in the first place. 

### 2) Other Improvements

This release features a lot of internal refactoring and code cleanup.

-----------

## Version [1.4.0](https://github.com/vb-consulting/NpgsqlRest/tree/1.4.0) (2024-26-01)

[Full Changelog](https://github.com/vb-consulting/NpgsqlRest/compare/1.3.0...1.4.0)

### 1) Improved Untyped Functions

In the previous version, untyped functions were returning a JSON array where each element was an entire raw record as returned from PostgreSQL encoded into JSON string:

Following raw output: 

```console
(1,one)
(2,two)
(3,three)
```

would produce this JSON response: `["1,one","2,two","3,three"]`.

However, this response is not very usable, because clients still need to process each record which can be pretty tricky for many reasons:
For example, comma symbols are encoded into double-quoted strings: `foo,bar` -> `"foo,bar"`, which makes them very hard to split values by as CSV strings, and many other reasons.

In this version, each record is encoded into a JSON array and each element into its own JSON string. The example above now looks like (formatted):

```json
[
    ["1", "one"],
    ["2", "two"],
    ["3", "three"]
]
```

Note that are all types now encoded as strings, regardless of the actual type. This is because, as far as I know, it is impossible to figure out actual types from the untyped function with the `Npgsql` data driver.

This may create awkward situations, because, for example, the raw output from PostgreSQL for the `boolean true` value and `text 't'` value are identical `t` and will be encoded also identical `"t"`. The assumption is that clients will know what types and values should they expect and act accordingly. 

This makes the use of untyped functions a very powerful approach, because the actual result table definition, doesn't have to be explicitly defined every time:

```sql
create function get_test_records() 
returns setof record
language sql
as 
$$
select * from (values 
    (1, 'one'), 
    (2, 'two'), 
    (3, 'three')
) v;
$$;
```

### 2) Improved Logging

There are also some improvements in the logging mechanism and new logging options:

- When the endpoint attribute is set through the comment annotation, this event is now logged as an information-level log. Previously, it was hard to tell did we set the endpoint attribute or not, because, when an annotation is wrong, it is interpreted as plain comment and ignored. Now it's easy.
- We can turn off or on this feature with the `LogAnnotationSetInfo` build option. The default is true.
- There is also another new build option to control logging: `LogEndpointCreatedInfo`. Controls should we log the endpoint creation or not? The default is true.

----------

## Version [1.3.0](https://github.com/vb-consulting/NpgsqlRest/tree/1.3.0) (2024-23-01)

### 1) Support For Variadic Parameters

```sql
create function variadic_param_plus_one(variadic v int[]) 
returns int[] 
language sql
as 
$$
select array_agg(n + 1) from unnest($1) AS n;
$$;
```
```csharp
[Fact]
public async Task Test_variadic_param_plus_one()
{
    using var body = new StringContent("{\"v\": [1,2,3,4]}", Encoding.UTF8);
    using var response = await test.Client.PostAsync("/api/variadic-param-plus-one", body);
    var content = await response.Content.ReadAsStringAsync();

    response?.StatusCode.Should().Be(HttpStatusCode.OK);
    content.Should().Be("[2,3,4,5]");
}
```

### 2) Support For Table-Valued Functions

It seems that support for functions returning existing tables (set of table, or table-valued functions) was accidentally missing. This is now fixed. 

Example:

```sql
create table test_table (
    id int,
    name text not null
);

insert into test_table values (1, 'one'), (2, 'two'), (3, 'three');

create function get_test_table() 
returns setof test_table
language sql
as 
$$
select * from test_table;
$$;
```
```csharp
[Fact]
public async Task Test_get_test_table()
{
    using var response = await test.Client.GetAsync("/api/get-test-table");
    var content = await response.Content.ReadAsStringAsync();

    response?.StatusCode.Should().Be(HttpStatusCode.OK);
    response?.Content?.Headers?.ContentType?.MediaType.Should().Be("application/json");
    content.Should().Be("[{\"id\":1,\"name\":\"one\"},{\"id\":2,\"name\":\"two\"},{\"id\":3,\"name\":\"three\"}]");
}
```

### 2) Support for Untyped Functions

Untyped functions are functions that return a set of records of the unknown type. 

For example:

```sql
create function get_test_records() 
returns setof record
language sql
as 
$$
select * from (values (1, 'one'), (2, 'two'), (3, 'three')) v;
$$;
```

These cannot be called without the result column definition list:

```sql
select * from get_test_records()

-- throws error:
-- ERROR:  a column definition list is required for functions returning "record"
-- LINE 1: select * from get_test_records()

select * from get_test_records() as v(a int, b text)
-- returns result with columns a int, b text
```

There is no way of knowing which results these functions will return (as far as I know) and the only way to return a workable set is to execute it as a scalar function:

```sql
select get_test_records() 

-- returns values:
(1,one)
(2,two)
(3,three)
```

These are raw record values as returned from PostgreSQL without any parsing (for example text is not quoted). When NpgsqlRest executes this type of function it will return a JSON array with raw values from the PostgreSQL server without left and right parenthesis:

```csharp
[Fact]
public async Task Test_get_test_records()
{
    using var response = await test.Client.GetAsync("/api/get-test-records");
    var content = await response.Content.ReadAsStringAsync();

    response?.StatusCode.Should().Be(HttpStatusCode.OK);
    response?.Content?.Headers?.ContentType?.MediaType.Should().Be("application/json");
    content.Should().Be("[\"1,one\",\"2,two\",\"3,three\"]");
}
```

----------

## Version 1.2.0 (2024-22-01)

### 1) Fix AOT Warnings

A couple of issues have caused AOT warnings, although AOT worked before. This is now fixed:

#### 1) JSONify Strings

```csharp
[JsonSerializable(typeof(string))]
internal partial class NpgsqlRestSerializerContext : JsonSerializerContext { }

private static readonly JsonSerializerOptions plainTextSerializerOptions = new()
{
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    TypeInfoResolver = new NpgsqlRestSerializerContext()
};

// 
// AOT works but still emmits warnings IL2026 and IL3050
// 
JsonSerializer.Serialize(str, plainTextSerializerOptions)
```

This is fixed with suppression:

```csharp
[UnconditionalSuppressMessage("Aot", "IL2026:RequiresUnreferencedCode",
        Justification = "Serializes only string type that have AOT friendly TypeInfoResolver")]
[UnconditionalSuppressMessage("Aot", "IL3050:RequiresDynamic",
        Justification = "Serializes only string type that have AOT friendly TypeInfoResolver")]
private static string SerializeString(ref string value) => 
    JsonSerializer.Serialize(value, plainTextSerializerOptions);
```

#### 2) HTTP file endpoint

HTTP file endpoint was mapped with `MapGet`, and although it worked fine with AOT it emitted AOT warnings.

This is replaced with a middleware interceptor:

```csharp
builder.Use(async (context, next) =>
{
    if (!(string.Equals(context.Request.Method, "GET", StringComparison.OrdinalIgnoreCase) && 
        string.Equals(context.Request.Path, path, StringComparison.Ordinal)))
    {
        await next(context);
        return;
    }
    context.Response.StatusCode = 200;
    context.Response.ContentType = "text/plain";
    await context.Response.WriteAsync(content.ToString());
});
```

This resolved the warning but also removed the need for a routing component, which can make AOT builds even leaner and smaller:

```csharp
var builder = WebApplication.CreateEmptyBuilder(new ());
builder.Logging.AddConsole();
builder.WebHost.UseKestrelCore();
// not needed any more
// builder.Services.AddRoutingCore();
```

#### 3) Configuration Get Value From String

Method `GetHost` used `app.Configuration.GetValue<string>("ASPNETCORE_URLS")` configuration read which issued AOT warnings but it worked since it uses a string. This is suppressed with `[UnconditionalSuppressMessage("Aot", "IL2026:RequiresUnreferencedCode", Justification = "Configuration.GetValue only uses string type parameters")]`.

### 2) Fix Arrays Encodings In Http Files

Fixed arrays encoding in query string parameters of the generated HTTP file sample.

### 3) Changed Method Type Logic

Change in logic that determines the endpoint default method:

- Before: endpoint is GET if the routine name contains string `get`.
- Now: it's complicated.

The endpoint is now GET if the routine name either:
- Starts with `get_` (case insensitive).
- Ends with `_get` (case insensitive).
- Contains `_get_` (case insensitive).

Otherwise, the endpoint is POST if the volatility option is VOLATILE. This assumes that the routine is changing some data, and that is also how PostgREST works and therefore endpoint is POST. Otherwise, it is GET.

You can see the source code here: [/DefaultEndpoint.cs](https://github.com/vb-consulting/NpgsqlRest/blob/master/NpgsqlRest/DefaultEndpoint.cs#L65)

This change may be breaking change in some rare circumstances (all 88 tests are passing without a change), but luckily not too many people are using this library at this point.

### 4) URL Path Handling Changes

Generated URLs by the default [DefaultUrlBuilder](https://github.com/vb-consulting/NpgsqlRest/blob/master/NpgsqlRest/DefaultUrlBuilder.cs#L5) will no longer add a slash character (`/`) at the end of the line.

However, when middleware tries to match the requested path, both versions will be matched (with an ending slash and without an ending slash).

That means that function with the signature `public.`test_func()` will match successfully both of these paths:
- `/api/test-func`
- `/api/test-func/`

But only one will be generated in an HTTP file: `/api/test-func`.

### 5) Fix Sending Request Headers With Parameter

There was a bug with the endpoint option to send request headers JSON through a routine parameter. It seems that the parameter name was always the default. This is fixed and [tested](https://github.com/vb-consulting/NpgsqlRest/blob/master/NpgsqlRestTests/RequestHeadersTests.cs#L115).

### 6) Sending Request Headers Parameter Defaults

Sending request headers with a routine parameter doesn't require that parameter to have a default value defined anymore.

### 7) New Feature: Body Parameter Name

There is a new feature that allows binding an entire body content to a particular routine parameter.

This is an endpoint-level value called `BodyParameterName`, which is NULL by default. 

- When this value is NULL (default), no parameter is assigned from body content. The same rules apply as before, nothing is changed.
- When this value is not NULL then:
  - If the endpoint has `RequestParamType` set to `BodyJson` it will be changed automatically to `QueryString` and a warning log will be emitted.
  - A parameter named `BodyParameterName` (matches original and converted names) will be assigned from the entire request body content.
  - If body content is NULL (not set), the parameter value will be NULL, and it will be always assigned from a body.
  - All other parameters will be assigned from the query string values (`RequestParamType = RequestParamType.QueryString`).

Example:

```sql
create function example_function(_p1 int, _content text) 
returns void 
language plpgsql
as 
$$
begin
    -- do something with _p1, and _content parameters
    raise info 'received % from _p1 and % from content', _p1, _content;
end;
$$;
```

```csharp
//
// Configure endpoint from example_function function to bind parameter _content from request body
//
app.UseNpgsqlRest(new(connectionString)
{
    EndpointCreated = endpoint =>
    {
        if (endpoint.Routine.Name == "login")
        {
            // set the second paramater as hash of itself
            endpoint.Routine.Parameters[1].HashOf = endpoint.Routine.Parameters[1];
        }
        return endpoint;
    }
});
```

Note that this would also work if we used converted parameter name `content` (assuming we use the default converter that converts names to lower camel case).

This value is also configurable with comment annotations:

```sql
comment on function example_function(int, text) is '
HTTP
body-param-name content
';
```

In this comment annotation a keyword that comes before the parameter name could be any of these: `BodyParameterMame`, `body-parameter-name`, `body_parameter_name`, `BodyParamMame`, `body-param-name` or `body_param_name`.

----------

## Version 1.1.0 (2024-19-01)

### 1) RoutineEndpoint Type Change

`RoutineEndpoint` type changed to `readonly record struct`

This allows for the manipulation of the endpoint parameter in the `EndpointCreated` callback event. 

This is useful to force certain endpoint configurations from the code, rather than through comment annotations.

Example:

```csharp
app.UseNpgsqlRest(new(connectionString)
{
    EndpointCreated = (routine, endpoint) =>
    {
        if (routine.SecurityType == SecurityType.Definer)
        {
            // filter out routines that can run as the definer user (this is usually superuser)
            return null;
        }
        if (string.Equals(routine.Name, "get_data", StringComparison.Ordinal))
        {
            // override the default response content type to be csv and don't rquire authorization 
            return endpoint with
            { 
                RequiresAuthorization = false, 
                ResponseContentType = "text/csv"
            };
        }
        // require authorization for all endpoints and force GET method
        return endpoint with { RequiresAuthorization = true, Method = Method.GET };
    }
});
```

### 2) New Event Option EndpointsCreated

`EndpointsCreated` option event:

- If defined (not null) - will be executed after all endpoints have been created and are ready for execution. This happens during the build phase.

- Receives one immutable parameter array of routine and e+ndpioint tuples: `(Routine routine, RoutineEndpoint endpoint)[]`.

- The option has the following signature: `public Action<(Routine routine, RoutineEndpoint endpoint)[]>? EndpointsCreated { get; set; }`.

- Example:

```csharp
app.UseNpgsqlRest(new(connectionString)
{
    EndpointsCreated = (endpoints) =>
    {
        foreach (var (routine, endpoint) in endpoints)
        {
            Console.WriteLine($"{routine.Type} {routine.Schema}.{routine.Signature} is mapped to {endpoint.Method} {endpoint.Url}");
        }
    }
});
```

This is useful in situations when we want to generate and create source code files based on generated endpoints such as Typescript or C# interfaces for example. It enables further automatic code generation based on generated endpoints.

### 3) New Custom Logger Option

There is a new option called `Logger` with the following signature: `public ILogger? Logger { get; set; }`

When provided (not null), this is the logger that will be used by default for all logging. You can use this to provide a custom logging implementation:

```csharp
app.UseNpgsqlRest(new(connectionString)
{
    Logger = new EmptyLogger()
});

class EmptyLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        throw new NotImplementedException();
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // empty
    }
}
```

-----------