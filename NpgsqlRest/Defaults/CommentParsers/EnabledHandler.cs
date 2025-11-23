namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: enabled
    /// Syntax: enabled
    ///         enabled [tag1, tag2, tag3 [, ...]]
    ///
    /// Description: Enable this endpoint or enable it for specific tags.
    /// </summary>
    private const string EnabledKey = "enabled";

    private static void HandleEnabled(
        Routine routine,
        string[] wordsLower,
        int len,
        ref bool disabled)
    {
        if (len == 1)
        {
            disabled = false;
        }
        else if (routine.Tags is not null && routine.Tags.Length > 0)
        {
            string[] arr = wordsLower[1..];
            for (var j = 0; j < routine.Tags.Length; j++)
            {
                var tag = routine.Tags[j];
                if (StrEqualsToArray(tag, arr))
                {
                    disabled = false;
                    break;
                }
            }
        }
    }
}
