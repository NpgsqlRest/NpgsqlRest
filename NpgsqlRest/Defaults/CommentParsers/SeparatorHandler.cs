using System.Text.RegularExpressions;

namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: separator | raw_separator
    /// Syntax: separator [value]
    ///
    /// Description: Set the value separator for raw mode output.
    /// </summary>
    private static readonly string[] SeparatorKey = [
        "separator",
        "raw_separator",
    ];

    private static void HandleSeparator(
        RoutineEndpoint endpoint,
        string line,
        string[] wordsLower,
        string description)
    {
        // wordsLower[0] may include @ prefix (e.g., "@separator"), and line may also have @ prefix.
        // Find the first space after the keyword to extract the value portion.
        var keyEnd = line.IndexOf(' ');
        if (keyEnd < 0 || keyEnd + 1 >= line.Length)
        {
            return;
        }
        var sep = line[(keyEnd + 1)..];
        CommentLogger?.CommentSetRawValueSeparator(description, sep);
        endpoint.RawValueSeparator = Regex.Unescape(sep);
    }
}
