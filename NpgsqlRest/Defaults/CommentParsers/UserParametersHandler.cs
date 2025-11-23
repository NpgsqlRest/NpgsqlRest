namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: user_parameters | user_params
    /// Syntax: user_parameters
    ///
    /// Description: Enable user parameters for this endpoint.
    /// </summary>
    private static readonly string[] UserParemetersKey = [
        "user_parameters",
        "user_params",
    ];

    private static void HandleUserParameters(
        RoutineEndpoint endpoint,
        string description)
    {
        endpoint.UseUserParameters = true;
        Logger?.CommentUserParameters(description);
    }
}
