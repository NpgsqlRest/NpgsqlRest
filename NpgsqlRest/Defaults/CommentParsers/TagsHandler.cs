namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: for | tags | tag
    /// Syntax: for [tag1, tag2, tag3 [, ...]]
    ///         tags [tag1, tag2, tag3 [, ...]]
    ///         tag [tag1, tag2, tag3 [, ...]]
    ///
    /// Description: Filter endpoint by tags.
    /// </summary>
    private static readonly string[] TagsKey = ["for", "tags", "tag"];

    private static void HandleTags(
        Routine routine,
        string[] wordsLower,
        ref bool haveTag)
    {
        if (routine.Tags is null || routine.Tags.Length == 0)
        {
            return;
        }

        string[] arr = wordsLower[1..];
        bool found = false;
        for (var j = 0; j < routine.Tags.Length; j++)
        {
            var tag = routine.Tags[j];
            if (StrEqualsToArray(tag, arr))
            {
                found = true;
                break;
            }
        }
        haveTag = found;
    }
}
