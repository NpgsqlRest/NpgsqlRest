namespace NpgsqlRest;

/// <summary>
/// Per-endpoint dedupe state for the unbound-RAISE warning. The warning fires once per endpoint per
/// process lifetime — after that the endpoint is in <see cref="_warned"/> and the runtime takes the
/// fast path. <see cref="Reset"/> exists for tests that need to re-run the warning logic against
/// the same endpoint paths.
/// </summary>
internal static class SseUnboundWarner
{
    private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    private static readonly object _lock = new();

    public static bool TryMarkWarned(string endpointPath)
    {
        lock (_lock)
        {
            return _warned.Add(endpointPath);
        }
    }

    internal static void Reset()
    {
        lock (_lock)
        {
            _warned.Clear();
        }
    }
}
