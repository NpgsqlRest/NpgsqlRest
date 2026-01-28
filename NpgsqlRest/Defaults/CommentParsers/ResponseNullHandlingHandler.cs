namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: response_null_handling | response_null
    /// Syntax: response_null_handling [ empty_string | empty | null_literal | null | no_content | 204 | 204_no_content ]
    ///
    /// Description: Set how null responses are handled.
    /// </summary>
    private static readonly string[] TextResponseNullHandlingKey = [
        "response_null_handling",
        "response_null",
    ];

    private static readonly string[] EmptyStringKey = [
        "empty",
        "empty_string"
    ];

    private static readonly string[] NullLiteral = [
        "null_literal",
        "null"
    ];

    private static readonly string[] NoContentKey = [
        "204",
        "204_no_content",
        "no_content",
    ];

    private static void HandleResponseNullHandling(
        RoutineEndpoint endpoint,
        string[] wordsLower,
        string description)
    {
        if (StrEqualsToArray(wordsLower[1], EmptyStringKey))
        {
            endpoint.TextResponseNullHandling = TextResponseNullHandling.EmptyString;
        }
        else if (StrEqualsToArray(wordsLower[1], NullLiteral))
        {
            endpoint.TextResponseNullHandling = TextResponseNullHandling.NullLiteral;
        }
        else if (StrEqualsToArray(wordsLower[1], NoContentKey))
        {
            endpoint.TextResponseNullHandling = TextResponseNullHandling.NoContent;
        }
        else
        {
            Logger?.InvalidResponseNullHandlingModeComment(wordsLower[1], description, endpoint.TextResponseNullHandling);
        }
        if (endpoint.TextResponseNullHandling != Options.TextResponseNullHandling)
        {
            CommentLogger?.CommentSetTextResponseNullHandling(description, wordsLower[1]);
        }
    }
}
