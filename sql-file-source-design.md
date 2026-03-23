# NpgsqlRest.SqlFileSource — Design Document

**Status:** Draft — awaiting review
**Date:** 2026-03-21

---

## 1. Summary

New plugin `NpgsqlRest.SqlFileSource` that implements `IRoutineSource` to scan a configured folder for `.sql` files and generate REST API endpoints. Follows the same plugin model as `NpgsqlRest.CrudSource`.

**v1 scope: single-statement files only.** Multi-statement batch execution (with composite JSON response) is a planned follow-up once single-statement support is stabilized. Multi-statement files are rejected at startup with a clear error.

Single-statement file types:
- **SELECT** (or `WITH ... SELECT`) → GET endpoint, return shape from `RowDescription`
- **Single mutation** (`INSERT`, `UPDATE`, `DELETE`) → auto verb from mutation type
- **`DO` block** → POST endpoint, parameterless

---

## 2. Plugin Architecture

### 2.1 Project Structure

```
plugins/NpgsqlRest.SqlFileSource/
├── NpgsqlRest.SqlFileSource.csproj   # net10.0, reference to NpgsqlRest
├── SqlFileSource.cs                  # IRoutineSource implementation (main entry)
├── SqlFileSourceOptions.cs           # Configuration POCO
├── SqlFileParser.cs                  # SQL file reading, statement splitting, annotation extraction
├── SqlFileDescriber.cs               # Wire protocol Describe (SchemaOnly)
├── SqlFileParameterFormatter.cs      # IRoutineSourceParameterFormatter for $N positional params
└── Enums.cs                          # Supporting enums
```

### 2.2 Interface Implementation

`SqlFileSource : IRoutineSource`

```
Read(serviceProvider, retryStrategy)
  ├─ Scan configured directory for *.sql files
  ├─ For each file:
  │   ├─ Parse: extract comments + split statements (single pass)
  │   ├─ Validate: single statement only (reject multi-statement with error)
  │   ├─ Detect: mutation type, DO block → auto HTTP verb
  │   ├─ Describe via wire protocol (SchemaOnly) → params + return columns
  │   ├─ Build Routine object (comments fed to DefaultCommentParser)
  │   └─ Yield (Routine, SqlFileParameterFormatter)
  └─ Done
```

### 2.3 Registration (NpgsqlRestClient)

In `App.CreateRoutineSources()`, add alongside existing sources:

```csharp
if (/* config enabled */)
{
    sources.Add(new SqlFileSource(new SqlFileSourceOptions
    {
        Directory = "/path/to/sql",
        // ...
    }));
}
```

---

## 3. SQL File Parsing

### 3.1 Annotations

**Core library annotations work automatically.** Since the SQL file source yields standard `Routine` objects, all existing comment annotations (`authorize`, `tag`, `request_param_type`, `sse`, etc.) from the `DefaultCommentParser` apply without any changes.

SQL file comments are valid PostgreSQL comments — the core annotation parser already handles `-- @key value` format.

**One new core annotation needed:** `@param` / `@parameter` to rename/retype parameters. This is added to the core `CommentParsers/` as a new handler, benefiting **all** endpoint types — functions, procedures, CRUD, and SQL files.

Unnamed parameters already work with `$1` as the HTTP name (e.g., `?$1=999`). This annotation is a convenience for better API ergonomics, not a requirement.

**Supported forms** (all equivalent, coexist with existing `@param X is hash of Y` / `@param X is upload metadata`):

```sql
-- Simplest: @param <original> <new_name> [type]
-- @param $1 from_date              -- rename only
-- @param $1 from_date date         -- rename + retype
-- @param _p1 from_date             -- rename a named parameter
-- @param _p1 from_date date        -- rename + retype a named parameter

-- "is" style: @param <original> is <new_name> [type is <type>]
-- @param $1 is from_date           -- rename only
-- @param $1 is from_date date  -- rename + retype
-- @param _p1 is from_date          -- rename a named parameter
```

**No ambiguity with existing handlers:** The existing `is hash of` and `is upload metadata` patterns require those specific keywords after `is`. The simple form (`@param $1 from_date`) has no `is` keyword at all. A parameter literally named `is` would use `@param $1 is` (single token after original name).

**Parser priority:** Check for `is hash of` / `is upload metadata` first (existing handlers), then fall through to the new rename/retype handler.

### 3.2 Comment Extraction

**All comments in the file are parsed as annotations** — not just a header block. Comments anywhere in the file (before, between, or after statements) are collected and fed to the existing `DefaultCommentParser` pipeline, exactly as if they were a `COMMENT ON FUNCTION` result.

Both comment forms:
- Line comments: `-- ...`
- Block comments: `/* ... */`

The parser extracts comment text, strips comment markers, concatenates into a single comment string, and passes to `DefaultCommentParser`. The SQL statements themselves (with comments removed) are kept separately for execution.

**Performance requirement:** The parser is a single-pass state machine — comments and statements are extracted simultaneously during one traversal of the file content. No second pass, no regex.

### 3.3 Statement Splitting

Split on `;` — but must handle:
- Strings (`'...'` including `''` escapes)
- Dollar-quoted strings (`$$...$$`, `$tag$...$tag$`)
- Block comments (`/* ... */`)
- Line comments (`-- ...`)

The statement splitter is integrated into the same single-pass state machine as comment extraction. Both operations happen in one traversal.

**Performance:** Optimized for speed — `ReadOnlySpan<char>` based, no allocations during scanning, only allocate when yielding a completed statement or comment block.

### 3.4 Mutation Detection

The single-pass parser also detects mutation commands (`INSERT`, `UPDATE`, `DELETE`) in the SQL statements (outside of strings, dollar-quotes, and comments). This drives automatic HTTP verb assignment.

**Auto HTTP verb** (when no explicit `HTTP GET/POST/PUT/DELETE` annotation):

| Detected content | HTTP verb | Rationale |
|---|---|---|
| None (SELECT only) | GET | Read-only |
| UPDATE only | POST | Modification |
| INSERT only | PUT | Creation |
| DELETE (alone or mixed) | DELETE | Most destructive wins |
| UPDATE + INSERT (no DELETE) | POST | Modification takes precedence |
| `DO` block | POST | Anonymous script, always mutating |

**Priority when multiple mutations present:** DELETE > POST (UPDATE) > PUT (INSERT).

**`DO` block detection:** The parser checks if the token before a dollar-quote opening is `DO` (case-insensitive). If yes → auto verb POST. No parameter inspection — `DO` blocks don't support `$N` parameters, and the parser doesn't look inside dollar-quotes. Wire protocol Describe will return 0 parameters, producing a parameterless endpoint.

An explicit `HTTP GET`, `HTTP POST`, etc. annotation always overrides the auto-detected verb.

### 3.5 Statement Validation

1. Single statement, no mutations → **GET** endpoint
2. Single statement with mutation → auto verb from mutation detection
3. `DO` block → **POST** endpoint, parameterless
4. Multiple statements → **reject at startup with clear error** ("multi-statement batch execution not yet supported")

### 3.6 Default Path Derivation

When no `@path` annotation is set, the endpoint path is derived from the filename (without extension) using the same `NameConverter` from `NpgsqlRestOptions` that converts routine names to URL paths. E.g., with the default converter, `reports_summary.sql` is treated identically to a function named `reports_summary`.

An explicit `@path` annotation overrides this entirely.

---

## 4. Wire Protocol Describe

### 4.1 Query Mode (single statement)

```csharp
await using var cmd = new NpgsqlCommand(sql, conn);
// Add parameter placeholders for each $N found
await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly);
var schema = await reader.GetColumnSchemaAsync();
// schema → column names + OIDs → RoutineEndpoint return columns
// cmd.Parameters → parameter OIDs
```

### 4.2 Annotation Type Override

`@param $1 from_date int` forces the parameter name and type, bypassing Describe for that parameter's type. Useful when the wire protocol infers a less specific type than intended.

---

## 5. Parameter Formatter

`SqlFileParameterFormatter : IRoutineSourceParameterFormatter`

Single statement with `$1`, `$2`, etc. The formatter's job is minimal since SQL files already contain the complete command text with `$N` placeholders. Unlike CrudSource (which builds SQL dynamically from table metadata), here the SQL is static — the formatter just passes parameters through.

---

## 6. Glob Pattern Enhancement (Core Library)

### 6.1 `Parser.IsPatternMatch` — `**` Support

Extend the existing `IsPatternMatch` in `NpgsqlRest/Parser.cs` to support recursive glob:

- `*` — matches any characters **except** `/` (path separator)
- `**` — matches any characters **including** `/` (crosses directory boundaries)
- `?` — matches exactly one character (unchanged)

This is standard glob semantics (gitignore, bash). Existing usage patterns (`*.html`, `/user/*`, `image/*`) are unaffected — they already don't need to cross `/` boundaries.

**Implementation:** In the wildcard matching loop, when encountering `*`:
1. Check if next char is also `*` → consume both, allow matching `/`
2. Single `*` → match anything except `/`

**Tests:** Extend `PatternMatcherTests.cs` with new `**` cases:
- `sql/**/*.sql` matches `sql/dir/file.sql` and `sql/a/b/c/file.sql`
- `sql/*.sql` does NOT match `sql/dir/file.sql`
- `**/*.sql` matches any `.sql` file at any depth
- `dir/**/file.sql` matches `dir/file.sql` and `dir/a/b/file.sql`
- Backward compat: all existing test cases still pass

### 6.2 Configuration (SqlFileSourceOptions)

```csharp
public enum CommentScope
{
    All,      // All comments in the file — default
    Header    // Only comments before the first statement
}

public enum ParseErrorMode
{
    Skip,     // Log error, skip the file, continue — default (production-safe)
    Throw     // Throw exception, halt startup (dev/CI)
}

public class SqlFileSourceOptions
{
    /// Glob pattern for SQL files, e.g. "sql/**/*.sql", "queries/*.sql"
    /// Supports * (any except /), ** (recursive), ? (single char)
    public string FilePattern { get; set; } = "";

    /// Which comments in the file to parse as annotations
    public CommentScope CommentScope { get; set; } = CommentScope.All;

    /// Behavior when a file fails to parse or describe
    public ParseErrorMode ErrorMode { get; set; } = ParseErrorMode.Skip;
}
```

- **`FilePattern`** — single glob replaces `Directory`, `SearchPattern`, `Recursive`. Path derivation uses the portion of the matched file path after the glob's base directory. URL prefix is controlled by core `NpgsqlRestOptions`, not per-source.
- **`CommentScope`** — `All` (default) parses every comment as potential annotations. `Header` only parses comments before the first statement, leaving inline SQL comments untouched.
- **`ErrorMode`** — `Skip` (default) logs the error and continues to the next file. `Throw` halts startup — useful in dev/CI to catch issues early.

---

## 7. TDD Test Plan

### 7.1 Test Infrastructure

- **Test fixture:** New `SqlFileSourceTestFixture` (like `PolpTestFixture`) that configures `SqlFileSource` in its pipeline
- **SQL inline:** Each test file contains its SQL as string constants — no external `.sql` files for tests
- **Pattern:** Partial `Database` class + `[Collection("SqlFileSourceFixture")]`
- **One test class per unit file**

### 7.2 Test Directory Structure

```
NpgsqlRestTests/
│
├── ParamAnnotationTests/                          # Phase 0 — core library @param rename/retype
│   ├── ParamRenameSimpleFormTests.cs               # @param $1 from_date
│   ├── ParamRenameWithTypeSimpleFormTests.cs       # @param $1 from_date date
│   ├── ParamRenameIsFormTests.cs                   # @param $1 is from_date
│   ├── ParamRenameIsFormWithTypeTests.cs           # @param $1 is from_date date
│   ├── ParamRenameNamedParamTests.cs               # @param _p1 from_date (named param, not $N)
│   ├── ParamRenameNamedParamWithTypeTests.cs       # @param _p1 from_date date
│   ├── ParamRenameNamedParamIsFormTests.cs         # @param _p1 is from_date
│   ├── ParamRenameEdgeCaseIsNameTests.cs           # @param $1 is (rename to literal "is")
│   ├── ParamRenameCoexistHashTests.cs              # @param X is hash of Y still works
│   ├── ParamRenameCoexistUploadTests.cs            # @param X is upload metadata still works
│   ├── ParamRenameMultipleParamsTests.cs           # Multiple @param lines in one comment
│   ├── ParamRenameNonExistentParamTests.cs         # @param $99 foo — param doesn't exist
│   ├── ParamRenameAllPgTypesTests.cs               # type override for int, text, date, uuid, json, etc.
│   ├── ParamRenameOnFunctionTests.cs               # Applied to a regular PG function endpoint
│   ├── ParamRenameOnCrudTests.cs                   # Applied to a CRUD endpoint
│   └── ParamRenameParameterAliasTests.cs           # @parameter (long form) works same as @param
│
├── SqlFileSourceTests/
│   ├── Setup/
│   │   └── SqlFileSourceTestFixture.cs
│   │
│   ├── ParserTests/                                # Phase 1 — single-pass parser, no DB needed
│   │   ├── LineCommentExtractionTests.cs            # -- comments extracted from SQL
│   │   ├── BlockCommentExtractionTests.cs           # /* */ comments extracted from SQL
│   │   ├── NestedBlockCommentTests.cs               # /* /* */ */ handling
│   │   ├── CommentsBetweenStatementsTests.cs        # Comments between statements are annotations
│   │   ├── CommentsAfterStatementsTests.cs          # Comments after last statement are annotations
│   │   ├── InlineCommentTests.cs                    # SELECT 1; -- inline comment
│   │   ├── CommentInStringLiteralTests.cs           # '-- not a comment' preserved in SQL
│   │   ├── CommentInDollarQuoteTests.cs             # $$-- not a comment$$ preserved in SQL
│   │   ├── StatementSplittingSingleTests.cs         # Single statement, no semicolon
│   │   ├── StatementSplittingMultipleTests.cs       # Multiple statements split on ;
│   │   ├── StatementSplittingTrailingSemiTests.cs   # Trailing ; doesn't create empty statement
│   │   ├── SemicolonInStringTests.cs                # 'contains;semicolon' not a split point
│   │   ├── SemicolonInDollarQuoteTests.cs           # $$contains;semicolon$$ not a split point
│   │   ├── SemicolonInBlockCommentTests.cs          # /* contains;semicolon */ not a split point
│   │   ├── DollarQuoteSimpleTests.cs                # $$ ... $$ preserved
│   │   ├── DollarQuoteTaggedTests.cs                # $tag$ ... $tag$ preserved
│   │   ├── DollarQuoteNestedTests.cs                # $outer$...$inner$...$inner$...$outer$
│   │   ├── StringEscapeTests.cs                     # 'it''s' — escaped single quotes
│   │   ├── EmptyFileTests.cs                        # Empty file → no statements
│   │   ├── CommentOnlyFileTests.cs                  # Only comments, no statements
│   │   ├── WhitespaceOnlyFileTests.cs               # Whitespace/newlines only
│   │   ├── PathDerivationBasicTests.cs              # reports_summary.sql → /api/sql/reports-summary
│   │   ├── PathDerivationSubdirTests.cs             # reports/summary.sql → /api/sql/reports/summary
│   │   └── PathDerivationCustomPrefixTests.cs       # Custom PathPrefix applied
│   │
│   ├── ModeDetectionTests/
│   │   ├── QueryModeDetectionTests.cs               # Single SELECT → Query mode, GET verb
│   │   ├── ScriptModeDetectionTests.cs              # @sse annotation → Script mode
│   │   ├── WithCteQueryModeTests.cs                 # WITH ... SELECT detected as Query
│   │   ├── RejectMultiStatementTests.cs              # Multiple statements → startup error
│   │   ├── SingleMutationAutoVerbTests.cs           # Single INSERT→PUT, UPDATE→POST, DELETE→DELETE
│   │   ├── MixedMutationPriorityTests.cs            # DELETE+INSERT→DELETE, UPDATE+INSERT→POST
│   │   ├── MutationVerbOverrideTests.cs             # Explicit HTTP annotation overrides auto verb
│   │   ├── MutationInStringNotDetectedTests.cs      # 'INSERT' in string literal not a mutation
│   │   ├── MutationInCommentNotDetectedTests.cs     # -- INSERT in comment not a mutation
│   │   ├── MutationInDollarQuoteNotDetectedTests.cs # $$INSERT$$ in dollar-quote not a mutation
│   │   ├── DoBlockAutoVerbTests.cs                  # DO $$ ... $$ → auto POST
│   │   └── DoBlockNoParamsTests.cs                  # DO block → parameterless endpoint
│   │
│   ├── DescribeTests/
│   │   ├── SelectDescribeTests.cs                   # SELECT: columns + param types inferred
│   │   ├── MutationDescribeTests.cs                 # INSERT/UPDATE/DELETE: param types inferred
│   │   ├── DoBlockDescribeTests.cs                  # DO block: 0 params, void return
│   │   ├── ParameterTypeOverrideTests.cs            # @param type override via annotation
│   │   ├── NoParameterTests.cs                      # SQL with no parameters
│   │   └── NullableParameterTests.cs                # Parameters with defaults (nullable)
│   │
│   ├── QueryEndpointTests/
│   │   ├── BasicQueryTests.cs                       # Simple SELECT → GET, JSON response
│   │   ├── QueryWithPathParamTests.cs               # $1 mapped to path segment
│   │   ├── QueryWithQueryStringTests.cs             # Parameters from query string
│   │   ├── QueryReturnTypesTests.cs                 # Various PG types in result columns
│   │   ├── QueryEmptyResultTests.cs                 # No rows → empty array
│   │   └── QueryWithDefaultParamTests.cs            # Parameters with default values
│   │
│   ├── MutationEndpointTests/
│   │   ├── InsertEndpointTests.cs                   # Single INSERT → PUT, returns affected/returning
│   │   ├── UpdateEndpointTests.cs                   # Single UPDATE → POST
│   │   ├── DeleteEndpointTests.cs                   # Single DELETE → DELETE
│   │   ├── DoBlockEndpointTests.cs                  # DO block → POST, parameterless
│   │   ├── MutationWithParamsTests.cs               # Mutation with $N parameters
│   │   ├── MutationAuthTests.cs                     # @authorize on mutation endpoints
│   │   └── VerbOverrideTests.cs                     # Explicit HTTP annotation overrides auto verb
│   │
│   └── IntegrationTests/
│       ├── FileDiscoveryTests.cs                    # Directory scanning, pattern matching
│       ├── SubdirectoryTests.cs                     # Recursive scanning, path inclusion
│       ├── SourceRegistrationTests.cs               # SqlFileSource in pipeline alongside other sources
│       └── ConfigurationTests.cs                    # SqlFileSourceOptions validation
```

### 7.3 Test Example Pattern

Each test file follows this structure (SQL inline, one class per file):

```csharp
// File: SqlFileSourceTests/ParsingTests/AnnotationParsingTests.cs

namespace NpgsqlRestTests.SqlFileSourceTests;

// SQL is defined inline as constants
public class AnnotationParsingTests
{
    private const string Sql = """
        -- @path /reports/summary
        -- @method GET
        -- @param $1 from_date date
        -- @tag reporting
        -- @authorize
        SELECT * FROM reports WHERE created >= $1;
        """;

    [Fact]
    public void ParsesPathAnnotation()
    {
        var result = SqlFileParser.Parse("test.sql", Sql);
        result.Path.Should().Be("/reports/summary");
    }

    [Fact]
    public void ParsesMethodAnnotation()
    {
        var result = SqlFileParser.Parse("test.sql", Sql);
        result.Method.Should().Be(Method.GET);
    }

    // ... one [Fact] per assertion
}
```

For integration tests that need HTTP:

```csharp
// File: SqlFileSourceTests/QueryEndpointTests/BasicQueryTests.cs

namespace NpgsqlRestTests.SqlFileSourceTests;

// Database partial class creates the supporting table
public static partial class Database
{
    public static void SqlFileBasicQueryTests()
    {
        script.Append("""
            create table if not exists sql_file_test_data (
                id int primary key,
                name text
            );
            insert into sql_file_test_data values (1, 'test');
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class BasicQueryTests(SqlFileSourceTestFixture test)
{
    // The SQL that would be in a .sql file — defined inline for reviewability
    // The fixture writes these to temp files at startup

    [Fact]
    public async Task SimpleSelectReturnsJsonArray()
    {
        var response = await test.Client.GetAsync("/api/sql/basic-query");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"name\":\"test\"");
    }
}
```

---

## 8. Implementation Phases

### Phase 0: Core `@param` Annotation (TDD — core library change)

New comment parser handler in `NpgsqlRest/Defaults/CommentParsers/`. This is the **only core library change** — everything else is the plugin.

1. **ParamRenameHandler** — parse all supported `@param` forms (simple, "is" style)
2. **Apply rename/retype** to `NpgsqlRestParameter` on the `RoutineEndpoint`

**Tests first:** New test directory `ParamAnnotationTests/` (16 test files) covering:
- All syntax forms (simple, "is" style, with/without type)
- Both positional `$N` and named parameters
- Coexistence with existing `is hash of` / `is upload metadata` handlers
- Edge cases: literal "is" as name, non-existent params, all PG types
- Applied to all endpoint types: functions, CRUD
- `@parameter` long-form alias

### Phase 1: Parsing (TDD — no database needed)

Pure string parsing, fully unit-testable without PostgreSQL.

1. **Annotation parser** — extract `@key value` from SQL comments
2. **Statement splitter** — split on `;` respecting strings, dollar-quotes, comments
3. **Mode detector** — classify as Query or Script based on annotations + statement analysis
4. **Path deriver** — filename → URL path

**Tests first:** All `ParserTests/` (24 test files) and `ModeDetectionTests/` (10 test files) written and red before implementation. Parser tests cover the single-pass state machine exhaustively: every comment form, every quoting style, every split edge case. Mode detection tests cover auto verb assignment, priority rules, overrides, and false positive rejection (mutations inside strings/comments/dollar-quotes).

### Phase 2: Describe + Parameter Merge (needs database)

Wire protocol integration.

1. **Describe via `SchemaOnly`** — columns + parameter OIDs for SELECT, mutations, DO blocks
2. **Annotation type override** — `@param $1 name type` bypasses Describe for that parameter

**Tests first:** All `DescribeTests/` (6 test files) written and red before implementation.

### Phase 3: Endpoint Generation (needs full pipeline)

Build `Routine` objects and plug into NpgsqlRest.

1. **SqlFileSource.Read()** — orchestrate parse → describe → yield Routine
2. **SqlFileParameterFormatter** — bind `$N` parameters
3. **Query endpoints** — GET with JSON response
4. **Mutation endpoints** — auto verb, parameter binding
5. **DO block endpoints** — POST, parameterless

**Tests first:** All `QueryEndpointTests/` (6 test files) and `MutationEndpointTests/` (7 test files) written and red before implementation.

### Phase 4: Integration + Configuration

1. **File discovery** — directory scanning, glob patterns, recursion
2. **NpgsqlRestClient integration** — config in `appsettings.json`, registration in `App.cs`
3. **Error handling** — startup validation, clear error messages

**Tests first:** All `IntegrationTests/` written and red before implementation.

---

## 9. Open Questions (for review)

1. **Path prefix configurable per-file or only globally?** Current design: global `PathPrefix` + per-file `@path` override via annotation.

2. **File encoding assumption?** UTF-8 seems safe. Worth making configurable?

3. **Should SELECT endpoints support POST with body parameters?** Current design: GET only. Could add `HTTP POST` annotation support for SELECT endpoints if needed.

4. **Connection lifecycle:** One connection for all Describe calls during startup, or one per file? Current design: one connection for the entire scan.

5. **How should `@param default=value` work?** Should it set a PostgreSQL default or just make the HTTP parameter optional with a C# default?

---

## 10. Estimated Scope

| Component | Estimated Lines | Complexity |
|---|---|---|
| **Core: ParamRenameHandler** | **~60** | **Low** |
| SqlFileParser | ~200 | Medium (single-pass state machine) |
| SqlFileDescriber | ~80 | Low (thin wrapper over SchemaOnly) |
| SqlFileSource (IRoutineSource) | ~120 | Medium (orchestration) |
| SqlFileParameterFormatter | ~40 | Low |
| SqlFileSourceOptions | ~30 | Low |
| **Plugin total** | **~470** | |
| Test files (ParamAnnotation) | 16 files | |
| Test files (SqlFileSource) | ~50 files | |
| NpgsqlRestClient integration | ~50 | Low |

---

## 11. Multi-Command SQL Files (v2)

### 11.1 Overview

Multi-statement SQL files execute as a batch via `NpgsqlBatch`. Each statement produces a result set (or void). The response is a JSON object where each key maps to one command's results. Void commands are omitted from the response.

### 11.2 Response Format

```json
{
  "command1": [{"id": 1, "name": "test"}],
  "command3": [{"count": 42}]
}
```

- Commands with result sets → JSON array of rows (same format as single-command endpoints)
- Void commands (INSERT/UPDATE/DELETE without RETURNING) → **omitted** from response
- If no commands return results → empty object `{}`

### 11.3 Command Naming

Default key pattern: `"command{0}"` where `{0}` is the 1-based index.

**Configurable via:**
1. Plugin option `CommandNamePattern` (global default)
2. Per-file annotation `-- @command_name step1` before each statement

```sql
-- @command_name validate
SELECT count(*) FROM orders WHERE id = $1;

-- @command_name process
UPDATE orders SET status = 'processing' WHERE id = $1 RETURNING id, status;

-- no annotation → uses pattern: "command3"
INSERT INTO order_log (order_id, action) VALUES ($1, 'started');
```

Response:
```json
{
  "validate": [{"count": 1}],
  "process": [{"id": 42, "status": "processing"}]
}
```

(Third command is void INSERT → omitted)

### 11.4 Parameters

All statements share the same parameter set. User sends `$1=123` once, every statement that references `$1` gets the value.

**Describe:** Each statement described individually via SchemaOnly. Parameter types collected per `$N` across all statements:
- All OIDs for same `$N` agree → use that type
- OIDs conflict → startup error with clear message (annotation `@param $1 name type` can override)
- `$N` appears in only some statements → type from the statement(s) that reference it

**Execution:** Each `NpgsqlBatchCommand` gets its own copy of bound parameter values. Parameters not referenced by a command are still bound (PostgreSQL ignores extra params in batch commands).

### 11.5 Transaction Control

No implicit transaction wrapping. The SQL file controls transactions explicitly:

```sql
BEGIN;
UPDATE accounts SET balance = balance - $1 WHERE id = $2;
UPDATE accounts SET balance = balance + $1 WHERE id = $3;
COMMIT;
```

### 11.6 Error Handling

If any command in the batch fails, the entire request fails and returns the error — same as any other NpgsqlRest error. No partial results.

### 11.7 HTTP Verb

Multi-command files use the same auto-detection as single-command: scan all statements for mutations, apply priority (DELETE > POST > PUT > GET). Explicit annotation overrides.

### 11.8 Implementation

| Component | Est. Lines | Complexity |
|---|---|---|
| Parser: remove multi-statement error | ~5 | Trivial |
| Describer: per-statement describe + param merge | ~80 | Medium |
| Parameter merger with conflict detection | ~40 | Low |
| Batch execution (NpgsqlBatch, bind per command) | ~60 | Medium |
| JSON object rendering (multi-result-set) | ~120 | High |
| Command naming (config + annotation) | ~30 | Low |
| SqlFileSourceOptions: CommandNamePattern | ~5 | Trivial |
| Tests | ~15 files | Medium |
| **Total** | **~350** | |

### 11.9 Rendering Approach

The multi-result-set rendering cannot use the existing `NpgsqlRestEndpoint` code which writes a JSON array for a single result set. Two options:

**Option A: Custom endpoint handler in the plugin.** SqlFileSource registers a custom `RequestDelegate` for multi-command endpoints that:
1. Executes the `NpgsqlBatch`
2. Uses `reader.NextResult()` to iterate command result sets
3. Writes `{key: [rows], ...}` directly to the response

**Option B: Extend core rendering.** Add a `MultiResultSet` flag to `RoutineEndpoint` that switches the rendering to JSON object mode.

**Recommendation:** Option A — keeps the complexity in the plugin, doesn't touch core rendering code. The custom handler uses the same `PipeWriter` + `StringBuilderPool` patterns for performance.

### 11.10 Column Metadata per Command

Each command in the batch may return different columns. The Describe phase captures column info per statement:

```csharp
class MultiCommandDescribeResult
{
    public List<SingleCommandDescribe> Commands { get; }
    public string[] MergedParameterTypes { get; }  // merged across all commands
}

class SingleCommandDescribe
{
    public ColumnInfo[]? Columns { get; }           // null for void commands
    public string? CommandName { get; }             // from annotation or pattern
}
```

The rendering uses per-command column metadata to write each result set correctly.
