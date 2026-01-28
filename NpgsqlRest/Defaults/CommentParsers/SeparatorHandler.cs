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
        var sep = line[(wordsLower[0].Length + 1)..];
        CommentLogger?.CommentSetRawValueSeparator(description, sep);
        endpoint.RawValueSeparator = Regex.Unescape(sep);
    }
}
