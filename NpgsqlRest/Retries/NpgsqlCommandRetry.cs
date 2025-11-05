using System.Data;
using System.Net.Sockets;
using System.Text;
using Npgsql;

namespace NpgsqlRest;

public static class NpgsqlRetryExtensions
{
    public static void ExecuteNonQueryWithRetry(
        this NpgsqlCommand command, 
        RetryStrategy? strategy,
        ILogger? logger = null)
    {
        if (strategy == null || strategy.RetrySequenceSeconds.Length == 0)
        {
            command.ExecuteNonQuery();
            return;
        }
        var maxRetries = strategy.RetrySequenceSeconds.Length;
        var exceptionsEncountered = new List<Exception>(maxRetries);
        
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                command.ExecuteNonQuery();
                return;
            }
            catch (Exception ex) when (ShouldRetryOn(ex, strategy))
            {
                exceptionsEncountered.Add(ex);

                if (attempt < maxRetries)
                {
                    var delaySec = strategy.RetrySequenceSeconds[exceptionsEncountered.Count - 1];
                    if (delaySec > 0)
                    {
                        var delay = TimeSpan.FromSeconds(delaySec);
                        Thread.Sleep(delay);
                    }
                }
                else
                {
                    (Logger ?? logger)?.FailedToExecuteCommandAfter(command.CommandText, attempt + 1);
                    ThrowRetryExhaustedException(exceptionsEncountered);
                }
            }
            catch (Exception)
            {
                (Logger ?? logger)?.FailedToExecuteNonRetryableCommand(command.CommandText);
                throw;
            }
        }
    }

    public static async Task ExecuteNonQueryWithRetryAsync(
        this NpgsqlCommand command, 
        RetryStrategy? strategy,
        CancellationToken cancellationToken = default,
        ILogger? logger = null,
        string? errorCodePolicy = null)
    {
        if (strategy == null || strategy.RetrySequenceSeconds.Length == 0)
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return;
        }
        var maxRetries = strategy.RetrySequenceSeconds.Length;
        var exceptionsEncountered = new List<Exception>(maxRetries);
        
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
                return;
            }
            catch (PostgresException ex) when (errorCodePolicy is not null && errorCodePolicy.TryGetErrorCodeMapping(ex.SqlState, out var statusCodeMapping))
            {
                throw new NpgsqlToHttpException(statusCodeMapping, ex);
            }
            catch (Exception ex) when (ShouldRetryOn(ex, strategy) && !cancellationToken.IsCancellationRequested)
            {
                exceptionsEncountered.Add(ex);
                if (attempt < maxRetries)
                {
                    var delaySec = strategy.RetrySequenceSeconds[exceptionsEncountered.Count - 1];
                    if (delaySec > 0)
                    {
                        var delay = TimeSpan.FromSeconds(delaySec);
                        await Task.Delay(delay, cancellationToken);
                    }
                }
                else
                {
                    (Logger ?? logger)?.FailedToExecuteCommandAfter(command.CommandText, attempt + 1);
                    ThrowRetryExhaustedException(exceptionsEncountered);
                }
            }
            catch (Exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                (Logger ?? logger)?.FailedToExecuteNonRetryableCommand(command.CommandText);
                throw;
            }
        }
    }
    
    public static NpgsqlDataReader ExecuteReaderWithRetry(
        this NpgsqlCommand command, 
        RetryStrategy? strategy,
        ILogger? logger = null)
    {
        if (strategy == null || strategy.RetrySequenceSeconds.Length == 0)
        {
            return command.ExecuteReader();
        }
        var maxRetries = strategy.RetrySequenceSeconds.Length;
        var exceptionsEncountered = new List<Exception>(maxRetries);
        
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                return command.ExecuteReader();
            }
            catch (Exception ex) when (ShouldRetryOn(ex, strategy))
            {
                exceptionsEncountered.Add(ex);
                if (attempt < maxRetries)
                {
                    var delaySec = strategy.RetrySequenceSeconds[exceptionsEncountered.Count - 1];
                    var message = ex.BuildExceptionMessage();
                    if (delaySec > 0)
                    {
                        var delay = TimeSpan.FromSeconds(delaySec);
                        (Logger ?? logger)?.FailedToExecuteCommandRetry(attempt + 1, delay.TotalMilliseconds, message);
                        Thread.Sleep(delay);
                    }
                    else
                    {
                        (Logger ?? logger)?.FailedToExecuteCommandRetry(attempt + 1, 0, message);
                    }
                }
                else
                {
                    (Logger ?? logger)?.FailedToExecuteCommandAfter(command.CommandText, attempt + 1);
                    ThrowRetryExhaustedException(exceptionsEncountered);
                }
            }
            catch (Exception)
            {
                (Logger ?? logger)?.FailedToExecuteNonRetryableCommand(command.CommandText);
                throw;
            }
        }
        throw new InvalidOperationException("This should never be reached");
    }
    
    public static async Task<NpgsqlDataReader> ExecuteReaderWithRetryAsync(
        this NpgsqlCommand command, 
        RetryStrategy? strategy, 
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        if (strategy == null || strategy.RetrySequenceSeconds.Length == 0)
        {
            return await command.ExecuteReaderAsync(cancellationToken);
        }
        var maxRetries = strategy.RetrySequenceSeconds.Length;
        var exceptionsEncountered = new List<Exception>(maxRetries);
        
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await command.ExecuteReaderAsync(cancellationToken);
            }
            catch (Exception ex) when (ShouldRetryOn(ex, strategy) && !cancellationToken.IsCancellationRequested)
            {
                exceptionsEncountered.Add(ex);
                if (attempt < maxRetries)
                {
                    var delaySec = strategy.RetrySequenceSeconds[exceptionsEncountered.Count - 1];
                    if (delaySec > 0)
                    {
                        var delay = TimeSpan.FromSeconds(delaySec);
                        await Task.Delay(delay, cancellationToken);
                    }
                }
                else
                {
                    (Logger ?? logger)?.FailedToExecuteCommandAfter(command.CommandText, attempt + 1);
                    ThrowRetryExhaustedException(exceptionsEncountered);
                }
            }
            catch (Exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                (Logger ?? logger)?.FailedToExecuteNonRetryableCommand(command.CommandText);
                throw;
            }
        }
        throw new InvalidOperationException("This should never be reached");
    }
    
    public static async Task<NpgsqlDataReader> ExecuteReaderWithRetryAsync(
        this NpgsqlCommand command,
        CommandBehavior behavior, 
        RetryStrategy? strategy, 
        CancellationToken cancellationToken = default,
        ILogger? logger = null,
        string? errorCodePolicy = null)
    {
        if (strategy == null || strategy.RetrySequenceSeconds.Length == 0)
        {
            return await command.ExecuteReaderAsync(behavior, cancellationToken);
        }
        var maxRetries = strategy.RetrySequenceSeconds.Length;
        var exceptionsEncountered = new List<Exception>(maxRetries);
        
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await command.ExecuteReaderAsync(behavior, cancellationToken);
            }
            catch (PostgresException ex) when (errorCodePolicy is not null && errorCodePolicy.TryGetErrorCodeMapping(ex.SqlState, out var statusCodeMapping))
            {
                throw new NpgsqlToHttpException(statusCodeMapping, ex);
            }
            catch (Exception ex) when (ShouldRetryOn(ex, strategy) && !cancellationToken.IsCancellationRequested)
            {
                exceptionsEncountered.Add(ex);

                if (attempt < maxRetries)
                {
                    var delaySec = strategy.RetrySequenceSeconds[exceptionsEncountered.Count - 1];
                    if (delaySec > 0)
                    {
                        var delay = TimeSpan.FromSeconds(delaySec);
                        await Task.Delay(delay, cancellationToken);
                    }
                }
                else
                {
                    (Logger ?? logger)?.FailedToExecuteCommandAfter(command.CommandText, attempt + 1);
                    ThrowRetryExhaustedException(exceptionsEncountered);
                }
            }
            catch (Exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                (Logger ?? logger)?.FailedToExecuteNonRetryableCommand(command.CommandText);
                throw;
            }
        }
        
        throw new InvalidOperationException("This should never be reached");
    }
    
    public static async Task<object?> ExecuteScalarWithRetryAsync(
        this NpgsqlCommand command, 
        RetryStrategy? strategy, 
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        if (strategy == null || strategy.RetrySequenceSeconds.Length == 0)
        {
            return await command.ExecuteScalarAsync(cancellationToken);
        }
        var maxRetries = strategy.RetrySequenceSeconds.Length;
        var exceptionsEncountered = new List<Exception>(maxRetries);
        
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await command.ExecuteScalarAsync(cancellationToken);
            }
            catch (Exception ex) when (ShouldRetryOn(ex, strategy) && !cancellationToken.IsCancellationRequested)
            {
                exceptionsEncountered.Add(ex);
                if (attempt < maxRetries)
                {
                    var delaySec = strategy.RetrySequenceSeconds[exceptionsEncountered.Count - 1];
                    if (delaySec > 0)
                    {
                        var delay = TimeSpan.FromSeconds(delaySec);
                        await Task.Delay(delay, cancellationToken);
                    }
                }
                else
                {
                    (Logger ?? logger)?.FailedToExecuteCommandAfter(command.CommandText, attempt + 1);
                    ThrowRetryExhaustedException(exceptionsEncountered);
                }
            }
            catch (Exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                (Logger ?? logger)?.FailedToExecuteNonRetryableCommand(command.CommandText);
                throw;
            }
        }
        
        throw new InvalidOperationException("This should never be reached");
    }
    
    public static async Task ExecuteBatchWithRetryAsync(
        this NpgsqlBatch batch, 
        RetryStrategy? strategy, 
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        if (strategy == null || strategy.RetrySequenceSeconds.Length == 0)
        {
            await batch.ExecuteNonQueryAsync(cancellationToken);
            return;
        }
        var maxRetries = strategy.RetrySequenceSeconds.Length;
        var exceptionsEncountered = new List<Exception>(maxRetries);
        
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                await batch.ExecuteNonQueryAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (ShouldRetryOn(ex, strategy) && !cancellationToken.IsCancellationRequested)
            {
                exceptionsEncountered.Add(ex);
                if (attempt < maxRetries)
                {
                    var delaySec = strategy.RetrySequenceSeconds[exceptionsEncountered.Count - 1];
                    if (delaySec > 0)
                    {
                        var delay = TimeSpan.FromSeconds(delaySec);
                        await Task.Delay(delay, cancellationToken);
                    }
                }
                else
                {
                    var cmdTexts = new StringBuilder();
                    foreach (var command in batch.BatchCommands)
                    {
                        cmdTexts.AppendLine(string.Concat(command.CommandText, ";") );
                    }
                    (Logger ?? logger)?.FailedToExecuteCommandAfter(cmdTexts.ToString(), attempt + 1);
                    ThrowRetryExhaustedException(exceptionsEncountered);
                }
            }
            catch (Exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                var cmdTexts = new StringBuilder();
                foreach (var command in batch.BatchCommands)
                {
                    cmdTexts.AppendLine(string.Concat(command.CommandText, ";") );
                }
                (Logger ?? logger)?.FailedToExecuteNonRetryableCommand(cmdTexts.ToString());
                throw;
            }
        }
        
        throw new InvalidOperationException("This should never be reached");
    }

    private static bool ShouldRetryOn(Exception exception, RetryStrategy strategy)
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
            if (strategy.ErrorCodes.Contains(npgsqlException.SqlState) == true)
            {
                return true;
            }
            return false;
        }

        return exception switch
        {
            TimeoutException => true,
            SocketException => true,
            System.Net.NetworkInformation.NetworkInformationException => true,
            TaskCanceledException => false,
            OperationCanceledException => false,
            _ => false
        };
    }
    
    private static void ThrowRetryExhaustedException(List<Exception> exceptionsEncountered)
    {
        throw new NpgsqlRetryExhaustedException(
            exceptionsEncountered.Count,
            exceptionsEncountered.ToArray(),
            $"Failed to execute command after {exceptionsEncountered.Count} attempts. See inner exception for details.");
    }
}