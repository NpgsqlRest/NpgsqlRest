namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: retry_strategy_name | retry_strategy | retry
    /// Syntax: retry_strategy [name]
    ///
    /// Description: Set the retry strategy for this endpoint.
    /// </summary>
    private static readonly string[] RetryStrategyKey = [
        "retry_strategy_name",
        "retry_strategy",
        "retry",
    ];

    private static void HandleRetryStrategy(
        RoutineEndpoint endpoint,
        string[] words,
        string description)
    {
        var name = string.Join(Consts.Space, words[1..]);
        if (Options.CommandRetryOptions.Strategies.TryGetValue(name, out var strategy))
        {
            endpoint.RetryStrategy = strategy;
            Logger?.RetryStrategySet(description, name);
        }
        else
        {
            Logger?.RetryStrategyNotFound(description, name);
        }
    }
}
