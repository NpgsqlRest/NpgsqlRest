namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: rate_limiter_policy_name | rate_limiter_policy | rate_limiter
    /// Syntax: rate_limiter_policy [name]
    ///
    /// Description: Set the rate limiter policy for this endpoint.
    /// </summary>
    private static readonly string[] RateLimiterPolicyKey = [
        "rate_limiter_policy_name",
        "rate_limiter_policy",
        "rate_limiter",
    ];

    private static void HandleRateLimiterPolicy(
        RoutineEndpoint endpoint,
        string[] words,
        string description)
    {
        endpoint.RateLimiterPolicy = string.Join(Consts.Space, words[1..]);
        CommentLogger?.RateLimiterPolicySet(description, endpoint.RateLimiterPolicy);
    }
}
