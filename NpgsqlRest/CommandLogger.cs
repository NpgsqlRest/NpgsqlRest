using Npgsql;

namespace NpgsqlRest;

/// <summary>
/// Helper class for logging SQL command executions at Trace level.
/// Use this when a command is about to be executed.
/// For configuration logging (endpoints, SQL command templates), use regular Logger.LogDebug.
/// </summary>
public static class CommandLogger
{
    /// <summary>
    /// Logs a command execution at Trace level. Call this immediately before executing a command.
    /// </summary>
    /// <param name="command">The NpgsqlCommand to log.</param>
    /// <param name="logger">The logger to use.</param>
    /// <param name="name">The name/context of the operation (e.g., "PasskeyAuth.Login", "RoutineSource").</param>
    public static void LogCommand(NpgsqlCommand command, ILogger? logger, string name)
    {
        if (logger?.IsEnabled(LogLevel.Trace) != true)
        {
            return;
        }

        var sb = StringBuilderPool.Rent();
        try
        {
            for (int i = 0; i < command.Parameters.Count; i++)
            {
                sb.Append('$');
                sb.Append(i + 1);
                sb.Append('=');
                sb.AppendLine(PgConverters.SerializeDatbaseObject(command.Parameters[i].Value));
            }

            sb.Append(command.CommandText);
            logger.LogTrace("{name}:\n{query}", name, sb.ToString());
        }
        finally
        {
            StringBuilderPool.Return(sb);
        }
    }

    /// <summary>
    /// Extension method for NpgsqlCommand to log command execution at Trace level.
    /// Uses the static Logger from NpgsqlRestOptions.
    /// </summary>
    /// <param name="command">The NpgsqlCommand to log.</param>
    /// <param name="name">The name/context of the operation.</param>
    public static void LogCommand(this NpgsqlCommand command, string name)
    {
        LogCommand(command, NpgsqlRestOptions.Logger, name);
    }
}
