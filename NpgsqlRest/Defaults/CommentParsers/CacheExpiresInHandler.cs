namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: cache_expires | cache_expires_in
    /// Syntax: cache_expires [interval]
    ///
    /// Description: Set cache expiration time as a PostgreSQL interval.
    /// </summary>
    private static readonly string[] CacheExpiresInKey = [
        "cache_expires",
        "cache_expires_in",
    ];

    private static void HandleCacheExpiresIn(
        RoutineEndpoint endpoint,
        string[] wordsLower,
        string description)
    {
        var value = Parser.ParsePostgresInterval(string.Join(Consts.Space, wordsLower[1..]));
        if (value is not null)
        {
            endpoint.CacheExpiresIn = value.Value;
            Logger?.CommentCacheExpiresIn(description, value.Value);
        }
        else
        {
            Logger?.InvalidCacheExpiresIn(description, string.Join(Consts.Space, wordsLower[1..]));
        }
    }
}
