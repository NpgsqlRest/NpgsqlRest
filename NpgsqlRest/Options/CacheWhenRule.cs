using System.Text.Json.Nodes;

namespace NpgsqlRest;

/// <summary>
/// One rule in a cache profile's <see cref="CacheProfile.When"/> list. Each rule combines:
///
/// - A <see cref="Parameter"/> name (a routine parameter to inspect at request time).
/// - A <see cref="Value"/> condition (scalar = exact match; array = OR over entries; JSON null matches .NET null/DBNull).
/// - An action: <see cref="Skip"/> = true → bypass the cache (no read, no write); otherwise <see cref="ThenExpiration"/>
///   is used to override the entry's TTL for this write.
///
/// In JSON config the action is expressed as a single <c>"Then"</c> field: the literal string <c>"skip"</c> sets
/// <see cref="Skip"/> = true; a PostgreSQL interval (e.g. <c>"30 seconds"</c>) sets <see cref="ThenExpiration"/>.
///
/// Multiple rules are evaluated in declaration order; first match wins. No match → fall through to the profile's
/// (or annotation's) default <see cref="CacheProfile.Expiration"/>.
///
/// Validation at startup: a rule whose <see cref="Parameter"/> name is not a real routine parameter, or which is
/// not part of the resolved cache-key parameter list, is dropped with a Warning. A rule with missing/invalid
/// <c>"Then"</c> JSON value is also dropped with a Warning.
/// </summary>
public class CacheWhenRule
{
    /// <summary>
    /// Routine parameter name to inspect (matches against either ActualName or ConvertedName).
    /// </summary>
    public string Parameter { get; set; } = "";

    /// <summary>
    /// Match condition. Scalar (single match) or JSON array (OR over entries). JSON null matches .NET null / DBNull.
    /// String/number match is case-insensitive ordinal on the stringified parameter value.
    /// </summary>
    public JsonNode? Value { get; set; }

    /// <summary>
    /// When this rule matches at request time:
    /// - <c>true</c> → bypass the cache entirely for this request (no read, no write).
    /// - <c>false</c> → use <see cref="ThenExpiration"/> as the entry's TTL when writing.
    ///
    /// Set during config parsing based on the JSON <c>"Then"</c> value: <c>"skip"</c> → true; PG interval → false.
    /// </summary>
    public bool Skip { get; set; }

    /// <summary>
    /// Override TTL applied to writes that match this rule. Only used when <see cref="Skip"/> is false.
    /// Null = entry never expires (matches the existing CacheExpiresIn=null behavior).
    /// </summary>
    public TimeSpan? ThenExpiration { get; set; }
}
