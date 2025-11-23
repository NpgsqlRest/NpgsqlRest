namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: query_string_null_handling | query_null_handling | query_string_null | query_null
    /// Syntax: query_string_null_handling [ empty_string | empty | null_literal | null | ignore ]
    ///
    /// Description: Set how null query string parameters are handled.
    /// </summary>
    private static readonly string[] QueryStringNullHandlingKey = [
        "query_string_null_handling",
        "query_null_handling",
        "query_string_null",
        "query_null",
    ];

    private static void HandleQueryStringNullHandling(
        RoutineEndpoint endpoint,
        string[] wordsLower,
        string description)
    {
        if (StrEqualsToArray(wordsLower[1], EmptyStringKey))
        {
            endpoint.QueryStringNullHandling = QueryStringNullHandling.EmptyString;
        }
        else if (StrEqualsToArray(wordsLower[1], NullLiteral))
        {
            endpoint.QueryStringNullHandling = QueryStringNullHandling.NullLiteral;
        }
        else if (StrEquals(wordsLower[1], RequestHeaderModeIgnoreKey))
        {
            endpoint.QueryStringNullHandling = QueryStringNullHandling.Ignore;
        }
        else
        {
            Logger?.InvalidQueryStringNullHandlingComment(wordsLower[1], description, endpoint.QueryStringNullHandling);
        }
        if (endpoint.TextResponseNullHandling != Options.TextResponseNullHandling)
        {
            Logger?.CommentSetQueryStringNullHandling(description, endpoint.QueryStringNullHandling);
        }
    }
}
