namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: openapi
    /// Syntax: openapi hide
    ///         openapi hidden
    ///         openapi ignore
    ///         openapi tag [tag1, tag2, tag3 [, ...]]
    ///         openapi tags [tag1, tag2, tag3 [, ...]]
    ///
    /// Description: Per-routine controls for the OpenAPI document. Consumed by the
    /// <c>NpgsqlRest.OpenApi</c> plugin; safe to leave on a routine even when the plugin is not loaded
    /// (the annotation simply sets properties on <see cref="RoutineEndpoint"/> that the plugin reads).
    ///
    /// <c>hide</c> / <c>hidden</c> / <c>ignore</c> — exclude this routine from the OpenAPI document
    /// while keeping the HTTP endpoint fully functional. Use to keep internal-only endpoints out of a
    /// document published to partners.
    ///
    /// <c>tag</c> / <c>tags</c> — override the default schema-name tag. Multiple tags can be supplied
    /// comma-separated. Tags drive grouping in tools like Swagger UI / ReDoc, so this is the natural
    /// way to slice "partner-facing" vs "internal" endpoints in a single document.
    /// </summary>
    private const string OpenApiKey = "openapi";
    private static readonly string[] OpenApiHideSubKey = ["hide", "hidden", "ignore"];
    private static readonly string[] OpenApiTagSubKey = ["tag", "tags"];

    private static void HandleOpenApi(
        RoutineEndpoint endpoint,
        string[] wordsLower,
        string[] words,
        int len,
        string description)
    {
        // Bare `openapi` (no sub-command) is treated as `openapi hide` — the path of least surprise for
        // a routine the author wants out of the docs entirely.
        if (len < 2)
        {
            endpoint.OpenApiHide = true;
            CommentLogger?.LogTrace("Endpoint {Description} marked as OpenAPI-hidden (bare `openapi` annotation)", description);
            return;
        }

        var sub = wordsLower[1];
        if (StrEqualsToArray(sub, OpenApiHideSubKey))
        {
            endpoint.OpenApiHide = true;
            CommentLogger?.LogTrace("Endpoint {Description} marked as OpenAPI-hidden", description);
        }
        else if (StrEqualsToArray(sub, OpenApiTagSubKey))
        {
            if (len < 3)
            {
                CommentLogger?.LogWarning(
                    "Endpoint {Description}: `openapi {Sub}` requires at least one tag value — ignored.",
                    description, sub);
                return;
            }
            // Preserve original casing for tag values. wordsLower is lowercased; words is original.
            endpoint.OpenApiTags = [.. words[2..]];
            CommentLogger?.LogTrace("Endpoint {Description} OpenAPI tags set: {Tags}",
                description, string.Join(", ", endpoint.OpenApiTags));
        }
        else
        {
            CommentLogger?.LogWarning(
                "Endpoint {Description}: unknown `openapi {Sub}` sub-command — expected `hide` or `tag <name>`.",
                description, sub);
        }
    }
}
