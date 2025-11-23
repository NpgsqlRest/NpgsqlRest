using Microsoft.Extensions.Primitives;

namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: Response header (key: value syntax)
    /// Syntax: [header-name]: [value]
    ///
    /// Description: Set custom response headers for the endpoint.
    /// </summary>
    private static readonly string[] ContentTypeKey = [
        "content-type", // content-type is header key
        "content_type",
    ];

    private static void HandleHeader(
        RoutineEndpoint endpoint,
        string headerName,
        string headerValue,
        string description)
    {
        if (headerValue.Contains(Consts.OpenBrace) && headerValue.Contains(Consts.CloseBrace))
        {
            endpoint.HeadersNeedParsing = true;
        }
        if (StrEqualsToArray(headerName, ContentTypeKey))
        {
            if (!string.Equals(endpoint.ResponseContentType, headerValue))
            {
                Logger?.CommentSetContentType(description, headerValue);
            }
            endpoint.ResponseContentType = headerValue;
        }
        else
        {
            if (endpoint.ResponseHeaders is null)
            {
                endpoint.ResponseHeaders = new()
                {
                    [headerName] = new StringValues(headerValue)
                };
            }
            else
            {
                if (endpoint.ResponseHeaders.TryGetValue(headerName, out StringValues values))
                {
                    endpoint.ResponseHeaders[headerName] = StringValues.Concat(values, headerValue);
                }
                else
                {
                    endpoint.ResponseHeaders.Add(headerName, new StringValues(headerValue));
                }
            }
            if (!string.Equals(endpoint.ResponseContentType, headerValue))
            {
                Logger?.CommentSetHeader(description, headerName, headerValue);
            }
        }
    }
}
