using System.Threading.Channels;
using NpgsqlRest;

namespace NpgsqlRestClient;

/// <summary>
/// Shared file-watching primitives used by both watch modes: the test runner's in-process watch
/// (<see cref="Testing.TestRunner"/>) and the server watch supervisor (<see cref="WatchSupervisor"/>).
/// </summary>
public static class WatchUtils
{
    /// <summary>A cwd-relative path against a cwd-relative glob (leading "./" tolerated on the pattern).</summary>
    public static bool MatchesCwdRelativePattern(string relativePath, string pattern)
    {
        pattern = pattern.Replace('\\', '/');
        if (pattern.StartsWith("./"))
        {
            pattern = pattern[2..];
        }
        return Parser.IsPatternMatch(relativePath, pattern);
    }

    /// <summary>
    /// The deepest fixed (wildcard-free) directory of a glob pattern — same logic file discovery uses.
    /// Null when the directory does not exist.
    /// </summary>
    public static string? GetWatchBaseDir(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }
        int firstWildcard = pattern.IndexOfAny(['*', '?']);
        if (firstWildcard < 0)
        {
            var dir = Path.GetDirectoryName(pattern);
            return string.IsNullOrEmpty(dir) ? "." : dir;
        }
        int lastSlash = pattern.LastIndexOf('/', firstWildcard);
        var baseDir = lastSlash >= 0 ? pattern[..lastSlash] : ".";
        return Directory.Exists(baseDir) ? baseDir : null;
    }

    /// <summary>
    /// First change (blocking), then a quiet period so editor write bursts coalesce into one batch.
    /// Returns null when cancelled.
    /// </summary>
    public static async Task<List<string>?> NextChangeBatchAsync(ChannelReader<string> reader, CancellationToken ct)
    {
        string first;
        try
        {
            first = await reader.ReadAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        var batch = new List<string> { first };
        while (true)
        {
            try { await Task.Delay(300, ct); } catch (OperationCanceledException) { return null; }
            bool any = false;
            while (reader.TryRead(out var more))
            {
                batch.Add(more);
                any = true;
            }
            if (!any) return batch;
        }
    }

    /// <summary>
    /// A recursive *.sql FileSystemWatcher over a directory, feeding full paths into the channel.
    /// </summary>
    public static FileSystemWatcher NewSqlWatcher(string fullDir, ChannelWriter<string> writer)
    {
        var w = new FileSystemWatcher(fullDir, "*.sql")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        void OnFsEvent(object s, FileSystemEventArgs e) => writer.TryWrite(e.FullPath);
        w.Changed += OnFsEvent;
        w.Created += OnFsEvent;
        w.Renamed += (s, e) => writer.TryWrite(e.FullPath);
        w.EnableRaisingEvents = true;
        return w;
    }

    /// <summary>
    /// True when the ecosystem-standard DOTNET_USE_POLLING_FILE_WATCHER is set — the escape hatch for
    /// environments where inotify events don't cross the filesystem boundary (Docker Desktop bind mounts,
    /// some network shares).
    /// </summary>
    public static bool UsePollingWatcher =>
        Environment.GetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER") is { } v
        && (v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Polling fallback: scans *.sql under <paramref name="fullDir"/> plus the extra files every second
    /// and writes changed/new paths into the channel. Runs until the token is cancelled.
    /// </summary>
    public static void StartPollingWatcher(string fullDir, IReadOnlyList<string> extraFiles, ChannelWriter<string> writer, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            var known = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            void Scan(bool emit)
            {
                IEnumerable<string> files = Directory.Exists(fullDir)
                    ? Directory.EnumerateFiles(fullDir, "*.sql", SearchOption.AllDirectories)
                    : [];
                foreach (var f in files.Concat(extraFiles))
                {
                    DateTime mtime;
                    try { mtime = File.GetLastWriteTimeUtc(f); } catch { continue; }
                    if (known.TryGetValue(f, out var prev))
                    {
                        if (prev != mtime)
                        {
                            known[f] = mtime;
                            if (emit) writer.TryWrite(f);
                        }
                    }
                    else
                    {
                        known[f] = mtime;
                        if (emit) writer.TryWrite(f);
                    }
                }
            }
            Scan(emit: false);
            while (ct.IsCancellationRequested is false)
            {
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
                Scan(emit: true);
            }
        }, CancellationToken.None);
    }
}
