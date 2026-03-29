# NpgsqlRest.SqlFileSource

**Data source plug-in** for the `NpgsqlRest` library.

Generate REST API endpoints directly from `.sql` files. Place SQL files in a configured directory, and NpgsqlRest creates endpoints automatically — no PostgreSQL functions needed.

## How It Works

1. At startup, the plugin scans the directory matching the configured glob pattern (e.g., `sql/**/*.sql`)
2. Each `.sql` file is parsed: comments are extracted as annotations, SQL is split into statements
3. Each statement is analyzed via PostgreSQL's wire protocol (`SchemaOnly`) — parameter types and return columns are inferred without executing the query
4. A REST endpoint is created for each file, with the URL path derived from the filename

## Install

```console
dotnet add package NpgsqlRest.SqlFileSource --version 1.0.0
```

## Usage

Initialize `SourcesCreated` callback function that receives an initialized list of sources to add `SqlFileSource` source:

```csharp
using NpgsqlRest;
using NpgsqlRest.SqlFileSource;

var app = builder.Build();
app.UseNpgsqlRest(new()
{
    ConnectionString = connectionString,
    SourcesCreated = sources =>
    {
        sources.Add(new SqlFileSource());
    },
});
app.Run();
```

To run only `SqlFileSource`, clear all others:

```csharp
using NpgsqlRest;
using NpgsqlRest.SqlFileSource;

var app = builder.Build();
app.UseNpgsqlRest(new()
{
    ConnectionString = connectionString,
    SourcesCreated = sources =>
    {
        sources.Clear();
        sources.Add(new SqlFileSource());
    },
});
app.Run();
```

## Single-Command Files

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

**Note:** `DO` blocks do not support `$N` parameters — this is a PostgreSQL language limitation.

## Multi-Command Files

A file with multiple statements (separated by `;`) returns a JSON object. Each key corresponds to one command's result:

```sql
-- sql/process_order.sql
-- HTTP POST
-- @result1 validate
-- @result3 confirm
-- @param $1 order_id
SELECT count(*) FROM orders WHERE id = $1;
UPDATE orders SET status = 'processing' WHERE id = $1;
SELECT id, status FROM orders WHERE id = $1;
```

`POST /api/process-order` with `{"order_id": 42}` →

```json
{
  "validate": [{"count": 1}],
  "result2": 1,
  "confirm": [{"id": 42, "status": "processing"}]
}
```

**Result set rules:**

- Commands returning rows → JSON array of row objects
- Void commands (INSERT/UPDATE/DELETE without RETURNING) → rows-affected count as integer
- Default keys: `result1`, `result2`, `result3`, ... (prefix configurable via `ResultPrefix`)
- Override per-result with `@resultN name` or `@resultN is name` annotations
- All statements share the same parameters — user sends each parameter once
- Uses `NpgsqlBatch` — single database round-trip

## Parameters

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

Rename and retype: `-- @param $1 user_id integer`

**Multi-command files:** Parameter types are merged across all statements. Same `$N` with conflicting types produces a startup error — override with `@param $1 name type`.

### Virtual Parameters

Use `@define_param` to create HTTP parameters that are NOT bound to the SQL command — useful for custom parameter placeholders and claim mapping:

```sql
-- @define_param format text
-- table_format = {format}
-- @param $1 department_id
SELECT id, name, email FROM users WHERE department_id = $1;
```

`GET /api/users-report?department_id=5&format=html_table`

Default type is `text`; specify a type with `@define_param name type`.

## Comments and Annotations

All comments in the SQL file are parsed as annotations, just like `COMMENT ON FUNCTION` in PostgreSQL:

```sql
-- Line comments are annotations
/* Block comments are annotations too */
SELECT * FROM table;
-- Comments after statements also work
```

All existing NpgsqlRest annotations work: `@authorize`, `@allow_anonymous`, `@tag`, `@sse`, `@request_param_type`, `@path`, `@timeout`, `@cached`, `@raw`, `@header`, `@separator`, `@login`, `@logout`, `@encrypt`, `@decrypt`, etc.

**SQL file specific annotations:**

| Annotation | Description | Example |
|---|---|---|
| `@param $N name` | Rename positional parameter | `-- @param $1 user_id` |
| `@param $N name type` | Rename + retype parameter | `-- @param $1 user_id integer` |
| `@param $N is name` | Rename ("is" style) | `-- @param $1 is user_id` |
| `@resultN name` | Rename multi-command result key | `-- @result1 validate` |
| `@resultN is name` | Rename result key ("is" style) | `-- @result1 is validate` |
| `@define_param name [type]` | Define virtual parameter (not bound to SQL) | `-- @define_param _user_id` |

**`CommentScope` setting** controls which comments are parsed:
- `All` (default) — every comment in the file, regardless of position
- `Header` — only comments before the first SQL statement

## URL Path Derivation

The endpoint path is derived from the filename (without `.sql` extension) using the same `NameConverter` as functions. For example, with the default camelCase converter:

- `get_reports.sql` → `/api/get-reports`
- `user_profile.sql` → `/api/user-profile`

Override with the `@path` annotation: `-- @path /custom/path/{id}`

## Error Handling

| Mode | Behavior | Use Case |
|---|---|---|
| `Exit` (default) | Logs error, exits process | Fail-fast — catches SQL errors at startup |
| `Skip` | Logs error, skips file, continues | Production — tolerate partial failures |

PostgreSQL errors include compiler-like formatting:

```
SqlFileSource: /path/to/get-posts.sql:
error 42703: column u.id does not exist
  at line 3, column 12
  select u.id, u.name from users u
             ^
```

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `FilePattern` | string | `""` | Glob pattern for SQL files. Supports `*`, `**` (recursive), `?`. Empty = disabled |
| `CommentsMode` | enum | `OnlyWithHttpTag` | `OnlyWithHttpTag` = requires explicit `HTTP` annotation. `ParseAll` = every file becomes an endpoint |
| `CommentScope` | enum | `All` | `All` = parse all comments. `Header` = only before first statement |
| `ErrorMode` | enum | `Exit` | `Exit` = log error + exit process. `Skip` = log error + continue |
| `ResultPrefix` | string | `"result"` | Prefix for multi-command result keys (e.g., `result1`, `result2`) |
| `UnnamedSingleColumnSet` | bool | `true` | Single-column queries return flat arrays instead of object arrays |

### Configuration via appsettings.json

```json
"NpgsqlRest": {
  "SqlFileSource": {
    "Enabled": true,
    "FilePattern": "sql/**/*.sql",
    "CommentsMode": "ParseAll",
    "CommentScope": "All",
    "ErrorMode": "Exit",
    "ResultPrefix": "result",
    "UnnamedSingleColumnSet": true
  }
}
```

## Feature Parity

SQL file endpoints support all features available to function/procedure endpoints:

- Response caching (`cached`, `cache_expires_in`)
- Raw mode (`raw`, `raw_value_separator`, `raw_new_line_separator`, `raw_column_names`)
- Binary mode
- Encryption/decryption (`encrypt`, `decrypt`)
- Table format handlers (e.g., HTML table output)
- SSE events
- Authorization (`authorize`, `allow_anonymous`)
- Custom headers (`header`)
- Retry logic with error code mapping
- HTTP client types (composite type parameters with HTTP definitions)

## Library Dependencies

- NpgsqlRest 2.0.0

## Contributing

Contributions from the community are welcomed.
Please make a pull request with a description if you wish to contribute.

## License

This project is licensed under the terms of the MIT license.
