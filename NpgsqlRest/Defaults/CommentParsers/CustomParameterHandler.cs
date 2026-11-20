namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Custom parameters whose value is SQL text executed by an upload handler (<c>row_command</c>, or the
    /// handler-prefixed <c>csv_row_command</c> / <c>excel_row_command</c>). Placeholders are never substituted in
    /// them: a request value inserted into SQL text would be SQL injection.
    /// </summary>
    internal static bool IsSqlCustomParameter(string name) =>
        name.Equals("row_command", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("_row_command", StringComparison.OrdinalIgnoreCase);

    private static bool HasPlaceholderToken(string value)
    {
        for (var i = value.IndexOf('{'); i >= 0 && i < value.Length - 1; i = value.IndexOf('{', i + 1))
        {
            var j = i + 1;
            if (value[j] == '!')
            {
                j++;
            }
            var start = j;
            while (j < value.Length && (char.IsLetterOrDigit(value[j]) || value[j] == '_'))
            {
                j++;
            }
            if (j > start && j < value.Length && (value[j] == '}' || value[j] == ':') && char.IsDigit(value[start]) is false)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Annotation: Custom parameter (key = value syntax)
    /// Syntax: [key] = [value]
    ///
    /// Description: Set custom parameter values that can be used in endpoint processing.
    /// </summary>
    private static void HandleCustomParameter(
        RoutineEndpoint endpoint,
        string customParamName,
        string customParamValue,
        string description)
    {
        if (customParamValue.Contains(Consts.OpenBrace) && customParamValue.Contains(Consts.CloseBrace))
        {
            endpoint.CustomParamsNeedParsing = true;
            // the name as it is stored: without the optional @
            if (IsSqlCustomParameter(customParamName.TrimStart('@')) && HasPlaceholderToken(customParamValue))
            {
                Logger?.LogWarning(
                    "{Description}: custom parameter {Name} is SQL - {{placeholders}} in it are not substituted (request values in SQL text would be SQL injection). Pass values through the command's $N parameters instead.",
                    description, customParamName);
            }
        }
        SetCustomParameter(endpoint, customParamName, customParamValue, description);
        CommentLogger?.CommentSetCustomParemeter(description, customParamName, customParamValue);
    }
}
