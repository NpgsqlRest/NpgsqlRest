using System.Data;
using System.Net.Sockets;
using Npgsql;

namespace NpgsqlRest;

public static class NpgsqlRetryExtensions
{
    public static void ExecuteNonQueryWithRetry(
        this NpgsqlCommand command, 
        RetryStrategy? strategy, 
        ILogger? logger)
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
                    var message = ex.BuildExceptionMessage();
                    if (delaySec > 0)
                    {
                        var delay = TimeSpan.FromSeconds(delaySec);
                        logger?.FailedToExecuteCommandRetry(attempt + 1, delay.TotalMilliseconds, message);
                        Thread.Sleep(delay);
                    }
                    else
                    {
                        logger?.FailedToExecuteCommandRetry(attempt + 1, 0, message);
                    }
                }
                else
                {
                    logger?.FailedToExecuteCommandAfter(ex, attempt + 1);
                    ThrowRetryExhaustedException(exceptionsEncountered);
                }
            }
            catch (Exception ex)
            {
                logger?.FailedToExecuteNonRetryableCommand(ex, ex.Message);
                throw;
            }
        }
    }

    public static async Task ExecuteNonQueryWithRetryAsync(
        this NpgsqlCommand command, 
        RetryStrategy? strategy, 
        ILogger? logger, 
        CancellationToken cancellationToken = default)
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
            catch (Exception ex) when (ShouldRetryOn(ex, strategy) && !cancellationToken.IsCancellationRequested)
            {
                exceptionsEncountered.Add(ex);
                if (attempt < maxRetries)
                {
                    var delaySec = strategy.RetrySequenceSeconds[exceptionsEncountered.Count - 1];
                    var message = ex.BuildExceptionMessage();
                    if (delaySec > 0)
                    {
                        var delay = TimeSpan.FromSeconds(delaySec);
                        logger?.FailedToExecuteCommandRetry(attempt + 1, delay.TotalMilliseconds, message);
                        await Task.Delay(delay, cancellationToken);
                    }
                    else
                    {
                        logger?.FailedToExecuteCommandRetry(attempt + 1, 0, message);
                    }
                }
                else
                {
                    logger?.FailedToExecuteCommandAfter(ex, attempt + 1);
                    ThrowRetryExhaustedException(exceptionsEncountered);
                }
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                logger?.FailedToExecuteNonRetryableCommand(ex, ex.Message);
                throw;
            }
        }
    }
    
    public static NpgsqlDataReader ExecuteReaderWithRetry(
        this NpgsqlCommand command, 
        RetryStrategy? strategy, 
        ILogger? logger)
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
                        logger?.FailedToExecuteCommandRetry(attempt + 1, delay.TotalMilliseconds, message);
                        Thread.Sleep(delay);
                    }
                    else
                    {
                        logger?.FailedToExecuteCommandRetry(attempt + 1, 0, message);
                    }
                }
                else
                {
                    logger?.FailedToExecuteCommandAfter(ex, attempt + 1);
                    ThrowRetryExhaustedException(exceptionsEncountered);
                }
            }
            catch (Exception ex)
            {
                logger?.FailedToExecuteNonRetryableCommand(ex, ex.Message);
                throw;
            }
        }
        throw new InvalidOperationException("This should never be reached");
    }
    
    public static async Task<NpgsqlDataReader> ExecuteReaderWithRetryAsync(
        this NpgsqlCommand command, 
        RetryStrategy? strategy, ILogger? logger, 
        CancellationToken cancellationToken = default)
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
                    var message = ex.BuildExceptionMessage();
                    if (delaySec > 0)
                    {
                        var delay = TimeSpan.FromSeconds(delaySec);
                        logger?.FailedToExecuteCommandRetry(attempt + 1, delay.TotalMilliseconds, message);
                        await Task.Delay(delay, cancellationToken);
                    }
                    else
                    {
                        logger?.FailedToExecuteCommandRetry(attempt + 1, 0, message);
                    }
                }
                else
                {
                    logger?.FailedToExecuteCommandAfter(ex, attempt + 1);
                    ThrowRetryExhaustedException(exceptionsEncountered);
                }
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                logger?.FailedToExecuteNonRetryableCommand(ex, ex.Message);
                throw;
            }
        }
        throw new InvalidOperationException("This should never be reached");
    }
    
    public static async Task<NpgsqlDataReader> ExecuteReaderWithRetryAsync(
        this NpgsqlCommand command,
        CommandBehavior behavior, 
        RetryStrategy? strategy, 
        ILogger? logger, 
        CancellationToken cancellationToken = default)
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
            catch (Exception ex) when (ShouldRetryOn(ex, strategy) && !cancellationToken.IsCancellationRequested)
            {
                exceptionsEncountered.Add(ex);

                if (attempt < maxRetries)
                {
                    var delaySec = strategy.RetrySequenceSeconds[exceptionsEncountered.Count - 1];
                    var message = ex.BuildExceptionMessage();
                    if (delaySec > 0)
                    {
                        var delay = TimeSpan.FromSeconds(delaySec);
                        logger?.FailedToExecuteCommandRetry(attempt + 1, delay.TotalMilliseconds, message);
                        await Task.Delay(delay, cancellationToken);
                    }
                    else
                    {
                        logger?.FailedToExecuteCommandRetry(attempt + 1, 0, message);
                    }
                }
                else
                {
                    logger?.FailedToExecuteCommandAfter(ex, attempt + 1);
                    ThrowRetryExhaustedException(exceptionsEncountered);
                }
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                logger?.FailedToExecuteNonRetryableCommand(ex, ex.Message);
                throw;
            }
        }
        
        throw new InvalidOperationException("This should never be reached");
    }
    
    public static async Task<object?> ExecuteScalarWithRetryAsync(
        this NpgsqlCommand command, 
        RetryStrategy? strategy, 
        ILogger? logger, 
        CancellationToken cancellationToken = default)
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
                    var message = ex.BuildExceptionMessage();
                    if (delaySec > 0)
                    {
                        var delay = TimeSpan.FromSeconds(delaySec);
                        logger?.FailedToExecuteCommandRetry(attempt + 1, delay.TotalMilliseconds, message);
                        await Task.Delay(delay, cancellationToken);
                    }
                    else
                    {
                        logger?.FailedToExecuteCommandRetry(attempt + 1, 0, message);
                    }
                }
                else
                {
                    logger?.FailedToExecuteCommandAfter(ex, attempt + 1);
                    ThrowRetryExhaustedException(exceptionsEncountered);
                }
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                logger?.FailedToExecuteNonRetryableCommand(ex, ex.Message);
                throw;
            }
        }
        
        throw new InvalidOperationException("This should never be reached");
    }
    
    public static async Task ExecuteBatchWithRetryAsync(
        this NpgsqlBatch batch, 
        RetryStrategy? strategy, 
        ILogger? logger, 
        CancellationToken cancellationToken = default)
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
                    var message = ex.BuildExceptionMessage();
                    if (delaySec > 0)
                    {
                        var delay = TimeSpan.FromSeconds(delaySec);
                        logger?.FailedToExecuteCommandRetry(attempt + 1, delay.TotalMilliseconds, message);
                        await Task.Delay(delay, cancellationToken);
                    }
                    else
                    {
                        logger?.FailedToExecuteCommandRetry(attempt + 1, 0, message);
                    }
                }
                else
                {
                    logger?.FailedToExecuteCommandAfter(ex, attempt + 1);
                    ThrowRetryExhaustedException(exceptionsEncountered);
                }
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                logger?.FailedToExecuteNonRetryableCommand(ex, ex.Message);
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