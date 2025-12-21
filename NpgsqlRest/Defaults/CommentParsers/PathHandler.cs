namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: path
    /// Syntax: path [path]
    ///
    /// Description: Sets the custom HTTP path.
    /// </summary>
    private const string PathKey = "path";

    private static void HandlePath(
        RoutineEndpoint endpoint,
        string[] wordsLower,
        string description,
        ref string urlDescription,
        ref string fullDescription,
        string routineDescription)
    {
        string? urlPathSegment = wordsLower[1];
        if (!Uri.TryCreate(urlPathSegment, UriKind.Relative, out Uri? uri))
        {
            Logger?.InvalidUrlPathSegmentComment(urlPathSegment, description, endpoint.Path);
        }
        else
        {
            endpoint.Path = uri.ToString();
            if (!endpoint.Path.StartsWith('/'))
            {
                endpoint.Path = string.Concat("/", endpoint.Path);
            }
            // Extract path parameters from the path template
            endpoint.PathParameters = ExtractPathParameters(endpoint.Path);

            Logger?.CommentSetHttp(description, endpoint.Method, endpoint.Path);
            urlDescription = string.Concat(endpoint.Method.ToString(), " ", endpoint.Path);
            fullDescription = string.Concat(routineDescription, " mapped to ", urlDescription);
        }
    }
}
