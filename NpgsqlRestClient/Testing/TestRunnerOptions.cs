namespace NpgsqlRestClient.Testing;

/// <summary>Options for the SQL test runner (<c>--test</c>). Bound from the top-level "TestRunner" config section.</summary>
public class TestRunnerOptions
{
    /// <summary>Glob (same engine as SqlFileSource) selecting *.test.sql files. Empty disables the runner.</summary>
    public string FilePattern { get; set; } = "";

    /// <summary>
    /// Optional filter narrowing the discovered set — the fast path for iterating on one test:
    /// <c>npgsqlrest ... --test --testrunner:filter=login</c>. Matched against each file's cwd-relative
    /// path: a value without wildcards is a substring match; with wildcards it is the same glob engine
    /// as <see cref="FilePattern"/>. Empty = run everything discovered.
    /// </summary>
    public string? Filter { get; set; } = null;

    /// <summary>
    /// Run only files carrying at least one of these tags (from a <c>-- @tag name [name ...]</c> header
    /// annotation). Empty = no tag requirement. Composes with <see cref="ExcludeTags"/> and <see cref="Filter"/>.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Skip files carrying any of these tags (e.g. exclude "slow" locally). Empty = skip nothing.</summary>
    public List<string> ExcludeTags { get; set; } = [];

    /// <summary>
    /// Endpoint-coverage summary after the run: exercised N of M testable endpoints plus the list of
    /// untested ones. Endpoint kinds the runner rejects (SSE, upload, login/logout, outbound proxy) are
    /// excluded from the ratio and reported separately. Tri-state: null (default) = report after FULL runs
    /// but stay quiet when the run is narrowed by <see cref="Filter"/>/<see cref="Tags"/> (a deliberately
    /// partial run would just nag); true = always report; false = never. A set
    /// <see cref="CoverageThreshold"/> always reports and gates.
    /// </summary>
    public bool? Coverage { get; set; } = null;

    /// <summary>
    /// Fail an otherwise-passing run (exit 2) when endpoint coverage is below this percentage (0-100), for
    /// CI gating. Setting it implies <see cref="Coverage"/>. Null = no gate.
    /// </summary>
    public int? CoverageThreshold { get; set; } = null;

    /// <summary>
    /// Name of a <c>ConnectionStrings</c> entry to run the tests against, instead of the app's main connection.
    /// In test mode it becomes the connection used for endpoint type-checking (Describe) and execution, so it
    /// can point at a dedicated test database. The database need not exist at startup — a <c>Setup</c> step
    /// (e.g. <c>create database …</c> on a maintenance connection) can create it first. Null/empty = use the
    /// app's main connection (the Phase-1 behavior).
    /// </summary>
    public string? ConnectionName { get; set; } = null;

    /// <summary>Max concurrent test files. 0 => Environment.ProcessorCount.</summary>
    public int MaxParallelism { get; set; } = 0;

    /// <summary>Stop scheduling new tests after the first failure/error (in-flight tests still finish).</summary>
    public bool FailFast { get; set; } = false;

    /// <summary>Per-test timeout. Zero or negative => no timeout.</summary>
    public TimeSpan PerTestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Optional path to also write a JUnit XML report (console is always printed).</summary>
    public string? JUnitOutput { get; set; } = null;

    /// <summary>Skip Teardown so a failed run's state can be inspected.</summary>
    public bool Keep { get; set; } = false;

    /// <summary>
    /// Detailed console REPORT: list passed assertions (✓), print the full failing SQL statement, and show
    /// captured `raise notice` output for passing tests too. This shapes the report only — for diagnostic
    /// logging of every executed query/HTTP call, raise the log channel instead (Log:MinimalLevels, see
    /// <see cref="LoggerName"/>).
    /// </summary>
    public bool DetailedReport { get; set; } = false;

    /// <summary>Zero tests discovered => exit 0 instead of 4.</summary>
    public bool AllowEmpty { get; set; } = false;

    /// <summary>
    /// Watch mode only: poll the test database's catalog for routine changes (create/replace/drop/alter
    /// of functions/procedures, COMMENT ON) and rebuild endpoints + re-run everything when it changes.
    /// Read from the shared top-level Watch:DatabasePollingInterval setting; zero disables.
    /// </summary>
    public TimeSpan DatabasePollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// SourceContext name for the runner's own log channel — discovery/parsing at Debug, each query and HTTP
    /// invocation at Verbose, captured `raise notice` by its severity. Set its level independently via
    /// Log:MinimalLevels (defaults to Information when absent). The console PASS/FAIL report is separate.
    /// </summary>
    public string LoggerName { get; set; } = "NpgsqlRestTest";

    public ResponseTempTableOptions ResponseTempTable { get; set; } = new();

    /// <summary>
    /// Named, reusable steps (name → step). Referenced by name from <see cref="Setup"/>/<see cref="Teardown"/>
    /// or from an individual test file header (<c>-- @setup Name</c> / <c>-- @teardown Name</c>).
    /// </summary>
    public Dictionary<string, TestSetupStep> Steps { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Run-once setup, before endpoint discovery, in the exact order written.</summary>
    public List<TestSetupStep> Setup { get; set; } = [];

    /// <summary>Run-once teardown, always (best-effort), in the exact order written.</summary>
    public List<TestSetupStep> Teardown { get; set; } = [];
}

/// <summary>Response temp-table configuration. Column values map 1:1 from the captured response; a null/empty column name omits that column.</summary>
public class ResponseTempTableOptions
{
    /// <summary>Temp table name when a test file has exactly one HTTP block.</summary>
    public string Name { get; set; } = "_response";

    /// <summary>Temp table name pattern when a file has 2+ HTTP blocks. <c>{n}</c> = the 1-based block ordinal.</summary>
    public string MultiNamePattern { get; set; } = "_response_{n}";

    /// <summary>
    /// Debugging aid: when set (e.g. "_responses_debug"), every captured response is ALSO mirrored into a
    /// PERMANENT table with this name — written on a separate autocommit connection, so it survives the
    /// test's rollback and the connection close, and can be examined in a query editor after the run.
    /// Recreated at the start of every run (always holds the LAST run). ONE table covers all blocks and
    /// files: each HTTP block adds one row, with the block column recording that block's response-table
    /// name (<see cref="Name"/>, a <see cref="MultiNamePattern"/> ordinal, or the `# @response` name)
    /// alongside test_file/method/path and the response columns. The temp-table semantics are unchanged.
    /// Null (default) = off. Do not enable in CI. In the fresh-test-database workflow combine with Keep,
    /// or teardown drops the database (and the mirror with it).
    /// </summary>
    public string? DebugTable { get; set; } = null;

    public ResponseColumnOptions Columns { get; set; } = new();
}

public class ResponseColumnOptions
{
    public string? Status { get; set; } = "status";
    public string? Body { get; set; } = "body";
    public string? ContentType { get; set; } = "content_type";
    public string? Headers { get; set; } = "headers";
    public string? IsSuccess { get; set; } = "is_success";
}

/// <summary>One Setup/Teardown step — exactly one of Sql / SqlFile / Command.</summary>
public class TestSetupStep
{
    /// <summary>Registry name when the step is defined under <c>Steps</c>; null for inline steps. Used in logs.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// A disabled step is simply IGNORED wherever it is referenced (Setup/Teardown, per-file
    /// <c>-- @setup</c>/<c>-- @teardown</c> annotations) — skipped with a debug log line, never an error.
    /// Lets the default configuration ship ready-made example steps that users flip on instead of typing.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Inline SQL executed as a single batch.</summary>
    public string? Sql { get; set; }

    /// <summary>Path (cwd-relative) to a .sql file executed as a single batch.</summary>
    public string? SqlFile { get; set; }

    /// <summary>Shell command (run via the OS shell). Non-zero exit aborts a Setup run.</summary>
    public string? Command { get; set; }

    /// <summary>Working directory for <see cref="Command"/> (cwd-relative); null => current directory.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// For a <see cref="Sql"/>/<see cref="SqlFile"/> step: name of the <c>ConnectionStrings</c> entry to run it
    /// on. Use this to run maintenance statements (e.g. <c>create database</c> / <c>drop database</c>) on an
    /// admin connection pointed at a maintenance database. Null = run on the test connection
    /// (<see cref="TestRunnerOptions.ConnectionName"/>, or the app's main connection). Ignored for Command steps.
    /// </summary>
    public string? ConnectionName { get; set; }

    public bool IsCommand => !string.IsNullOrWhiteSpace(Command);
}
