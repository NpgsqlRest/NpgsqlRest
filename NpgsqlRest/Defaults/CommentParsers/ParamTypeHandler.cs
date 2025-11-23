namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: request_param_type | param_type
    /// Syntax: request_param_type [[query_string | query] | [body_json | body]]
    ///
    /// Description: Set how request parameters are sent - query string or JSON body.
    /// </summary>
    private static readonly string[] ParamTypeKey = [
        "request_param_type",
        "param_type",
    ];

    private static readonly string[] QueryKey = [
        "query_string",
        "query"
    ];

    private static readonly string[] JsonKey = [
        "body_json",
        "body"
    ];

    private static void HandleParamType(
        RoutineEndpoint endpoint,
        string[] wordsLower,
        string description,
        RequestParamType originalParamType)
    {
        if (StrEqualsToArray(wordsLower[1], QueryKey))
        {
            endpoint.RequestParamType = RequestParamType.QueryString;
        }
        else if (StrEqualsToArray(wordsLower[1], JsonKey))
        {
            endpoint.RequestParamType = RequestParamType.BodyJson;
        }
        else
        {
            Logger?.InvalidParameterTypeComment(wordsLower[1], description, endpoint.RequestParamType);
        }

        if (originalParamType != endpoint.RequestParamType)
        {
            Logger?.CommentSetParameterType(description, endpoint.RequestParamType);
        }
    }
}
