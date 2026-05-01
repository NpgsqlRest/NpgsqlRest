namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: cache_profile
    /// Syntax: cache_profile &lt;name&gt;
    ///
    /// Selects a named caching profile defined in <see cref="CacheOptions.Profiles"/>. The profile supplies the
    /// cache backend, default expiration, default key parameters, and per-parameter skip conditions. The endpoint
    /// inherits these defaults; the existing <c>@cached p1, p2</c> and <c>@cache_expires X</c> annotations override
    /// the corresponding profile fields when present.
    ///
    /// Accepts exactly one profile name. Multiple names produce a Warning and the annotation is ignored.
    ///
    /// Profile name resolution happens at startup in <c>NpgsqlRestBuilder</c>. If the named profile does not exist,
    /// startup fails with a single error listing every unresolved name and the offending endpoints. This handler
    /// only stores the literal name on <see cref="RoutineEndpoint.CacheProfile"/>.
    ///
    /// Implies caching: an endpoint with <c>@cache_profile</c> is treated as cached even if it has no <c>@cached</c>
    /// annotation. The profile's <c>Parameters</c> list is used for the cache key (or all params if the profile
    /// doesn't specify any).
    /// </summary>
    private const string CacheProfileKey = "cache_profile";

    private static void HandleCacheProfile(
        RoutineEndpoint endpoint,
        string[] words,
        int len,
        string description)
    {
        if (len < 2 || string.IsNullOrWhiteSpace(words[1]))
        {
            Logger?.CommentInvalidCacheProfile(description, "no profile name supplied");
            return;
        }
        if (len > 2)
        {
            Logger?.CommentInvalidCacheProfile(description,
                $"expected exactly one profile name, got {len - 1} ({string.Join(", ", words[1..])}); ignoring annotation");
            return;
        }

        endpoint.CacheProfile = words[1];
        // Implies caching even without @cached.
        endpoint.Cached = true;
        CommentLogger?.CommentCacheProfile(description, words[1]);
    }
}
