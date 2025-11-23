namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: request_headers_parameter_name | request_headers_param_name | request-headers-param-name
    /// Syntax: request_headers_parameter_name [name]
    ///
    /// Description: Set the name of the request headers parameter.
    /// </summary>
    private static readonly string[] RequestHeadersParameterNameKey = [
        "request_headers_parameter_name",
        "request_headers_param_name",
        "request-headers-param-name",
    ];

    private static void HandleRequestHeadersParameterName(
        RoutineEndpoint endpoint,
        string[] wordsLower,
        int len,
        string description)
    {
        if (len == 2)
        {
            if (!string.Equals(endpoint.RequestHeadersParameterName, wordsLower[1]))
            {
                Logger?.CommentSetRequestHeadersParamName(description, wordsLower[1]);
            }
            endpoint.RequestHeadersParameterName = wordsLower[1];
        }
    }
}
