using System.Text.RegularExpressions;

namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: new_line | raw_new_line
    /// Syntax: new_line [value]
    ///
    /// Description: Set the newline separator for raw mode output.
    /// </summary>
    private static readonly string[] NewLineKey = [
        "new_line",
        "raw_new_line",
    ];

    private static void HandleNewLine(
        RoutineEndpoint endpoint,
        string line,
        string[] wordsLower,
        string description)
    {
        var nl = line[(wordsLower[0].Length + 1)..];
        CommentLogger?.CommentSetRawNewLineSeparator(description, nl);
        endpoint.RawNewLineSeparator = Regex.Unescape(nl);
    }
}
