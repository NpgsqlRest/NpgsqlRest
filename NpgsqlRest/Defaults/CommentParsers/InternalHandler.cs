namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: internal
    /// Syntax: internal
    ///
    /// Description: Mark this endpoint as internal-only. Internal endpoints are accessible
    /// via self-referencing calls (proxy, HTTP client types) but are NOT exposed as HTTP routes.
    /// </summary>
    private static readonly string[] InternalKey = ["internal", "internal_only"];
}
