namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: decrypt | decrypted | unprotect | unprotected
    /// Syntax: decrypt
    ///         decrypt [column1, column2, column3 [, ...]]
    ///
    /// Description: Decrypt result column values using the default data protector before returning to the client.
    /// - Without arguments: decrypts all text result columns.
    /// - With arguments: decrypts only the specified columns.
    /// </summary>
    private static readonly string[] DecryptKey = [
        "decrypt",
        "decrypted",
        "unprotect",
        "unprotected",
    ];

    private static void HandleDecrypt(
        RoutineEndpoint endpoint,
        string[] wordsLower,
        int len,
        string description)
    {
        if (len == 1)
        {
            endpoint.DecryptAllColumns = true;
            CommentLogger?.CommentDecryptAll(description);
        }
        else
        {
            var names = wordsLower[1..];
            HashSet<string> result = new(names.Length, StringComparer.OrdinalIgnoreCase);
            for (int j = 0; j < names.Length; j++)
            {
                result.Add(names[j]);
            }
            endpoint.DecryptColumns = result;
            CommentLogger?.CommentDecryptColumns(description, result);
        }
    }
}
