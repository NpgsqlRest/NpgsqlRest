namespace NpgsqlRest;

/// <summary>
/// A named caching policy that an endpoint can opt into via the <c>@cache_profile</c> comment annotation.
/// Profiles let you maintain multiple distinct caching policies (different backends, expirations, key shapes,
/// or bypass conditions) within a single application.
///
/// At the C# / library level a <see cref="CacheProfile"/> is fully configured: its <see cref="Cache"/> is an
/// <see cref="IRoutineCache"/> instance you provide. The NpgsqlRestClient configuration layer reads JSON profiles
/// (with a <c>"Type"</c> string and optional <c>"Enabled": true</c>) and constructs profile instances on your behalf.
/// The core library itself stays interface-only and does not know about Memory/Redis/Hybrid backends — it just
/// stores the resolved <see cref="IRoutineCache"/>.
///
/// Profile resolution at startup:
/// - Each endpoint with <c>@cache_profile name</c> looks up <c>name</c> in <see cref="CacheOptions.Profiles"/>.
/// - Unknown names cause startup to fail with a single error listing all unresolved references and the offending endpoints.
/// - Endpoints without <c>@cache_profile</c> use <see cref="CacheOptions.DefaultRoutineCache"/> (the root cache).
/// </summary>
public class CacheProfile
{
    /// <summary>
    /// The cache backend instance for this profile. Required.
    ///
    /// The library does not interpret <see cref="Cache"/> beyond calling <c>Get</c>/<c>AddOrUpdate</c>/<c>Remove</c>.
    /// In the client app, the same <see cref="IRoutineCache"/> instance is reused for every profile of the same
    /// <c>"Type"</c> — one Memory cache, one Redis connection, one HybridCache singleton at most.
    /// </summary>
    public IRoutineCache Cache { get; set; } = null!;

    /// <summary>
    /// Default expiration for entries written under this profile. <c>null</c> means entries never expire (matches
    /// today's <c>@cached</c> behavior when no <c>@cache_expires</c> is set).
    ///
    /// An endpoint's <c>@cache_expires &lt;interval&gt;</c> annotation overrides this value.
    /// </summary>
    public TimeSpan? Expiration { get; set; }

    /// <summary>
    /// Default cache-key parameter list for endpoints using this profile when their <c>@cached</c> annotation
    /// does not specify any. Three semantics:
    ///
    /// - <c>null</c> (or property omitted): use ALL routine parameters as the cache key — same as today's bare <c>@cached</c>.
    /// - <c>[]</c> (empty array): use NO parameters — one cache entry per endpoint URL, regardless of inputs.
    /// - <c>["p1", "p2"]</c>: use only these named parameters as the cache key.
    ///
    /// The endpoint's <c>@cached p1, p2</c> annotation overrides this list; if both the annotation and the profile
    /// specify parameters, the annotation wins.
    /// </summary>
    public string[]? Parameters { get; set; }

    /// <summary>
    /// Optional list of conditional rules evaluated against the resolved parameter values at request time.
    /// Each rule combines a <see cref="CacheWhenRule.Parameter"/> name, a <see cref="CacheWhenRule.Value"/>
    /// condition (scalar or array), and an action — either bypass the cache (<see cref="CacheWhenRule.Skip"/>)
    /// or override the entry's TTL (<see cref="CacheWhenRule.ThenExpiration"/>).
    ///
    /// Rules are evaluated in declaration order; the first match wins. No match → fall through to the profile's
    /// (or annotation's) default <see cref="Expiration"/>. Use this for:
    /// - Skip-on-condition (e.g. "if `to` is null, fetch fresh")
    /// - Tiered TTLs ("if `tier=free`, cache 5 min; if `tier=pro`, no cache")
    /// - Status-aware caching ("if `status=draft`, cache 30 sec; if `status=published`, cache 1 hour")
    ///
    /// Each rule's <c>Parameter</c> must be in the cache-key parameter list (via profile's <see cref="Parameters"/>
    /// or the endpoint's <c>@cached</c> annotation), otherwise different rule-evaluations would share the same cache
    /// entry and produce confusing results. Rules whose Parameter isn't in the cache key are dropped with a startup
    /// Warning.
    /// </summary>
    public CacheWhenRule[]? When { get; set; }
}
