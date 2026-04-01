namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    private static readonly string[] VoidKey = [
        "void",
        "void_result",
    ];

    private static void HandleVoid(
        RoutineEndpoint endpoint,
        string description)
    {
        CommentLogger?.CommentSetVoid(description);
        endpoint.Void = true;
    }
}
