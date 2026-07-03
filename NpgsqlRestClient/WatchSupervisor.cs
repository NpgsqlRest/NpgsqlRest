using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace NpgsqlRestClient;

/// <summary>
/// Server watch mode (`--watch` without `--test`): this process becomes a tiny SUPERVISOR that never
/// builds the server itself — it spawns this same executable as a CHILD (marked via an environment
/// variable), watches the SqlFileSource tree and the configuration files, and restarts the child on
/// every debounced change. The child runs the completely normal server pipeline (production parity),
/// with one relaxation: SqlFileSource ErrorMode is forced from Exit to Skip so a broken SQL file logs
/// its error and drops that endpoint instead of killing the server (see App.CreateEndpointSources).
///
/// Design notes:
/// - A child process (vs. in-process rebuild) guarantees clean state: Kestrel's port binding and
///   ASP.NET's route table cannot be torn down in-process (routes can be added, never removed), and
///   static caches would accumulate. This is the same conclusion `dotnet watch` and nodemon reached.
///   The in-process hot-swap upgrade is designed in scrap/WATCH_HOT_SWAP_PLAN.md (target 3.20).
/// - Graceful stop: SIGTERM via libc on Unix (Process.Kill would be SIGKILL), hard kill fallback after
///   a timeout; on Windows there is no SIGTERM for console children — hard process-tree kill, which is
///   acceptable here because a dev server holds nothing that needs teardown.
/// - The child runs a parent WATCHDOG: if the supervisor dies without stopping it (SIGKILL, a wrapper
///   runner like `bun run` murdering the process group), the child exits by itself — no orphan holding
///   the port.
/// </summary>
public static class WatchSupervisor
{
    /// <summary>Set on the child process; the value is the supervisor's PID (for the parent watchdog).</summary>
    public const string ChildEnv = "NPGSQLREST_WATCH_CHILD";

    public static bool IsChild { get; } = string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ChildEnv)) is false;

    /// <summary>Child exit code meaning "the database poller detected a change — respawn me immediately".</summary>
    public const int DbChangeExitCode = 64;

    // Blittable signature — plain DllImport is AOT-safe here and avoids AllowUnsafeBlocks.
    [DllImport("libc", SetLastError = true, EntryPoint = "kill")]
    private static extern int kill(int pid, int sig);
    private const int SIGTERM = 15;

    /// <summary>
    /// Called in the CHILD: exit when the supervisor process disappears without stopping us first,
    /// so a SIGKILLed supervisor never leaves an orphaned server holding the port.
    /// </summary>
    public static void StartParentWatchdog()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable(ChildEnv), out var parentPid) is false)
        {
            return;
        }
        var thread = new Thread(() =>
        {
            try
            {
                Process.GetProcessById(parentPid).WaitForExit();
            }
            catch
            {
                // already gone
            }
            Environment.Exit(143);
        })
        {
            IsBackground = true,
            Name = "watch-parent-watchdog",
        };
        thread.Start();
    }

    /// <summary>Supervisor main loop. Returns the process exit code.</summary>
    public static async Task<int> RunAsync(Config config, string[] args)
    {
        var output = new Out();

        // Something to watch? A SqlFileSource tree, database catalog polling, or both.
        var sqlFileCfg = config.NpgsqlRestCfg.GetSection("SqlFileSource");
        string? filePattern = null;
        if (sqlFileCfg.Exists() && config.GetConfigBool("Enabled", sqlFileCfg, true))
        {
            filePattern = config.GetConfigStr("FilePattern", sqlFileCfg);
        }
        // Database polling (the child does the actual polling by hashing the routine discovery query;
        // the supervisor needs the value for validation and the banner). With polling active, --watch is
        // valid even without a SQL file source — a routines-only project restarts on create/replace/drop/
        // comment. Polling is only meaningful when the routine source itself is enabled.
        var dbPollInterval = Builder.ParseTestTimeout(
            config.GetConfigStr("DatabasePollingInterval", config.Cfg.GetSection("Watch")), TimeSpan.FromSeconds(2));
        var routineOptionsCfg = config.NpgsqlRestCfg.GetSection("RoutineOptions");
        if (routineOptionsCfg.Exists() && config.GetConfigBool("Enabled", routineOptionsCfg, true) is false)
        {
            dbPollInterval = TimeSpan.Zero;
        }

        string? baseDirFull = null;
        var skipPattern = "*.test.sql";
        if (string.IsNullOrWhiteSpace(filePattern) is false)
        {
            var baseDir = WatchUtils.GetWatchBaseDir(filePattern);
            if (baseDir is null)
            {
                output.LineAnsi(
                    $"watch: cannot watch — SqlFileSource FilePattern \"{filePattern}\" has no existing base directory.",
                    Testing.TestRunner.AnsiFail);
                return 1;
            }
            baseDirFull = Path.GetFullPath(baseDir);
            skipPattern = (config.GetConfigStr("SkipPattern", sqlFileCfg) ?? "*.test.sql").Replace('\\', '/');
        }
        else if (dbPollInterval <= TimeSpan.Zero)
        {
            output.LineAnsi(
                "watch: nothing to watch — --watch without --test requires an enabled SqlFileSource with a FilePattern and/or database polling (Watch:DatabasePollingInterval). For test watch mode use --test --watch.",
                Testing.TestRunner.AnsiFail);
            return 1;
        }

        // Config files restart the server too: the .json/.jsonc files passed on the command line, plus
        // the implicit default when present.
        var configFiles = args
            .Where(a => a.StartsWith('-') is false
                && (a.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || a.EndsWith(".jsonc", StringComparison.OrdinalIgnoreCase))
                && File.Exists(a))
            .Select(Path.GetFullPath)
            .ToList();
        if (File.Exists("appsettings.json"))
        {
            configFiles.Add(Path.GetFullPath("appsettings.json"));
        }
        configFiles = [.. configFiles.Distinct(StringComparer.Ordinal)];
        var configFileSet = new HashSet<string>(configFiles, StringComparer.Ordinal);

        using var stop = new CancellationTokenSource();
        bool stopping = false;
        void OnSignal(PosixSignalContext ctx)
        {
            // The child receives Ctrl+C from the terminal group by itself; for SIGTERM (docker stop,
            // where only PID 1 is signalled) the shutdown path below forwards the stop to the child.
            ctx.Cancel = true;
            stopping = true;
            stop.Cancel();
        }
        using var sigInt = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal);
        using var sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal);

        // Watchers (or the polling fallback for bind-mount environments) feed one debounced channel.
        var changes = Channel.CreateUnbounded<string>();
        var watchers = new List<FileSystemWatcher>();
        bool polling = WatchUtils.UsePollingWatcher;
        if (polling)
        {
            WatchUtils.StartPollingWatcher(baseDirFull ?? "", configFiles, changes.Writer, stop.Token);
        }
        else
        {
            if (baseDirFull is not null)
            {
                watchers.Add(WatchUtils.NewSqlWatcher(baseDirFull, changes.Writer));
            }
            foreach (var dir in configFiles.Select(Path.GetDirectoryName).Where(d => d is not null).Distinct(StringComparer.Ordinal))
            {
                var w = new FileSystemWatcher(dir!)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                };
                void OnFsEvent(object s, FileSystemEventArgs e)
                {
                    if (configFileSet.Contains(e.FullPath))
                    {
                        changes.Writer.TryWrite(e.FullPath);
                    }
                }
                w.Changed += OnFsEvent;
                w.Created += OnFsEvent;
                w.Renamed += (s, e) => { if (configFileSet.Contains(e.FullPath)) changes.Writer.TryWrite(e.FullPath); };
                w.EnableRaisingEvents = true;
                watchers.Add(w);
            }
        }

        var watchingParts = new List<string>(3);
        if (baseDirFull is not null)
        {
            watchingParts.Add($"{Path.GetRelativePath(Environment.CurrentDirectory, baseDirFull)} (*.sql)");
        }
        if (configFiles.Count > 0)
        {
            watchingParts.Add("config files");
        }
        if (dbPollInterval > TimeSpan.Zero)
        {
            watchingParts.Add($"database (poll {dbPollInterval.TotalSeconds:0.#}s)");
        }
        var watchingWhat = string.Join(" + ", watchingParts) + (polling ? " [polling]" : "");
        output.Line($"watch mode: supervising the server process, watching {watchingWhat} — Ctrl+C to stop", ConsoleColor.Cyan);

        Process child = Spawn(args);
        Task exitTask = child.WaitForExitAsync(CancellationToken.None);
        // The pending batch survives iterations that don't consume it (e.g. a database-change respawn),
        // so the channel never has two competing readers.
        Task<List<string>?>? pendingBatch = null;

        try
        {
            while (stopping is false)
            {
                pendingBatch ??= WatchUtils.NextChangeBatchAsync(changes.Reader, stop.Token);
                var completed = await Task.WhenAny(exitTask, pendingBatch);

                if (stopping)
                {
                    break;
                }

                if (completed == exitTask && pendingBatch.IsCompleted is false)
                {
                    if (child.ExitCode == DbChangeExitCode)
                    {
                        // The child's database poller detected a routine/annotation change and asked to be
                        // restarted.
                        output.Line($"\n— {DateTime.Now:HH:mm:ss} database change detected — restarting —", ConsoleColor.Cyan);
                        child = Spawn(args);
                        exitTask = child.WaitForExitAsync(CancellationToken.None);
                        continue;
                    }
                    // The child died on its own (crash, config error, port conflict). Don't respawn in a
                    // loop — hold until the next file change, then try again.
                    output.Line($"\nserver exited (code {child.ExitCode}) — waiting for file changes", ConsoleColor.Yellow);
                    var wakeBatch = await pendingBatch;
                    pendingBatch = null;
                    if (wakeBatch is null || stopping)
                    {
                        break;
                    }
                    if (Relevant(wakeBatch, baseDirFull, skipPattern, configFileSet) is not { } wakeTrigger)
                    {
                        // Irrelevant change (e.g. a *.test.sql edit) — keep holding.
                        continue;
                    }
                    output.Line($"— {DateTime.Now:HH:mm:ss} change detected ({wakeTrigger}) — restarting —", ConsoleColor.Cyan);
                    child = Spawn(args);
                    exitTask = child.WaitForExitAsync(CancellationToken.None);
                    continue;
                }

                var batch = await pendingBatch;
                pendingBatch = null;
                if (batch is null)
                {
                    break;
                }
                if (Relevant(batch, baseDirFull, skipPattern, configFileSet) is not { } trigger)
                {
                    continue;
                }
                output.Line($"\n— {DateTime.Now:HH:mm:ss} change detected ({trigger}) — restarting —", ConsoleColor.Cyan);
                StopChild(child);
                child = Spawn(args);
                exitTask = child.WaitForExitAsync(CancellationToken.None);
            }
            return 0;
        }
        finally
        {
            foreach (var w in watchers)
            {
                w.Dispose();
            }
            StopChild(child);
        }
    }

    /// <summary>
    /// First relevant path in the batch (cwd-relative, for display), or null when the whole batch is
    /// noise. Relevant: a config file, or a *.sql file under the watched tree that matches the
    /// SqlFileSource FilePattern and does NOT match SkipPattern (a test-file edit must not bounce the
    /// server).
    /// </summary>
    private static string? Relevant(List<string> batch, string? baseDirFull, string skipPattern, HashSet<string> configFileSet)
    {
        foreach (var path in batch.Distinct(StringComparer.Ordinal))
        {
            var full = Path.GetFullPath(path);
            var rel = Path.GetRelativePath(Environment.CurrentDirectory, full).Replace('\\', '/');
            if (configFileSet.Contains(full))
            {
                return rel;
            }
            if (baseDirFull is not null
                && full.StartsWith(baseDirFull, StringComparison.Ordinal)
                && rel.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)
                && WatchUtils.MatchesCwdRelativePattern(rel, skipPattern) is false)
            {
                return rel;
            }
        }
        return null;
    }

    private static Process Spawn(string[] args)
    {
        var exe = Environment.ProcessPath ?? "dotnet";
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, // stdout/stderr inherited — the child's logs are the console output
        };
        // Under `dotnet NpgsqlRestClient.dll ...` ProcessPath is the dotnet host; argv[0] is the dll path.
        if (string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            psi.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
        }
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        psi.Environment[ChildEnv] = Environment.ProcessId.ToString();
        return Process.Start(psi)!;
    }

    private static void StopChild(Process child)
    {
        try
        {
            if (child.HasExited)
            {
                return;
            }
            if (OperatingSystem.IsWindows())
            {
                // No SIGTERM for console children on Windows; a dev server has nothing needing teardown.
                child.Kill(entireProcessTree: true);
            }
            else
            {
                kill(child.Id, SIGTERM); // graceful: Kestrel drains and stops
                if (child.WaitForExit(5000) is false)
                {
                    child.Kill(entireProcessTree: true);
                }
            }
            child.WaitForExit();
        }
        catch
        {
            // already gone
        }
    }
}
