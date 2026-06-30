namespace NpgsqlRestClient.Testing;

/// <summary>Options for the SQL test runner (<c>--test</c>). Bound from the top-level "TestRunner" config section.</summary>
public class TestRunnerOptions
{
    /// <summary>Glob (same engine as SqlFileSource) selecting *.test.sql files. Empty disables the runner.</summary>
    public string FilePattern { get; set; } = "";

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

    /// <summary>Show captured `raise notice` output for all tests, not just failed/errored ones.</summary>
    public bool Verbose { get; set; } = false;

    /// <summary>Zero tests discovered => exit 0 instead of 4.</summary>
    public bool AllowEmpty { get; set; } = false;

    /// <summary>
    /// SourceContext name for the runner's own log channel — discovery/parsing at Debug, each query and HTTP
    /// invocation at Verbose, captured `raise notice` by its severity. Set its level independently via
    /// Log:MinimalLevels (defaults to Information when absent). The console PASS/FAIL report is separate.
    /// </summary>
    public string LoggerName { get; set; } = "NpgsqlRestTest";

    public ResponseTempTableOptions ResponseTempTable { get; set; } = new();

    /// <summary>Run-once setup, before endpoint discovery. Commands run first, then SqlFile/Sql (declared order within each group).</summary>
    public List<TestSetupStep> Setup { get; set; } = [];

    /// <summary>Run-once teardown, always (best-effort). Reverse: SqlFile/Sql first, then Commands.</summary>
    public List<TestSetupStep> Teardown { get; set; } = [];
}

/// <summary>Response temp-table configuration. Column values map 1:1 from the captured response; a null/empty column name omits that column.</summary>
public class ResponseTempTableOptions
{
    /// <summary>Temp table name when a test file has exactly one HTTP block.</summary>
    public string Name { get; set; } = "_response";

    /// <summary>Temp table name pattern when a file has 2+ HTTP blocks. <c>{n}</c> = the 1-based block ordinal.</summary>
    public string MultiNamePattern { get; set; } = "_response_{n}";

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
    /// <summary>Inline SQL executed as a single batch.</summary>
    public string? Sql { get; set; }

    /// <summary>Path (cwd-relative) to a .sql file executed as a single batch.</summary>
    public string? SqlFile { get; set; }

    /// <summary>Shell command (run via the OS shell). Non-zero exit aborts a Setup run.</summary>
    public string? Command { get; set; }

    /// <summary>Working directory for <see cref="Command"/> (cwd-relative); null => current directory.</summary>
    public string? WorkingDirectory { get; set; }

    public bool IsCommand => !string.IsNullOrWhiteSpace(Command);
}
