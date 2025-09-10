namespace NpgsqlRest;

public class CommandRetryOptions
{
    /// <summary>
    /// Enable or disable command retry logic for all commands.
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// The name of the default retry strategy to use for commands.
    /// </summary>
    public string DefaultStrategy { get; set; } = "default";
    
    /// <summary>
    /// Available retry strategies that can be referenced by name.
    /// </summary>
    public Dictionary<string, RetryStrategy> Strategies { get; set; } = new()
    {
        ["default"] = new RetryStrategy
        {
            RetrySequenceSeconds = [0, 1, 2, 5, 10],
            ErrorCodes = [
                // Serialization failures (MUST retry for correctness)
                "40001", // serialization_failure 
                "40P01", // deadlock_detected
                // Connection issues (Class 08)
                "08000", // connection_exception
                "08003", // connection_does_not_exist
                "08006", // connection_failure  
                "08001", // sqlclient_unable_to_establish_sqlconnection
                "08004", // sqlserver_rejected_establishment_of_sqlconnection
                "08007", // transaction_resolution_unknown
                "08P01", // protocol_violation
                // Resource constraints (Class 53)
                "53000", // insufficient_resources
                "53100", // disk_full
                "53200", // out_of_memory
                "53300", // too_many_connections
                "53400", // configuration_limit_exceeded
                // System errors (Class 58) 
                "57P01", // admin_shutdown
                "57P02", // crash_shutdown  
                "57P03", // cannot_connect_now
                "58000", // system_error
                "58030", // io_error
                // Lock acquisition issues (Class 55)
                "55P03", // lock_not_available
                "55006", // object_in_use
                "55000", // object_not_in_prerequisite_state
            ]
        }
    };
}
