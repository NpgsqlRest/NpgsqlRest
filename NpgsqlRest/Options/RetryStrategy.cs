namespace NpgsqlRest;

public class RetryStrategy
{
    public double[] RetrySequenceSeconds { get; set; } = null!;
    public HashSet<string> ErrorCodes { get; set; } = null!;
}
