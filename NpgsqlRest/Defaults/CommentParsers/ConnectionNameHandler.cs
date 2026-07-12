namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: connection | connection_name
    /// Syntax: connection [name]
    ///
    /// Description: Specify which named connection to use for this endpoint.
    /// </summary>
    private static readonly string[] ConnectionNameKey = [
        "connection",
        "connection_name",
    ];

    private static void HandleConnectionName(
        RoutineEndpoint endpoint,
        string[] wordsLower,
        string description)
    {
        var name = string.Join(Consts.Space, wordsLower[1..]);
        if (string.IsNullOrEmpty(name) is false)
        {
            SetValidatedConnectionName(endpoint, name, description);
            CommentLogger?.CommentConnectionName(description, name);
        }
        else
        {
            Logger?.CommentEmptyConnectionName(description);
        }
    }

    /// <summary>
    /// Shared by both annotation parse forms ("connection name" and "connection=name"): warns when the
    /// name is not registered in either DataSources (multi-host) or ConnectionStrings, then assigns it
    /// anyway (warn-only, back-compat - an unknown name still fails the request with a 500 at run time).
    /// </summary>
    private static void SetValidatedConnectionName(RoutineEndpoint endpoint, string name, string description)
    {
        if (Options.DataSources?.ContainsKey(name) is not true &&
            Options.ConnectionStrings?.ContainsKey(name) is not true)
        {
            Logger?.CommentInvalidConnectionName(description, name);
        }
        endpoint.ConnectionName = name;
    }
}
