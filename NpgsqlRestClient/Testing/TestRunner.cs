using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlRest;

namespace NpgsqlRestClient.Testing;

/// <summary>
/// SQL test runner (`--test`). Discovers *.test.sql files and runs each on its own non-pooled connection,
/// invoking endpoints in-process (via <see cref="RoutineInvoker"/> on the test's ambient connection) and
/// asserting on the captured response. See scrap/test-runner-plan for the full design.
/// </summary>
public sealed class TestRunner
{
    // Failure/error color: ANSI 256-color 196 — the same red Serilog's Code theme uses (truer than the
    // 16-color ConsoleColor.Red, which renders orange in some themes). Emitted only to a real terminal.
    private const string AnsiFail = "\x1b[38;5;196m";

    public const int ExitPass = 0;
    public const int ExitFailures = 1;
    public const int ExitErrors = 2;
    public const int ExitConfig = 3;
    public const int ExitNoTests = 4;

    // Per-async-flow ambient connection: each parallel test file sets this so the endpoint pipeline
    // (NpgsqlRestOptions.AmbientConnectionAccessor) runs on that file's connection/transaction.
    private static readonly AsyncLocal<NpgsqlConnection?> Ambient = new();

    private readonly NpgsqlRestOptions _rest;
    private readonly TestRunnerOptions _opt;
    private readonly string _connString;
    private readonly ILogger? _log;
    private readonly bool _logNotices;
    private readonly Out _out = new();
    private volatile bool _failFast;

    private enum Outcome { Pass, Fail, Error }

    // Result of executing one step. Emit=false marks an arrange/act step that is not a reported test (it
    // only becomes a result when it errors). Name/Message describe the assertion when Emit=true.
    private readonly record struct StepResult(bool Emit, Outcome Outcome, string? Message, string? Name);

    // One reported test = one assertion: a boolean-returning SELECT, a DO block (passes unless it raises,
    // e.g. via ASSERT), or an HTTP step with `# @expect-status`. Errors from any step are also recorded
    // here so they surface and count.
    private sealed class AssertionResult
    {
        public required string Name { get; init; }
        public int? Line { get; init; }
        public Outcome Outcome { get; init; }
        public string? Message { get; init; }
        public string? Sql { get; init; }
    }

    // A test file groups its assertions; the file's outcome is the worst of them (Error > Fail > Pass).
    // Execution is fail-fast: assertions after the first failure/error in a file are not run (so not listed).
    private sealed class FileResult
    {
        public required string File { get; init; }
        public long ElapsedMs { get; set; }
        public List<AssertionResult> Assertions { get; } = [];
        public List<string> Notices { get; set; } = [];

        public Outcome Outcome =>
            Assertions.Any(a => a.Outcome == Outcome.Error) ? Outcome.Error :
            Assertions.Any(a => a.Outcome == Outcome.Fail) ? Outcome.Fail :
            Outcome.Pass;
    }

    public TestRunner(NpgsqlRestOptions rest, TestRunnerOptions opt, string baseConnectionString, ILogger? logger, bool logConnectionNotices = false)
    {
        _rest = rest;
        _opt = opt;
        // Always non-pooled: a fresh physical session per test (no temp-table / GUC / prepared-statement carryover).
        _connString = new NpgsqlConnectionStringBuilder(baseConnectionString) { Pooling = false }.ConnectionString;
        _log = logger;
        _logNotices = logConnectionNotices;
        // Wire the ambient accessor once; null when no test flow is active → zero effect on discovery/normal ops.
        _rest.AmbientConnectionAccessor = () => Ambient.Value;
    }

    /// <summary>Runs before endpoint discovery: collision check + Setup steps. Returns false on failure (logged, teardown attempted).</summary>
    public async Task<bool> SetupAsync(CancellationToken ct = default)
    {
        try
        {
            // Response-table names are validated and created per HTTP block (no pre-run permanent-table
            // collision check needed: writes are pg_temp-qualified and CREATE TEMP TABLE (no IF NOT EXISTS)
            // fails loudly on a real duplicate).
            _log?.LogDebug("setup: {Commands} command(s), {Sql} sql step(s)",
                _opt.Setup.Count(s => s.IsCommand), _opt.Setup.Count(s => !s.IsCommand));

            // Commands first (provision infra), then SqlFile/Sql (migrations/seed), in declared order within each group.
            foreach (var step in _opt.Setup.Where(s => s.IsCommand))
            {
                await RunCommandAsync(step, ct);
            }
            await using (var conn = new NpgsqlConnection(_connString))
            {
                await conn.OpenAsync(ct);
                foreach (var step in _opt.Setup.Where(s => !s.IsCommand))
                {
                    await RunSqlStepAsync(conn, step, ct);
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            _out.Line($"Test runner setup failed: {ex.Message}", ConsoleColor.Red);
            _log?.LogError(ex, "Test runner setup failed");
            await TeardownAsync(ct);
            return false;
        }
    }

    /// <summary>Runs after endpoint discovery: executes the test files and (always) Teardown. Returns the process exit code.</summary>
    public async Task<int> RunAsync(RoutineEndpoint[] endpoints, CancellationToken ct = default)
    {
        try
        {
            var files = DiscoverFiles();
            _log?.LogDebug("discovered {Count} test file(s) matching {Pattern}", files.Count, _opt.FilePattern);
            if (files.Count == 0)
            {
                _out.Line("Test runner: no test files matched FilePattern.", ConsoleColor.Yellow);
                return _opt.AllowEmpty ? ExitPass : ExitNoTests;
            }

            var lookup = BuildEndpointLookup(endpoints);
            var results = new ConcurrentBag<FileResult>();
            int dop = _opt.MaxParallelism > 0 ? _opt.MaxParallelism : Environment.ProcessorCount;

            // Console header stays a clean human report (just the file count, like other test runners). The
            // parallelism is a diagnostic → log channel at Debug, not the always-on console line.
            _out.Line($"NpgsqlRest test runner — {files.Count} file(s)", ConsoleColor.Cyan);
            _log?.LogDebug("running {Count} test file(s) at degree of parallelism {Parallelism}", files.Count, dop);

            await Parallel.ForEachAsync(
                files,
                new ParallelOptions { MaxDegreeOfParallelism = dop, CancellationToken = ct },
                async (file, _) =>
                {
                    if (_failFast) return; // stop scheduling new work after the first failure (in-flight finish)
                    var r = await RunFileAsync(file, lookup, ct);
                    results.Add(r);
                    if (_opt.FailFast && r.Outcome != Outcome.Pass) _failFast = true;
                });

            var ordered = results.OrderBy(r => r.File, StringComparer.Ordinal).ToList();
            ReportConsole(ordered);
            if (!string.IsNullOrWhiteSpace(_opt.JUnitOutput))
            {
                WriteJUnit(ordered, _opt.JUnitOutput!);
            }

            if (ordered.Any(r => r.Outcome == Outcome.Error)) return ExitErrors;
            if (ordered.Any(r => r.Outcome == Outcome.Fail)) return ExitFailures;
            return ExitPass;
        }
        finally
        {
            await TeardownAsync(ct);
        }
    }

    private async Task<FileResult> RunFileAsync(string file, IReadOnlyDictionary<string, RoutineEndpoint> lookup, CancellationToken runCt)
    {
        var sw = Stopwatch.StartNew();
        var result = new FileResult { File = file };
        var notices = new List<string>();
        NpgsqlConnection? conn = null;
        var contextName = Path.GetRelativePath(Environment.CurrentDirectory, file);

        using var perTestCts = CancellationTokenSource.CreateLinkedTokenSource(runCt);
        if (_opt.PerTestTimeout > TimeSpan.Zero) perTestCts.CancelAfter(_opt.PerTestTimeout);
        var ct = perTestCts.Token;
        int timeoutSecs = _opt.PerTestTimeout > TimeSpan.Zero ? (int)Math.Ceiling(_opt.PerTestTimeout.TotalSeconds) : 0;

        try
        {
            conn = new NpgsqlConnection(_connString);
            conn.Notice += (_, e) =>
            {
                // Route notices through the test runner's own log channel, tagged with this test file. The
                // log level follows the notice severity (info/notice => Information, warning => Warning, …);
                // the channel's MinimalLevel governs visibility. Also capture per-test for the console report.
                if (_logNotices && _log is not null)
                    NpgsqlRestLogger.LogConnectionNotice(_log, e.Notice, _rest.LogConnectionNoticeEventsMode, contextName);
                lock (notices) notices.Add(e.Notice.MessageText);
            };
            await conn.OpenAsync(ct);
            Ambient.Value = conn;

            var steps = SqlTestFileParser.Parse(await File.ReadAllTextAsync(file, ct));

            int httpTotal = steps.Count(s => s is HttpStep);
            int httpOrdinal = 0;
            _log?.LogDebug("parsed {File}: {Steps} step(s), {Http} HTTP block(s)", contextName, steps.Count, httpTotal);
            foreach (var step in steps)
            {
                StepResult sr;
                if (step is SqlStep sql)
                {
                    sr = await ExecuteSqlStepAsync(conn, sql, timeoutSecs, ct);
                }
                else if (step is HttpStep http)
                {
                    httpOrdinal++;
                    sr = await InvokeHttpStepAsync(conn, http, ResolveResponseTable(http, httpOrdinal, httpTotal), lookup, ct);
                }
                else
                {
                    continue;
                }

                if (sr.Emit)
                {
                    result.Assertions.Add(new AssertionResult
                    {
                        Name = sr.Name ?? DefaultAssertionName(step),
                        Line = step.LineNumber,
                        Outcome = sr.Outcome,
                        Message = sr.Message,
                        Sql = step is SqlStep s ? s.Text : null,
                    });
                }
                // Fail-fast within a file: a failed/errored step (a failing DO-block assert aborts the PG
                // transaction anyway) stops execution, so later assertions in this file are not run.
                if (sr.Outcome is Outcome.Fail or Outcome.Error) break;
            }
        }
        catch (OperationCanceledException) when (perTestCts.IsCancellationRequested && !runCt.IsCancellationRequested)
        {
            result.Assertions.Add(TimeoutAssertion());
        }
        catch (PostgresException pg) when (pg.SqlState == "57014")
        {
            result.Assertions.Add(TimeoutAssertion());
        }
        catch (Exception ex)
        {
            result.Assertions.Add(new AssertionResult { Name = "execution error", Outcome = Outcome.Error, Message = ex.Message });
        }
        finally
        {
            // No explicit ROLLBACK: the connection is non-pooled, so DisposeAsync physically closes it and
            // the server aborts any still-open transaction — that is the safety net for a test that didn't
            // roll back itself. An explicit ROLLBACK here would also raise PG warning 25P01
            // ("there is no transaction in progress") for the common case where the test already rolled
            // back or never opened a transaction.
            Ambient.Value = null;
            if (conn is not null) await conn.DisposeAsync();
        }

        result.ElapsedMs = sw.ElapsedMilliseconds;
        result.Notices = notices;
        _log?.LogDebug("{File}: {Outcome} ({Count} assertion(s), {Ms}ms)",
            contextName, result.Outcome, result.Assertions.Count, result.ElapsedMs);
        return result;

        AssertionResult TimeoutAssertion() => new()
        {
            Name = "timed out",
            Outcome = Outcome.Error,
            Message = $"timed out after {_opt.PerTestTimeout.TotalSeconds:0}s",
        };
    }

    private async Task<StepResult> ExecuteSqlStepAsync(
        NpgsqlConnection conn, SqlStep step, int timeoutSecs, CancellationToken ct)
    {
        _log?.LogTrace("sql (line {Line}): {Sql}", step.LineNumber, step.Text.Trim());
        try
        {
            await using var cmd = new NpgsqlCommand(step.Text, conn);
            if (timeoutSecs > 0) cmd.CommandTimeout = timeoutSecs;
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            // Boolean first column => assertion (first row only; 0 rows = pass). The optional second column
            // is the human-readable assertion name/message.
            if (reader.FieldCount >= 1 && reader.GetFieldType(0) == typeof(bool))
            {
                string? desc = null;
                bool pass = true;
                if (await reader.ReadAsync(ct))
                {
                    pass = !reader.IsDBNull(0) && reader.GetBoolean(0);
                    desc = reader.FieldCount >= 2 && !reader.IsDBNull(1) ? reader.GetValue(1)?.ToString() : null;
                }
                return pass
                    ? new StepResult(true, Outcome.Pass, null, desc)
                    : new StepResult(true, Outcome.Fail, desc ?? $"assertion failed: {FirstLine(step.Text)}", desc);
            }
            // A DO block produces no result set; it counts as an assertion that passes unless it raised
            // (e.g. ASSERT => P0004, caught below). Any other statement is arrange/act, not a reported test.
            return step.IsDoBlock
                ? new StepResult(true, Outcome.Pass, null, null)
                : new StepResult(false, Outcome.Pass, null, null);
        }
        catch (PostgresException pg)
        {
            if (pg.SqlState == "P0004") return new StepResult(true, Outcome.Fail, pg.MessageText, null); // assert
            if (pg.SqlState == "57014") throw; // timeout — let RunFileAsync classify
            return new StepResult(true, Outcome.Error, $"{pg.SqlState}: {pg.MessageText}", null);
        }
    }

    private async Task<StepResult> InvokeHttpStepAsync(
        NpgsqlConnection conn, HttpStep step, string responseTable, IReadOnlyDictionary<string, RoutineEndpoint> lookup, CancellationToken ct)
    {
        var httpName = $"{step.Method} {step.Path}";
        var pathOnly = StripQuery(step.Path);
        if (lookup.TryGetValue($"{step.Method} {pathOnly}", out var ep))
        {
            if (ep.SseEventsPath is not null || ep.SsePublishEnabled)
                return new StepResult(true, Outcome.Error, $"SSE endpoints are not supported in test mode: {httpName}", httpName);
            if (ep.Upload)
                return new StepResult(true, Outcome.Error, $"upload endpoints are not supported in test mode: {httpName}", httpName);
            if (ep.IsProxy)
            {
                var host = ep.ProxyHost ?? _rest.ProxyOptions?.Host;
                if (host is not null && !host.StartsWith('/'))
                    return new StepResult(true, Outcome.Error, $"outbound proxy/HTTP-type endpoints are disallowed in test mode: {httpName}", httpName);
            }
        }

        var user = BuildPrincipal(step.Claims);

        Dictionary<string, string>? headers = null;
        string? contentType = null;
        if (step.Headers.Count > 0)
        {
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, value) in step.Headers)
            {
                headers[name] = value;
                if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase)) contentType = value;
            }
        }

        Ambient.Value = conn; // ensure this file's connection is used by the endpoint pipeline
        _log?.LogTrace("http {Method} {Path} (line {Line})", step.Method, step.Path, step.LineNumber);
        var response = await RoutineInvoker.InvokeAsync(step.Method, step.Path, headers, step.Body, contentType, user, ct);
        _log?.LogTrace("http {Method} {Path} → {Status}, captured into temp table \"{Table}\"",
            step.Method, step.Path, response.StatusCode, responseTable);

        await WriteResponseAsync(conn, responseTable, response, ct);

        // An HTTP block is a reported assertion only when it carries `# @expect-status`; otherwise it is an
        // act step (capture the response into the temp table) and produces no test of its own.
        if (step.ExpectStatus.HasValue)
        {
            var name = $"{httpName} (expect {step.ExpectStatus.Value})";
            return response.StatusCode == step.ExpectStatus.Value
                ? new StepResult(true, Outcome.Pass, null, name)
                : new StepResult(true, Outcome.Fail, $"expected status {step.ExpectStatus.Value} but got {response.StatusCode} for {httpName}", name);
        }
        return new StepResult(false, Outcome.Pass, null, null);
    }

    private async Task WriteResponseAsync(NpgsqlConnection conn, string table, RoutineInvokeResult response, CancellationToken ct)
    {
        if (!ValidateIdentifier(table))
            throw new InvalidOperationException($"invalid response table name '{table}'");

        var c = _opt.ResponseTempTable.Columns;
        var cols = new List<(string Name, string Type, object? Value, bool Jsonb)>();
        if (!string.IsNullOrWhiteSpace(c.Status)) cols.Add((c.Status!, "int", response.StatusCode, false));
        if (!string.IsNullOrWhiteSpace(c.Body)) cols.Add((c.Body!, "text", (object?)response.Body ?? DBNull.Value, false));
        if (!string.IsNullOrWhiteSpace(c.ContentType)) cols.Add((c.ContentType!, "text", (object?)response.ContentType ?? DBNull.Value, false));
        if (!string.IsNullOrWhiteSpace(c.Headers)) cols.Add((c.Headers!, "jsonb", (object?)response.Headers ?? DBNull.Value, true));
        if (!string.IsNullOrWhiteSpace(c.IsSuccess)) cols.Add((c.IsSuccess!, "boolean", response.IsSuccess, false));
        if (cols.Count == 0)
            throw new InvalidOperationException("ResponseTempTable.Columns has no columns enabled.");

        foreach (var col in cols)
        {
            if (!ValidateIdentifier(col.Name))
                throw new InvalidOperationException($"invalid response column name '{col.Name}'");
        }

        // CREATE TEMP TABLE (no IF NOT EXISTS): each HTTP block gets its own fresh table, so a name clash
        // (e.g. a duplicate `# @response` name) fails the test loudly. TEMP forces pg_temp; the INSERT is
        // pg_temp-qualified so it can never touch a permanent table. (Client runs with Npgsql SQL-rewriting
        // disabled → one statement per command, positional params.)
        var defs = string.Join(", ", cols.Select(col => $"\"{col.Name}\" {col.Type}"));
        await using (var create = new NpgsqlCommand($"create temp table \"{table}\" ({defs})", conn))
        {
            await create.ExecuteNonQueryAsync(ct);
        }

        var colList = string.Join(", ", cols.Select(col => $"\"{col.Name}\""));
        var valList = string.Join(", ", cols.Select((col, i) => col.Jsonb ? $"${i + 1}::jsonb" : $"${i + 1}"));
        await using var ins = new NpgsqlCommand($"insert into pg_temp.\"{table}\" ({colList}) values ({valList})", conn);
        foreach (var col in cols)
        {
            ins.Parameters.AddWithValue(col.Value ?? DBNull.Value);
        }
        await ins.ExecuteNonQueryAsync(ct);
    }

    // The temp-table name for an HTTP block: explicit `# @response` wins; otherwise a file with a single
    // HTTP block uses Name (`_response`), and a file with 2+ blocks uses MultiNamePattern with {n} = the
    // 1-based block ordinal (`_response_1`, `_response_2`, …).
    private string ResolveResponseTable(HttpStep http, int ordinal, int httpTotal)
    {
        if (!string.IsNullOrWhiteSpace(http.ResponseTable)) return http.ResponseTable!.Trim();
        if (httpTotal <= 1) return _opt.ResponseTempTable.Name;
        return _opt.ResponseTempTable.MultiNamePattern
            .Replace("{n}", ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static ClaimsPrincipal? BuildPrincipal(IReadOnlyList<(string Name, string Value)> claims)
    {
        if (claims.Count == 0) return null; // anonymous → 401 on protected endpoints
        var identity = new ClaimsIdentity(authenticationType: "TestRunner"); // non-null auth type => IsAuthenticated
        foreach (var (name, value) in claims)
        {
            identity.AddClaim(new Claim(name, value));
        }
        return new ClaimsPrincipal(identity);
    }

    // -------- Setup/Teardown step execution --------

    private async Task RunCommandAsync(TestSetupStep step, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = string.IsNullOrWhiteSpace(step.WorkingDirectory)
                ? Environment.CurrentDirectory
                : Path.GetFullPath(step.WorkingDirectory),
        };
        psi.ArgumentList.Add(OperatingSystem.IsWindows() ? "/c" : "-c");
        psi.ArgumentList.Add(step.Command!);

        _log?.LogDebug("setup/teardown command: {Command}", step.Command);
        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        if (!string.IsNullOrWhiteSpace(stdout)) _log?.LogDebug("command output: {Out}", stdout.Trim());
        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"command failed (exit {proc.ExitCode}): {step.Command}{(string.IsNullOrWhiteSpace(stderr) ? "" : "\n" + stderr.Trim())}");
        }
    }

    private async Task RunSqlStepAsync(NpgsqlConnection conn, TestSetupStep step, CancellationToken ct)
    {
        string sql;
        if (!string.IsNullOrWhiteSpace(step.SqlFile))
        {
            sql = await File.ReadAllTextAsync(Path.GetFullPath(step.SqlFile), ct);
        }
        else
        {
            sql = step.Sql ?? "";
        }
        if (string.IsNullOrWhiteSpace(sql)) return;
        _log?.LogDebug("setup/teardown sql: {Source}", step.SqlFile ?? "(inline)");
        // Split into individual statements (SQL-rewriting is disabled → one statement per command).
        // Reuses the test-file parser; DO blocks / dollar-quoted bodies stay whole.
        foreach (var parsed in SqlTestFileParser.Parse(sql))
        {
            if (parsed is SqlStep s)
            {
                _log?.LogTrace("sql: {Sql}", s.Text.Trim());
                await using var cmd = new NpgsqlCommand(s.Text, conn);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
    }

    private async Task TeardownAsync(CancellationToken ct)
    {
        if (_opt.Keep || _opt.Teardown.Count == 0) return;
        _log?.LogDebug("teardown: {Steps} step(s)", _opt.Teardown.Count);
        try
        {
            await using var conn = new NpgsqlConnection(_connString);
            await conn.OpenAsync(ct);
            foreach (var step in _opt.Teardown.Where(s => !s.IsCommand)) // SQL first
            {
                try { await RunSqlStepAsync(conn, step, ct); }
                catch (Exception ex) { _log?.LogWarning(ex, "teardown SQL step failed"); }
            }
        }
        catch (Exception ex) { _log?.LogWarning(ex, "teardown connection failed"); }

        foreach (var step in _opt.Teardown.Where(s => s.IsCommand)) // commands last (e.g. docker down)
        {
            try { await RunCommandAsync(step, ct); }
            catch (Exception ex) { _log?.LogWarning(ex, "teardown command failed"); }
        }
    }

    // -------- discovery / reporting / helpers --------

    private List<string> DiscoverFiles()
    {
        var pattern = _opt.FilePattern;
        if (string.IsNullOrWhiteSpace(pattern)) return [];

        int firstWildcard = pattern.IndexOfAny(['*', '?']);
        if (firstWildcard < 0)
        {
            return File.Exists(pattern) ? [Path.GetFullPath(pattern)] : [];
        }

        int lastSlash = pattern.LastIndexOf('/', firstWildcard);
        string baseDir = lastSlash >= 0 ? pattern[..lastSlash] : ".";
        if (!Directory.Exists(baseDir)) return [];

        var searchOption = pattern.Contains("**") ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var result = new List<string>();
        foreach (var file in Directory.EnumerateFiles(baseDir, "*", searchOption))
        {
            if (Parser.IsPatternMatch(file.Replace('\\', '/'), pattern))
            {
                result.Add(file);
            }
        }
        return result;
    }

    private static Dictionary<string, RoutineEndpoint> BuildEndpointLookup(RoutineEndpoint[] endpoints)
    {
        var d = new Dictionary<string, RoutineEndpoint>(endpoints.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var ep in endpoints)
        {
            d[$"{ep.Method} {ep.Path}"] = ep;
        }
        return d;
    }

    // Print the failing statement under the message (dimmed). Capped to its first line unless --verbose.
    private void WriteFailingSql(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return;
        var text = sql.Trim();
        if (_opt.Verbose)
        {
            _out.Line("        " + text.Replace("\n", "\n        "), ConsoleColor.DarkGray);
            return;
        }
        int nl = text.IndexOf('\n');
        var line = (nl >= 0 ? text[..nl] : text).TrimEnd();
        bool more = nl >= 0 || line.Length > 120;
        if (line.Length > 120) line = line[..120];
        _out.Line($"        {line}{(more ? " …" : "")}", ConsoleColor.DarkGray);
    }

    private void ReportConsole(List<FileResult> results)
    {
        // Counts are at assertion granularity (each boolean SELECT / DO block / `# @expect-status` HTTP
        // step is one test); files are the grouping unit. A file is fail-fast, so on failure the assertions
        // listed are those that ran up to and including the first failure.
        int passed = 0, failed = 0, errored = 0, totalAssertions = 0;
        foreach (var fr in results)
        {
            var rel = Path.GetRelativePath(Environment.CurrentDirectory, fr.File);
            int aPass = fr.Assertions.Count(a => a.Outcome == Outcome.Pass);
            passed += aPass;
            failed += fr.Assertions.Count(a => a.Outcome == Outcome.Fail);
            errored += fr.Assertions.Count(a => a.Outcome == Outcome.Error);
            totalAssertions += fr.Assertions.Count;

            if (fr.Assertions.Count == 0)
            {
                // The file ran but contained no recognizable assertion — surface it rather than count a pass.
                _out.Line($"PASS  {rel}  (no assertions, {fr.ElapsedMs}ms)", ConsoleColor.Yellow);
            }
            else if (fr.Outcome == Outcome.Pass)
            {
                _out.Line($"PASS  {rel}  ({aPass} assertion{(aPass == 1 ? "" : "s")}, {fr.ElapsedMs}ms)", ConsoleColor.Green);
                if (_opt.Verbose)
                    foreach (var a in fr.Assertions) _out.Line($"        ✓ {a.Name}", ConsoleColor.DarkGray);
            }
            else
            {
                var label = fr.Outcome == Outcome.Error ? "ERROR" : "FAIL ";
                _out.LineAnsi($"{label} {rel}  ({fr.ElapsedMs}ms)", AnsiFail);
                foreach (var a in fr.Assertions)
                {
                    if (a.Outcome == Outcome.Pass)
                    {
                        if (_opt.Verbose) _out.Line($"        ✓ {a.Name}", ConsoleColor.DarkGray);
                        continue;
                    }
                    var loc = a.Line is null ? "" : $"  [{rel}:{a.Line}]";
                    _out.LineAnsi($"        ✗ {a.Name}{loc}", AnsiFail);
                    if (!string.IsNullOrWhiteSpace(a.Message) && a.Message != a.Name) _out.LineAnsi($"          {a.Message}", AnsiFail);
                    WriteFailingSql(a.Sql);
                }
            }

            if (fr.Notices.Count > 0 && (_opt.Verbose || fr.Outcome != Outcome.Pass))
                foreach (var n in fr.Notices) _out.Line($"        notice: {n}", ConsoleColor.DarkGray);
        }

        var summary = $"\n{passed} passed, {failed} failed, {errored} error(s)  —  {totalAssertions} assertion{(totalAssertions == 1 ? "" : "s")} in {results.Count} file{(results.Count == 1 ? "" : "s")}";
        if (failed + errored == 0) _out.Line(summary, ConsoleColor.Green);
        else _out.LineAnsi(summary, AnsiFail);
    }

    private static void WriteJUnit(List<FileResult> results, string path)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        // One <testcase> per assertion (classname = file), so CI tools count individual tests. A file with
        // no assertions still appears as a single skipped testcase. Notices are attached to failing cases.
        int tests = results.Sum(f => Math.Max(f.Assertions.Count, 1));
        int failures = results.Sum(f => f.Assertions.Count(a => a.Outcome == Outcome.Fail));
        int errors = results.Sum(f => f.Assertions.Count(a => a.Outcome == Outcome.Error));
        int skipped = results.Count(f => f.Assertions.Count == 0);

        var suite = new XElement("testsuite",
            new XAttribute("name", "npgsqlrest"),
            new XAttribute("tests", tests),
            new XAttribute("failures", failures),
            new XAttribute("errors", errors),
            new XAttribute("skipped", skipped),
            new XAttribute("time", (results.Sum(r => r.ElapsedMs) / 1000.0).ToString("0.000", inv)));

        foreach (var fr in results)
        {
            // Spread the file's wall-clock time evenly across its assertions for per-case timing.
            double caseTime = fr.Assertions.Count > 0 ? fr.ElapsedMs / 1000.0 / fr.Assertions.Count : fr.ElapsedMs / 1000.0;
            string caseTimeStr = caseTime.ToString("0.000", inv);

            if (fr.Assertions.Count == 0)
            {
                var empty = new XElement("testcase",
                    new XAttribute("name", "(no assertions)"),
                    new XAttribute("classname", fr.File),
                    new XAttribute("time", caseTimeStr));
                empty.Add(new XElement("skipped"));
                suite.Add(empty);
                continue;
            }

            foreach (var a in fr.Assertions)
            {
                var tc = new XElement("testcase",
                    new XAttribute("name", a.Name),
                    new XAttribute("classname", fr.File),
                    new XAttribute("time", caseTimeStr));
                if (a.Outcome == Outcome.Fail)
                    tc.Add(new XElement("failure", new XAttribute("message", a.Message ?? "assertion failed"), $"{fr.File}{(a.Line is null ? "" : $":{a.Line}")}"));
                else if (a.Outcome == Outcome.Error)
                    tc.Add(new XElement("error", new XAttribute("message", a.Message ?? "error"), $"{fr.File}{(a.Line is null ? "" : $":{a.Line}")}"));
                if (a.Outcome != Outcome.Pass && fr.Notices.Count > 0)
                    tc.Add(new XElement("system-out", string.Join('\n', fr.Notices)));
                suite.Add(tc);
            }
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        new XDocument(new XDeclaration("1.0", "utf-8", null), suite).Save(path);
    }

    private static string StripQuery(string path)
    {
        int q = path.IndexOf('?');
        return q >= 0 ? path[..q] : path;
    }

    private static string FirstLine(string text)
    {
        int nl = text.IndexOf('\n');
        var line = (nl >= 0 ? text[..nl] : text).Trim();
        return line.Length > 80 ? line[..80] + "…" : line;
    }

    // Fallback display name when a step has no inherent label (a boolean SELECT without a description column,
    // a DO block, or a non-`# @expect-status` HTTP step that errored).
    private static string DefaultAssertionName(TestStep step) => step switch
    {
        SqlStep { IsDoBlock: true } => $"assert block (line {step.LineNumber})",
        SqlStep sql => FirstLine(sql.Text),
        HttpStep http => $"{http.Method} {http.Path}",
        _ => $"step (line {step.LineNumber})",
    };

    // Valid unquoted/quoted SQL identifier: [A-Za-z_][A-Za-z0-9_]*  (prevents injection in table/column DDL).
    private static bool ValidateIdentifier(string? id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        if (!(char.IsLetter(id[0]) || id[0] == '_')) return false;
        for (int i = 1; i < id.Length; i++)
        {
            if (!(char.IsLetterOrDigit(id[i]) || id[i] == '_')) return false;
        }
        return true;
    }
}
