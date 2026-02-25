namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: Resolved parameter expression (key = SQL expression)
    /// Syntax: _param_name = select value from table where other_param = {_other_param}
    ///
    /// Description: When the key matches a real function parameter name, the value is treated as a
    /// SQL expression that is executed server-side to resolve the parameter value.
    /// The resolved value cannot be overridden by client input.
    /// Placeholders in the SQL expression reference other filled parameters.
    /// </summary>
    private static void HandleResolvedParameter(
        RoutineEndpoint endpoint,
        string paramName,
        string sqlExpression,
        string description)
    {
        endpoint.ResolvedParameterExpressions ??= new(StringComparer.OrdinalIgnoreCase);
        endpoint.ResolvedParameterExpressions[paramName] = sqlExpression;
        CommentLogger?.CommentSetCustomParemeter(description, paramName, "(resolved SQL expression)");
    }
}
