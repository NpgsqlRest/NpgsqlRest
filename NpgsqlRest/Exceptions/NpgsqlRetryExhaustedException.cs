namespace NpgsqlRest;

public class NpgsqlRetryExhaustedException(int totalAttempts, Exception[] attemptExceptions, string message)
    : Exception(message, attemptExceptions?.Length > 0 ? attemptExceptions[^1] : null)
{
    public int TotalAttempts { get; } = totalAttempts;
    public Exception[] AttemptExceptions { get; } = attemptExceptions ?? [];
}
