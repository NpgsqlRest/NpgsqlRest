namespace NpgsqlRest;

/// <summary>
/// NpgsqlRest's logger, with one addition: while an endpoint's comment is being parsed, every warning and error
/// logged about it is also kept, and ends up in <see cref="RoutineEndpoint.Diagnostics"/>. That is what lets a
/// tool (npgsqlrest --endpoints, an editor) show an endpoint's problems with the endpoint, instead of having to
/// find them in the log. Everything is passed on to the real logger unchanged.
/// </summary>
internal sealed class EndpointDiagnosticsLogger(ILogger inner) : ILogger
{
    [ThreadStatic]
    private static bool _collecting;
    [ThreadStatic]
    private static List<string>? _collected;

    /// <summary>Starts keeping the warnings logged on this thread.</summary>
    public static void Begin()
    {
        _collecting = true;
        _collected = null;
    }

    /// <summary>Stops, and returns what was kept (null for nothing).</summary>
    public static string[]? End()
    {
        _collecting = false;
        var result = _collected?.ToArray();
        _collected = null;
        return result;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) =>
        inner.IsEnabled(logLevel) || (_collecting && logLevel >= LogLevel.Warning && logLevel != LogLevel.None);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (_collecting && logLevel >= LogLevel.Warning && logLevel != LogLevel.None)
        {
            (_collected ??= []).Add(formatter(state, exception));
        }
        if (inner.IsEnabled(logLevel))
        {
            inner.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
