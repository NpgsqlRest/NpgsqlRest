namespace NpgsqlRest;

/// <summary>
/// A SQL command executed before the main routine call, after any context (request headers, claims, IP) is set.
/// Commands run in the same batch as the context set_config calls, so they share a network round-trip.
/// Useful for setting per-request GUCs derived from claims (e.g. multi-tenant search_path) or for any other pre-routine setup.
/// </summary>
public class BeforeRoutineCommand
{
    /// <summary>
    /// SQL text to execute. May contain positional parameters ($1, $2, ...) bound from the Parameters list at request time.
    /// </summary>
    public string Sql { get; set; } = "";

    /// <summary>
    /// Optional list of parameter sources. Resolved at request time from claims, request headers, or the client IP.
    /// </summary>
    public BeforeRoutineCommandParameter[] Parameters { get; set; } = [];

    /// <summary>
    /// Implicit conversion from a plain SQL string for ergonomic configuration of parameterless commands.
    /// Lets callers write <c>"select set_config(...)"</c> directly inside the BeforeRoutineCommands array.
    /// </summary>
    public static implicit operator BeforeRoutineCommand(string sql) => new() { Sql = sql, Parameters = [] };
}
