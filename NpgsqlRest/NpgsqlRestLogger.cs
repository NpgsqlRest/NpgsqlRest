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

    private static void LogInformation(string? where, string message)
    {
        if (Logger?.IsEnabled(LogLevel.Information) is true)
        {
            __LogInformationCallback(Logger, where, message, null);
        }
    }

    private static void LogWarning(string? where, string message)
    {
        if (Logger?.IsEnabled(LogLevel.Warning) is true)
        {
            __LogWarningCallback(Logger, where, message, null);
        }
    }

    private static void LogTrace(string? where, string message)
    {
        if (Logger?.IsEnabled(LogLevel.Trace) is true)
        {
            __LogTraceCallback(Logger, where, message, null);
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
    public static void LogConnectionNotice(PostgresNotice notice, PostgresConnectionNoticeLoggingMode mode)
    {
        if (Logger is null)
        {
            return;
        }
        if (notice.IsInfo() || notice.IsNotice())
        {
            if (mode == PostgresConnectionNoticeLoggingMode.MessageOnly)
            {
                Logger.LogInformation(notice.MessageText);
            }
            else if (mode == PostgresConnectionNoticeLoggingMode.FirstStackFrameAndMessage)
            {
                LogInformation(notice?.Where?.Split('\n').LastOrDefault() ?? "", notice?.MessageText!);
            }
            else if (mode == PostgresConnectionNoticeLoggingMode.FullStackAndMessage)
            {
                LogInformation(notice?.Where, notice?.MessageText!);
            }
        }
        else if (notice.IsWarning())
        {
            if (mode == PostgresConnectionNoticeLoggingMode.MessageOnly)
            {
                Logger.LogWarning(notice.MessageText);
            }
            else if (mode == PostgresConnectionNoticeLoggingMode.FirstStackFrameAndMessage)
            {
                LogWarning(notice?.Where?.Split('\n').Last() ?? "", notice?.MessageText!);
            }
            else if (mode == PostgresConnectionNoticeLoggingMode.FullStackAndMessage)
            {
                LogWarning(notice?.Where, notice?.MessageText!);
            }
        }
        else
        {
            if (mode == PostgresConnectionNoticeLoggingMode.MessageOnly)
            {
                Logger.LogTrace(notice.MessageText);
            }
            else if (mode == PostgresConnectionNoticeLoggingMode.FirstStackFrameAndMessage)
            {
                LogTrace(notice?.Where?.Split('\n').Last() ?? "", notice?.MessageText!);
            }
            else if (mode == PostgresConnectionNoticeLoggingMode.FullStackAndMessage)
            {
                LogTrace(notice?.Where, notice?.MessageText!);
            }
        }
    }
}