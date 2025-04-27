# Changelog Archive

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