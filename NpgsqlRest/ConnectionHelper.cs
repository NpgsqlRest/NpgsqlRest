using Npgsql;

namespace NpgsqlRest;

/// <summary>
/// Helper methods for creating and opening database connections.
/// </summary>
public static class ConnectionHelper
{
    /// <summary>
    /// Creates and opens a connection using the standard resolution order:
    /// 1. Named DataSource (if connectionName provided)
    /// 2. Named ConnectionString (if connectionName provided)
    /// 3. Default DataSource
    /// 4. Default ConnectionString
    /// </summary>
    /// <param name="options">NpgsqlRest options containing connection configuration</param>
    /// <param name="connectionName">Optional connection name for named DataSource or ConnectionString lookup</param>
    /// <param name="loggingMode">Mode for logging PostgreSQL NOTICE events</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="logger">Optional logger for retry logging</param>
    /// <returns>An opened NpgsqlConnection</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when connectionName is specified but not found, or when no default connection is configured
    /// </exception>
    public static async Task<NpgsqlConnection> OpenConnectionAsync(
        NpgsqlRestOptions options,
        string? connectionName,
        PostgresConnectionNoticeLoggingMode loggingMode,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        NpgsqlConnection connection;

        if (connectionName is not null)
        {
            // Named DataSource lookup
            if (options.DataSources?.TryGetValue(connectionName, out var namedDataSource) is true)
            {
                connection = namedDataSource.CreateConnection();
            }
            // Named ConnectionString lookup
            else if (options.ConnectionStrings?.TryGetValue(connectionName, out var connString) is true)
            {
                connection = new NpgsqlConnection(connString);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Connection name '{connectionName}' not found in DataSources or ConnectionStrings");
            }
        }
        else
        {
            // Default DataSource
            if (options.DataSource is not null)
            {
                connection = options.DataSource.CreateConnection();
            }
            // Default ConnectionString
            else if (!string.IsNullOrEmpty(options.ConnectionString))
            {
                connection = new NpgsqlConnection(options.ConnectionString);
            }
            else
            {
                throw new InvalidOperationException(
                    "No DataSource or ConnectionString configured");
            }
        }

        // Setup notice logging
        if (options.LogConnectionNoticeEvents)
        {
            connection.Notice += (sender, args) =>
            {
                NpgsqlRestLogger.LogConnectionNotice(args.Notice, loggingMode);
            };
        }

        // Open with retry
        await connection.OpenRetryAsync(options.ConnectionRetryOptions, cancellationToken, logger);

        return connection;
    }
}
