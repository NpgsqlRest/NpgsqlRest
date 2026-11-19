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
        List<Exception>? exceptionsEncountered = null; // allocated on the first failure only
        
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                command.ExecuteNonQuery();
                return;
            }
            catch (Exception ex) when (ShouldRetryOn(ex, strategy))
            {
                (exceptionsEncountered ??= new(maxRetries)).Add(ex);

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

    public static Task<int> ExecuteNonQueryWithRetryAsync(
        this NpgsqlCommand command,
        RetryStrategy? strategy,
        CancellationToken cancellationToken = default,
        ILogger? logger = null,
        string? errorCodePolicy = null)
    {
        if (strategy == null || strategy.RetrySequenceSeconds.Length == 0)
        {
            return command.ExecuteNonQueryAsync(cancellationToken);
        }
        return ExecuteNonQueryRetryLoopAsync(command, strategy, cancellationToken, logger, errorCodePolicy, firstFailure: null);
    }

    /// <summary>
    /// For a caller that made the first attempt itself (so that no extra async method waits on the database with it):
    /// continues after that attempt failed, handling the failure exactly as the retrying method handles one - error
    /// code mapping, timeout mapping, retries with their delays, logging.
    /// </summary>
    internal static Task<int> ExecuteNonQueryAfterFailureAsync(
        this NpgsqlCommand command,
        Exception firstFailure,
        RetryStrategy strategy,
        CancellationToken cancellationToken,
        string? errorCodePolicy)
        => ExecuteNonQueryRetryLoopAsync(command, strategy, cancellationToken, null, errorCodePolicy, firstFailure);

    private static async Task<int> ExecuteNonQueryRetryLoopAsync(
        NpgsqlCommand command,
        RetryStrategy strategy,
        CancellationToken cancellationToken,
        ILogger? logger,
        string? errorCodePolicy,
        Exception? firstFailure)
    {
        var maxRetries = strategy.RetrySequenceSeconds.Length;
        List<Exception>? exceptionsEncountered = null; // allocated on the first failure only

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                if (attempt == 0 && firstFailure is not null)
                {
                    // The caller already made this attempt: its failure goes through the handlers below.
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(firstFailure);
                }
                return await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (PostgresException ex) when (errorCodePolicy is not null && errorCodePolicy.TryGetErrorCodeMapping(ex.SqlState, out var statusCodeMapping))
            {
                throw new NpgsqlToHttpException(statusCodeMapping, ex);
            }
            catch (NpgsqlException ex) when (ex.InnerException is TimeoutException)
            {
                if (Options.ErrorHandlingOptions.TimeoutErrorMapping is not null)
                {
                    throw new NpgsqlToHttpException(Options.ErrorHandlingOptions.TimeoutErrorMapping, ex);
                }
            }
            catch (Exception ex) when (ShouldRetryOn(ex, strategy) && !cancellationToken.IsCancellationRequested)
            {
                (exceptionsEncountered ??= new(maxRetries)).Add(ex);
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
        return 0; // unreachable — loop always returns or throws
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
        List<Exception>? exceptionsEncountered = null; // allocated on the first failure only
        
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                return command.ExecuteReader();
            }
            catch (Exception ex) when (ShouldRetryOn(ex, strategy))
            {
                (exceptionsEncountered ??= new(maxRetries)).Add(ex);
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
        ILogger? logger = null,
        string? errorCodePolicy = null)
    {
        if (strategy == null || strategy.RetrySequenceSeconds.Length == 0)
        {
            return await command.ExecuteReaderAsync(cancellationToken);
        }
        var maxRetries = strategy.RetrySequenceSeconds.Length;
        List<Exception>? exceptionsEncountered = null; // allocated on the first failure only
        
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await command.ExecuteReaderAsync(cancellationToken);
            }
            catch (PostgresException ex) when (errorCodePolicy is not null && errorCodePolicy.TryGetErrorCodeMapping(ex.SqlState, out var statusCodeMapping))
            {
                throw new NpgsqlToHttpException(statusCodeMapping, ex);
            }
            catch (NpgsqlException ex) when (ex.InnerException is TimeoutException)
            {
                if (Options.ErrorHandlingOptions.TimeoutErrorMapping is not null)
                {
                    throw new NpgsqlToHttpException(Options.ErrorHandlingOptions.TimeoutErrorMapping, ex);
                }
            }
            catch (Exception ex) when (ShouldRetryOn(ex, strategy) && !cancellationToken.IsCancellationRequested)
            {
                (exceptionsEncountered ??= new(maxRetries)).Add(ex);
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
    
    public static Task<NpgsqlDataReader> ExecuteReaderWithRetryAsync(
        this NpgsqlCommand command,
        CommandBehavior behavior, 
        RetryStrategy? strategy, 
        CancellationToken cancellationToken = default,
        ILogger? logger = null,
        string? errorCodePolicy = null)
    {
        if (strategy == null || strategy.RetrySequenceSeconds.Length == 0)
        {
            return command.ExecuteReaderAsync(behavior, cancellationToken);
        }
        return ExecuteReaderRetryLoopAsync(command, behavior, strategy, cancellationToken, logger, errorCodePolicy, firstFailure: null);
    }

    /// <summary>
    /// For a caller that made the first attempt itself (so that no extra async method waits on the database with it):
    /// continues after that attempt failed, handling the failure exactly as the retrying method handles one - error
    /// code mapping, timeout mapping, retries with their delays, logging.
    /// </summary>
    internal static Task<NpgsqlDataReader> ExecuteReaderAfterFailureAsync(
        this NpgsqlCommand command,
        Exception firstFailure,
        CommandBehavior behavior,
        RetryStrategy strategy,
        CancellationToken cancellationToken,
        string? errorCodePolicy)
        => ExecuteReaderRetryLoopAsync(command, behavior, strategy, cancellationToken, null, errorCodePolicy, firstFailure);

    private static async Task<NpgsqlDataReader> ExecuteReaderRetryLoopAsync(
        NpgsqlCommand command,
        CommandBehavior behavior,
        RetryStrategy strategy,
        CancellationToken cancellationToken,
        ILogger? logger,
        string? errorCodePolicy,
        Exception? firstFailure)
    {
        var maxRetries = strategy.RetrySequenceSeconds.Length;
        List<Exception>? exceptionsEncountered = null; // allocated on the first failure only
        
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (attempt > 0 || firstFailure is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            try
            {
                if (attempt == 0 && firstFailure is not null)
                {
                    // The caller already made this attempt: its failure goes through the handlers below.
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(firstFailure);
                }
                return await command.ExecuteReaderAsync(behavior, cancellationToken);
            }
            catch (PostgresException ex) when (errorCodePolicy is not null && errorCodePolicy.TryGetErrorCodeMapping(ex.SqlState, out var statusCodeMapping))
            {
                throw new NpgsqlToHttpException(statusCodeMapping, ex);
            }
            catch (NpgsqlException ex) when (ex.InnerException is TimeoutException)
            {
                if (Options.ErrorHandlingOptions.TimeoutErrorMapping is not null)
                {
                    throw new NpgsqlToHttpException(Options.ErrorHandlingOptions.TimeoutErrorMapping, ex);
                }
            }
            catch (Exception ex) when (ShouldRetryOn(ex, strategy) && !cancellationToken.IsCancellationRequested)
            {
                (exceptionsEncountered ??= new(maxRetries)).Add(ex);

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
        ILogger? logger = null,
        string? errorCodePolicy = null)
    {
        if (strategy == null || strategy.RetrySequenceSeconds.Length == 0)
        {
            return await command.ExecuteScalarAsync(cancellationToken);
        }
        var maxRetries = strategy.RetrySequenceSeconds.Length;
        List<Exception>? exceptionsEncountered = null; // allocated on the first failure only
        
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await command.ExecuteScalarAsync(cancellationToken);
            }
            catch (PostgresException ex) when (errorCodePolicy is not null && errorCodePolicy.TryGetErrorCodeMapping(ex.SqlState, out var statusCodeMapping))
            {
                throw new NpgsqlToHttpException(statusCodeMapping, ex);
            }
            catch (NpgsqlException ex) when (ex.InnerException is TimeoutException)
            {
                if (Options.ErrorHandlingOptions.TimeoutErrorMapping is not null)
                {
                    throw new NpgsqlToHttpException(Options.ErrorHandlingOptions.TimeoutErrorMapping, ex);
                }
            }
            catch (Exception ex) when (ShouldRetryOn(ex, strategy) && !cancellationToken.IsCancellationRequested)
            {
                (exceptionsEncountered ??= new(maxRetries)).Add(ex);
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
        ILogger? logger = null,
        string? errorCodePolicy = null)
    {
        if (strategy == null || strategy.RetrySequenceSeconds.Length == 0)
        {
            await batch.ExecuteNonQueryAsync(cancellationToken);
            return;
        }
        var maxRetries = strategy.RetrySequenceSeconds.Length;
        List<Exception>? exceptionsEncountered = null; // allocated on the first failure only
        
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                await batch.ExecuteNonQueryAsync(cancellationToken);
                return;
            }
            catch (PostgresException ex) when (errorCodePolicy is not null && errorCodePolicy.TryGetErrorCodeMapping(ex.SqlState, out var statusCodeMapping))
            {
                throw new NpgsqlToHttpException(statusCodeMapping, ex);
            }
            catch (NpgsqlException ex) when (ex.InnerException is TimeoutException)
            {
                if (Options.ErrorHandlingOptions.TimeoutErrorMapping is not null)
                {
                    throw new NpgsqlToHttpException(Options.ErrorHandlingOptions.TimeoutErrorMapping, ex);
                }
            }
            catch (Exception ex) when (ShouldRetryOn(ex, strategy) && !cancellationToken.IsCancellationRequested)
            {
                (exceptionsEncountered ??= new(maxRetries)).Add(ex);
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

    public static async Task<NpgsqlDataReader> ExecuteBatchReaderWithRetryAsync(
        this NpgsqlBatch batch,
        CommandBehavior behavior,
        RetryStrategy? strategy,
        CancellationToken cancellationToken = default,
        ILogger? logger = null,
        string? errorCodePolicy = null)
    {
        if (strategy == null || strategy.RetrySequenceSeconds.Length == 0)
        {
            return await batch.ExecuteReaderAsync(behavior, cancellationToken);
        }
        var maxRetries = strategy.RetrySequenceSeconds.Length;
        List<Exception>? exceptionsEncountered = null; // allocated on the first failure only

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await batch.ExecuteReaderAsync(behavior, cancellationToken);
            }
            catch (PostgresException ex) when (errorCodePolicy is not null && errorCodePolicy.TryGetErrorCodeMapping(ex.SqlState, out var statusCodeMapping))
            {
                throw new NpgsqlToHttpException(statusCodeMapping, ex);
            }
            catch (NpgsqlException ex) when (ex.InnerException is TimeoutException)
            {
                if (Options.ErrorHandlingOptions.TimeoutErrorMapping is not null)
                {
                    throw new NpgsqlToHttpException(Options.ErrorHandlingOptions.TimeoutErrorMapping, ex);
                }
            }
            catch (Exception ex) when (ShouldRetryOn(ex, strategy) && !cancellationToken.IsCancellationRequested)
            {
                (exceptionsEncountered ??= new(maxRetries)).Add(ex);

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
                        cmdTexts.AppendLine(string.Concat(command.CommandText, ";"));
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
                    cmdTexts.AppendLine(string.Concat(command.CommandText, ";"));
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
            if (strategy.ErrorCodes.Contains(npgsqlException.SqlState))
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