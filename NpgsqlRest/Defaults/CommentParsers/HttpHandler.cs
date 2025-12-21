namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: http
    /// Syntax: http
    ///         http [GET | POST | PUT | DELETE]
    ///         http [GET | POST | PUT | DELETE] path
    ///         http path
    ///
    /// Description: HTTP settings:
    /// - Use HTTP annotation to enable when running in CommentsMode.OnlyWithHttpTag option.
    /// - Change the HTTP method with the optional second argument.
    /// - Change the HTTP path with the optional third argument.
    /// - Change the HTTP path with second argument if the second argument doesn't match any valid VERB.
    /// </summary>
    private const string HttpKey = "http";

    private static void HandleHttp(
        RoutineEndpoint endpoint,
        string[] wordsLower,
        int len,
        string description,
        ref string urlDescription,
        ref string fullDescription,
        string routineDescription,
        ref bool hasHttpTag)
    {
        hasHttpTag = true;
        string? urlPathSegment = null;
        if (len == 2 || len == 3)
        {
            if (Enum.TryParse<Method>(wordsLower[1], true, out var parsedMethod))
            {
                endpoint.Method = parsedMethod;
                endpoint.RequestParamType = endpoint.Method == Method.GET
                    ? RequestParamType.QueryString
                    : RequestParamType.BodyJson;
            }
            else
            {
                urlPathSegment = wordsLower[1];
            }
        }
        if (len == 3)
        {
            urlPathSegment = wordsLower[2];
        }
        if (urlPathSegment is not null)
        {
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
            }
        }
        
        urlDescription = string.Concat(endpoint.Method.ToString(), " ", endpoint.Path);
        fullDescription = string.Concat(routineDescription, " mapped to ", urlDescription);
        Logger?.CommentSetHttp(fullDescription, endpoint.Method, endpoint.Path);
    }
}
