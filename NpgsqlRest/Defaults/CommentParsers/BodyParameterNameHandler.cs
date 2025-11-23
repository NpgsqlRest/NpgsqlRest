namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: body_parameter_name | body_param_name
    /// Syntax: body_parameter_name [name]
    ///
    /// Description: Set the name of the body parameter.
    /// </summary>
    private static readonly string[] BodyParameterNameKey = [
        "body_parameter_name",
        "body_param_name"
    ];

    private static void HandleBodyParameterName(
        RoutineEndpoint endpoint,
        string[] wordsLower,
        int len,
        string description)
    {
        if (len == 2)
        {
            if (!string.Equals(endpoint.BodyParameterName, wordsLower[1]))
            {
                Logger?.CommentSetBodyParamName(description, wordsLower[1]);
            }
            endpoint.BodyParameterName = wordsLower[1];
        }
    }
}
