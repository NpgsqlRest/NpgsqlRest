using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlRest;

namespace NpgsqlRestClient;

/// <summary>
/// Database change detection for watch mode. There is no filesystem to watch for routine-source
/// endpoints — instead, the poller runs the SAME discovery query the routine source uses (same
/// configured filters — see <see cref="RoutineSource.CreateFingerprintCommand"/>), hashed server-side
/// into one scalar, on a dedicated non-pooled connection. If the hash changes, the discovered
/// endpoints changed — by definition: create/replace/drop/alter of functions and procedures, grants,
/// COMMENT ON (annotations), schema renames, and changes to the composite/table types their signatures
/// use. Anything the query does not read (an unrelated table, temp objects, data) can never trigger.
/// </summary>
public sealed class WatchDbPoller(
    string connectionString,
    TimeSpan interval,
    Action onChange,
    ILogger? logger,
    IReadOnlyList<RoutineSource> sources)
{
    private readonly string _connString = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
    private NpgsqlConnection? _conn;
    private string? _baseline;
    private volatile bool _rebaseline;

    /// <summary>Fingerprint of one source's discovery result on an open connection (also used by tests).</summary>
    public static string? GetFingerprint(NpgsqlConnection connection, RoutineSource source)
    {
        using var cmd = source.CreateFingerprintCommand(connection);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// Forget the baseline so the next tick captures a fresh one WITHOUT firing. Called after work
    /// that legitimately changes the database (a test rerun with committed fixtures, a restart) so
    /// self-inflicted changes never trigger.
    /// </summary>
    public void Rebaseline() => _rebaseline = true;

    /// <summary>Poll loop; runs until cancelled. Connection failures skip the tick and retry.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        if (sources.Count == 0)
        {
            return;
        }
        while (ct.IsCancellationRequested is false)
        {
            try
            {
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            try
            {
                if (_conn is null || _conn.State != System.Data.ConnectionState.Open)
                {
                    _conn?.Dispose();
                    _conn = new NpgsqlConnection(_connString);
                    await _conn.OpenAsync(ct);
                }
                var sb = new StringBuilder();
                foreach (var source in sources)
                {
                    await using var cmd = source.CreateFingerprintCommand(_conn);
                    sb.Append(await cmd.ExecuteScalarAsync(ct) as string).Append('|');
                }
                var current = sb.ToString();
                if (_rebaseline)
                {
                    _rebaseline = false;
                    _baseline = current;
                    continue;
                }
                if (_baseline is null)
                {
                    _baseline = current;
                    continue;
                }
                if (string.Equals(_baseline, current, StringComparison.Ordinal) is false)
                {
                    _baseline = current;
                    onChange();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // DB down / restarting: skip this tick, reconnect on the next one.
                logger?.LogDebug("watch: database poll failed ({Message}) — retrying", ex.Message);
                try { _conn?.Dispose(); } catch { }
                _conn = null;
            }
        }
        try { _conn?.Dispose(); } catch { }
    }
}
