namespace NpgsqlRest;

public class ConnectionRetryOptions
{
    public bool Enabled { get; set; } = true;
    
    public RetryStrategy Strategy { get; set; } = new()
    {
        RetrySequenceSeconds = [1, 3, 6, 12],
        ErrorCodes = [
            "08000", "08003", "08006", "08001", "08004", // Connection failure codes
            "55P03", // Lock not available
            "55006", // Object in use
            "53300", // Too many connections
            "57P03", // Cannot connect now
            "40001", // Serialization failure (can be retried)
        ]
    };
}