using Npgsql;

namespace NpgsqlRest;

public static class NpgsqlRestLogger
{
    private const string LogPattern = "{where}:\n{message}";

    internal static readonly LogDefineOptions LogDefineOptions = new() { SkipEnabledCheck = true };

    private static readonly Action<ILogger, string?, string, Exception?> __LogInformationCallback =
        LoggerMessage.Define<string?, string>(LogLevel.Information, 0, LogPattern, LogDefineOptions);

    private static readonly Action<ILogger, string?, string, Exception?> __LogWarningCallback =
        LoggerMessage.Define<string?, string>(LogLevel.Warning, 1, LogPattern, LogDefineOptions);

    private static readonly Action<ILogger, string?, string, Exception?> __LogTraceCallback =
        LoggerMessage.Define<string?, string>(LogLevel.Trace, 4, LogPattern, LogDefineOptions);

    private static void LogInformation(ILogger logger, string? where, string message)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            __LogInformationCallback(logger, where, message, null);
        }
    }

    private static void LogWarning(ILogger logger, string? where, string message)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            __LogWarningCallback(logger, where, message, null);
        }
    }

    private static void LogTrace(ILogger logger, string? where, string message)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            __LogTraceCallback(logger, where, message, null);
        }
    }

    public static void LogEndpoint(RoutineEndpoint endpoint, string parameters, string command)
    {
        if (Logger?.IsEnabled(LogLevel.Debug) is true && endpoint.LogCallback is not null)
        {
            endpoint.LogCallback(Logger, parameters, command, null);
        }
    }

#pragma warning disable IDE0079 // Remove unnecessary suppression
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2254:Template should be a static expression", Justification = "<Pending>")]
#pragma warning restore IDE0079 // Remove unnecessary suppression
    public static void LogConnectionNotice(PostgresNotice notice, PostgresConnectionNoticeLoggingMode mode, string? context = null)
    {
        if (Logger is null)
        {
            return;
        }
        LogConnectionNotice(Logger, notice, mode, context);
    }

    /// <summary>
    /// Logs a connection notice through the supplied <paramref name="logger"/> rather than the static
    /// <see cref="Logger"/>. Used by the SQL test runner so notices route through its own log channel
    /// (e.g. "NpgsqlRestTest"). The log level still follows the notice severity (info/notice => Information,
    /// warning => Warning, otherwise Trace/Verbose) and the format is identical to the normal-app path.
    /// </summary>
#pragma warning disable IDE0079 // Remove unnecessary suppression
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2254:Template should be a static expression", Justification = "<Pending>")]
#pragma warning restore IDE0079 // Remove unnecessary suppression
    public static void LogConnectionNotice(ILogger logger, PostgresNotice notice, PostgresConnectionNoticeLoggingMode mode, string? context = null)
    {
        if (logger is null)
        {
            return;
        }
        // Optional caller context (e.g. the SQL test runner's current test file) prefixed to the location,
        // so notices can be attributed to their source. Null in normal operation → format unchanged.
        string? tag = string.IsNullOrEmpty(context) ? null : $"[{context}] ";
        if (notice.IsInfo() || notice.IsNotice())
        {
            if (mode == PostgresConnectionNoticeLoggingMode.MessageOnly)
            {
                logger.LogInformation(string.Concat(tag, notice.MessageText));
            }
            else if (mode == PostgresConnectionNoticeLoggingMode.FirstStackFrameAndMessage)
            {
                LogInformation(logger, string.Concat(tag, notice?.Where?.Split('\n').LastOrDefault() ?? ""), notice?.MessageText!);
            }
            else if (mode == PostgresConnectionNoticeLoggingMode.FullStackAndMessage)
            {
                LogInformation(logger, string.Concat(tag, notice?.Where), notice?.MessageText!);
            }
        }
        else if (notice.IsWarning())
        {
            if (mode == PostgresConnectionNoticeLoggingMode.MessageOnly)
            {
                logger.LogWarning(string.Concat(tag, notice.MessageText));
            }
            else if (mode == PostgresConnectionNoticeLoggingMode.FirstStackFrameAndMessage)
            {
                LogWarning(logger, string.Concat(tag, notice?.Where?.Split('\n').Last() ?? ""), notice?.MessageText!);
            }
            else if (mode == PostgresConnectionNoticeLoggingMode.FullStackAndMessage)
            {
                LogWarning(logger, string.Concat(tag, notice?.Where), notice?.MessageText!);
            }
        }
        else
        {
            if (mode == PostgresConnectionNoticeLoggingMode.MessageOnly)
            {
                logger.LogTrace(string.Concat(tag, notice.MessageText));
            }
            else if (mode == PostgresConnectionNoticeLoggingMode.FirstStackFrameAndMessage)
            {
                LogTrace(logger, string.Concat(tag, notice?.Where?.Split('\n').Last() ?? ""), notice?.MessageText!);
            }
            else if (mode == PostgresConnectionNoticeLoggingMode.FullStackAndMessage)
            {
                LogTrace(logger, string.Concat(tag, notice?.Where), notice?.MessageText!);
            }
        }
    }
}