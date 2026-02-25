namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: encrypt | encrypted | protect | protected
    /// Syntax: encrypt
    ///         encrypt [param1, param2, param3 [, ...]]
    ///
    /// Description: Encrypt parameter values using the default data protector before sending to PostgreSQL.
    /// - Without arguments: encrypts all text parameters.
    /// - With arguments: encrypts only the specified parameters.
    /// </summary>
    private static readonly string[] EncryptKey = [
        "encrypt",
        "encrypted",
        "protect",
        "protected",
    ];

    private static void HandleEncrypt(
        Routine routine,
        RoutineEndpoint endpoint,
        string[] wordsLower,
        int len,
        string description)
    {
        if (len == 1)
        {
            endpoint.EncryptAllParameters = true;
            CommentLogger?.CommentEncryptAll(description);
        }
        else
        {
            var names = wordsLower[1..];
            HashSet<string> result = new(names.Length, StringComparer.OrdinalIgnoreCase);
            for (int j = 0; j < names.Length; j++)
            {
                var name = names[j];
                if (!routine.OriginalParamsHash.Contains(name))
                {
                    Logger?.LogWarning("Comment annotation encrypt: parameter \"{name}\" not found in {description}", name, description);
                }
                else
                {
                    result.Add(name);
                }
            }
            endpoint.EncryptParameters = result;
            CommentLogger?.CommentEncryptParams(description, result);
        }
    }
}
