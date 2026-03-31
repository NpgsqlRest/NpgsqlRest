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
        // wordsLower[0] may include @ prefix (e.g., "@new_line"), and line may also have @ prefix.
        // Find the first space after the keyword to extract the value portion.
        var keyEnd = line.IndexOf(' ');
        if (keyEnd < 0 || keyEnd + 1 >= line.Length)
        {
            return;
        }
        var nl = line[(keyEnd + 1)..];
        CommentLogger?.CommentSetRawNewLineSeparator(description, nl);
        endpoint.RawNewLineSeparator = Regex.Unescape(nl);
    }
}
