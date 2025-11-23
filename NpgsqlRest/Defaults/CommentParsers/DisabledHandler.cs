namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: disabled
    /// Syntax: disabled
    ///         disabled [tag1, tag2, tag3 [, ...]]
    ///
    /// Description: Disable this endpoint or disable it for specific tags.
    /// </summary>
    private const string DisabledKey = "disabled";

    private static void HandleDisabled(
        Routine routine,
        string[] wordsLower,
        int len,
        ref bool disabled)
    {
        if (len == 1)
        {
            disabled = true;
        }
        else if (routine.Tags is not null && routine.Tags.Length > 0)
        {
            string[] arr = wordsLower[1..];
            for (var j = 0; j < routine.Tags.Length; j++)
            {
                var tag = routine.Tags[j];
                if (StrEqualsToArray(tag, arr))
                {
                    disabled = true;
                    break;
                }
            }
        }
    }
}
