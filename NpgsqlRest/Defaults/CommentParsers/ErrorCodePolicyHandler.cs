namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: error_code_policy_name | error_code_policy | error_code
    /// Syntax: error_code_policy [name]
    ///
    /// Description: Set the error code policy for this endpoint.
    /// </summary>
    private static readonly string[] ErrorCodePolicyKey = [
        "error_code_policy_name",
        "error_code_policy",
        "error_code",
    ];

    private static void HandleErrorCodePolicy(
        RoutineEndpoint endpoint,
        string[] words,
        string description)
    {
        endpoint.ErrorCodePolicy = string.Join(Consts.Space, words[1..]);
        Logger?.ErrorCodePolicySet(description, endpoint.ErrorCodePolicy);
    }
}
