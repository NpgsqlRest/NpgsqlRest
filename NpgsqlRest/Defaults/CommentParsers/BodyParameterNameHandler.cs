namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: body_parameter_name | body_param_name
    /// Syntax: body_parameter_name [name]
    ///
    /// Description: Set the name of the body parameter. The name is matched case-insensitively
    /// against either the parameter's actual (database) name or its converted (API) name, so an
    /// HTTP Custom Type field can be targeted by its converted name (e.g. "responseBody").
    /// </summary>
    private static readonly string[] BodyParameterNameKey = [
        "body_parameter_name",
        "body_param_name"
    ];

    private static void HandleBodyParameterName(
        RoutineEndpoint endpoint,
        string[] words,
        int len,
        string description)
    {
        if (len == 2)
        {
            // Preserve the original case the user wrote: the name is matched case-insensitively at
            // request time, but matching against the camelCase converted name (e.g. "responseBody")
            // only works when the stored value is not force-lowercased.
            if (!string.Equals(endpoint.BodyParameterName, words[1]))
            {
                CommentLogger?.CommentSetBodyParamName(description, words[1]);
            }
            endpoint.BodyParameterName = words[1];
        }
    }
}
