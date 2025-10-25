using System.Text;
using Npgsql;

namespace NpgsqlRest;

public static class NpgsqlConnectionRetryExtensions
{
    public static void OpenRetry(this NpgsqlConnection connection, ConnectionRetryOptions settings, ILogger? logger = null)
    {
        if (connection.State != System.Data.ConnectionState.Closed)
        {
            connection.Close();
        }
        if (!settings.Enabled)
        {
            connection.Open();
            return;
        }
        var maxRetries = settings.Strategy.RetrySequenceSeconds.Length;
        var exceptionsEncountered = new List<Exception>(maxRetries);
        
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                connection.Open();
                return;
            }
            catch (Exception ex) when (ShouldRetryOn(ex, settings))
            {
                exceptionsEncountered.Add(ex);

                if (attempt < maxRetries)
                {
                    var delaySec = settings.Strategy.RetrySequenceSeconds[exceptionsEncountered.Count - 1];
                    var message = BuildExceptionMessage(ex);
                    if (delaySec > 0)
                    {
                        var delay = TimeSpan.FromSeconds(delaySec);
                        (Logger ?? logger)?.FailedToOpenConnectionRetry(attempt + 1, delay.TotalMilliseconds, message);
                        Thread.Sleep(delay);
                    }
                    else
                    {
                        (Logger ?? logger)?.FailedToOpenConnectionRetry(attempt + 1, 0, message);
                    }
                }
                else
                {
                    (Logger ?? logger)?.FailedToOpenConnectionAfter(ex, attempt + 1);
                    ThrowRetryExhaustedException(exceptionsEncountered);
                }
            }
            catch (Exception ex)
            {
                // Non-retryable exception
                (Logger ?? logger)?.FailedToOpenNonRetryableConnection(ex, ex.Message);
                throw;
            }
        }
    }

    public static async Task OpenRetryAsync(
        this NpgsqlConnection connection,
        ConnectionRetryOptions settings,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        if (connection.State != System.Data.ConnectionState.Closed)
        {
            await connection.CloseAsync();
        }
        if (!settings.Enabled)
        {
            await connection.OpenAsync(cancellationToken);
            return;
        }
        var maxRetries = settings.Strategy.RetrySequenceSeconds.Length;
        var exceptionsEncountered = new List<Exception>(maxRetries);

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await connection.OpenAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (ShouldRetryOn(ex, settings) && !cancellationToken.IsCancellationRequested)
            {
                exceptionsEncountered.Add(ex);

                if (attempt < maxRetries)
                {
                    var delaySec = settings.Strategy.RetrySequenceSeconds[exceptionsEncountered.Count - 1];
                    var message = BuildExceptionMessage(ex);
                    if (delaySec > 0)
                    {
                        var delay = TimeSpan.FromSeconds(delaySec);
                        (Logger ?? logger)?.FailedToOpenConnectionRetry(attempt + 1, delay.TotalMilliseconds, message);
                        await Task.Delay(delay, cancellationToken);
                    }
                    else
                    {
                        (Logger ?? logger)?.FailedToOpenConnectionRetry(attempt + 1, 0, message);
                    }
                }
                else
                {
                    (Logger ?? logger)?.FailedToOpenConnectionAfter(ex, attempt + 1);
                    ThrowRetryExhaustedException(exceptionsEncountered);
                }
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                (Logger ?? logger)?.FailedToOpenNonRetryableConnection(ex, ex.Message);
                throw;
            }
        }
    }
    
    public static string BuildExceptionMessage(this Exception exception)
    {
        StringBuilder sb = new();
        sb.Append(exception.Message);
        if (exception is NpgsqlException npgsqlException)
        {
            if (npgsqlException.SqlState is not null)
            {
                sb.Append(" (SQL State: ");
                sb.Append(npgsqlException.SqlState);
                sb.Append(')');
            }
            if (npgsqlException.IsTransient)
            {
                sb.Append(" (transient)");
            }
        }
        if (exception.InnerException is not null)
        {
            sb.Append(" ---> ");
            sb.Append(BuildExceptionMessage(exception.InnerException));
        }
        return sb.ToString();
    }

    private static bool ShouldRetryOn(Exception exception, ConnectionRetryOptions settings)
    {
        if (exception is NpgsqlException npgsqlException)
        {
            if (npgsqlException.IsTransient)
            {
                return true;
            }
            if (npgsqlException.SqlState is null)
            {
                return true;
            }
            if (settings.Strategy.ErrorCodes.Contains(npgsqlException.SqlState) == true)
            {
                return true;
            }
            return false;
        }

        // Handle other exception types (matching EF Core pattern)
        return exception switch
        {
            TimeoutException => true,
            System.Net.Sockets.SocketException => true,
            System.Net.NetworkInformation.NetworkInformationException => true,
            TaskCanceledException => false, // Don't retry cancellation
            OperationCanceledException => false, // Don't retry cancellation
            _ => false
        };
    }
    
    private static void ThrowRetryExhaustedException(List<Exception> exceptionsEncountered)
    {
        throw new NpgsqlRetryExhaustedException(
            exceptionsEncountered.Count,
            exceptionsEncountered.ToArray(),
            $"Failed to open PostgreSQL connection after {exceptionsEncountered.Count} attempts. See inner exception for details.");
    }
}

