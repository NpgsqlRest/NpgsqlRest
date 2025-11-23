namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
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
        }
        SetCustomParameter(endpoint, customParamName, customParamValue);
        Logger?.CommentSetCustomParemeter(description, customParamName, customParamValue);
    }
}
