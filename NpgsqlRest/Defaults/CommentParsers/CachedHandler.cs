namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: cached
    /// Syntax: cached
    ///         cached [param1, param2, param3 [, ...]]
    ///
    /// Description: Enable caching for this endpoint, optionally specifying which parameters to use as cache keys.
    /// </summary>
    private const string CacheKey = "cached";

    private static void HandleCached(
        Routine routine,
        RoutineEndpoint endpoint,
        string[] words,
        int len,
        string description)
    {
        if (!(routine.ReturnsSet == false && routine.ColumnCount == 1 && routine.ReturnsRecordType is false))
        {
            Logger?.CommentInvalidCache(description);
        }
        endpoint.Cached = true;
        if (len > 1)
        {
            var names = words[1..];
            HashSet<string> result = new(names.Length);
            for (int j = 0; j < names.Length; j++)
            {
                var name = names[j];
                if (!routine.OriginalParamsHash.Contains(name))
                {
                    Logger?.CommentInvalidCacheParam(description, name);
                }
                else
                {
                    result.Add(name);
                }
            }
            endpoint.CachedParams = result;
        }
        else
        {
            // Bare `cached` (no parameter list) keys on EVERY routine parameter, as documented. Materialize
            // the full set here: the cache-key builder in NpgsqlRestEndpoint.cs only appends a parameter's
            // value when CachedParams contains it, so a null set would key on the routine identifier alone
            // and serve one entry to every call regardless of input.
            endpoint.CachedParams = [.. routine.OriginalParamsHash];
        }

        CommentLogger?.CommentCached(description, endpoint.CachedParams ?? []);
    }
}
