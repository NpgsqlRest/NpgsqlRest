namespace NpgsqlRest;

/// <summary>
/// Describes how to resolve a single positional parameter for a BeforeRoutineCommand at request time.
/// </summary>
public class BeforeRoutineCommandParameter
{
    /// <summary>
    /// Where the parameter value comes from.
    /// </summary>
    public BeforeRoutineCommandParameterSource Source { get; set; }

    /// <summary>
    /// Source-dependent name. For Claim: claim type. For RequestHeader: header name. For IpAddress: ignored.
    /// </summary>
    public string? Name { get; set; }
}
