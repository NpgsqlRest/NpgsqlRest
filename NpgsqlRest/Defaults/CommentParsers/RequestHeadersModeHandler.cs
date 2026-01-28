namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: request_headers_mode | request_headers
    /// Syntax: request_headers_mode [ ignore | context | parameter ]
    ///
    /// Description: Set how request headers are handled.
    /// </summary>
    private static readonly string[] RequestHeadersModeKey = [
        "request_headers_mode",
        "request_headers",
    ];

    private const string RequestHeaderModeIgnoreKey = "ignore";
    private const string RequestHeaderModeContextKey = "context";
    private const string RequestHeaderModeParameterKey = "parameter";

    private static void HandleRequestHeadersMode(
        RoutineEndpoint endpoint,
        string[] wordsLower,
        string description)
    {
        if (StrEquals(wordsLower[1], RequestHeaderModeIgnoreKey))
        {
            endpoint.RequestHeadersMode = RequestHeadersMode.Ignore;
        }
        else if (StrEquals(wordsLower[1], RequestHeaderModeContextKey))
        {
            endpoint.RequestHeadersMode = RequestHeadersMode.Context;
        }
        else if (StrEquals(wordsLower[1], RequestHeaderModeParameterKey))
        {
            endpoint.RequestHeadersMode = RequestHeadersMode.Parameter;
        }
        else
        {
            Logger?.InvalidRequestHeadersModeComment(wordsLower[1], description, endpoint.RequestHeadersMode);
        }
        if (endpoint.RequestHeadersMode != Options.RequestHeadersMode)
        {
            CommentLogger?.CommentSetRequestHeadersMode(description, wordsLower[1]);
        }
    }
}
