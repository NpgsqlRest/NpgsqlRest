namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: buffer_rows | buffer
    /// Syntax: buffer_rows [number]
    ///
    /// Description: Set the number of rows to buffer when reading results.
    /// </summary>
    private static readonly string[] BufferRowsKey = [
        "buffer_rows",
        "buffer"
    ];

    private static void HandleBufferRows(
        RoutineEndpoint endpoint,
        string[] wordsLower,
        string description)
    {
        if (ulong.TryParse(wordsLower[1], out var parsedBuffer))
        {
            if (endpoint.BufferRows != parsedBuffer)
            {
                Logger?.CommentBufferRows(description, wordsLower[1]);
            }
            endpoint.BufferRows = parsedBuffer;
        }
        else
        {
            Logger?.InvalidBufferRows(wordsLower[1], description, Options.BufferRows);
        }
    }
}
