namespace NpgsqlRest.Common;

/// <summary>
/// Shared comment/annotation parsing string primitives, compiled directly into each consuming
/// assembly (NpgsqlRest core, NpgsqlRest.SqlFileSource, ...) via linked source — NOT a separate
/// assembly or NuGet package. Declared <c>internal</c> on purpose: each consumer gets its own copy,
/// avoiding duplicate-public-type collisions across the core→plugin reference boundary.
///
/// </summary>
internal static class CommentPrimitives
{
    // Canonical comment-annotation word separators (space, comma).
    private static readonly char[] WordSeparators = [' ', ','];

    /// <summary>
    /// Case-insensitive compare with an optional leading '@' stripped from <paramref name="str1"/>
    /// (so "@authorize" equals "authorize"). The '@' is NOT stripped from <paramref name="str2"/>.
    /// </summary>
    public static bool StrEquals(string str1, string str2)
    {
        var s1 = str1.Length > 0 && str1[0] == '@' ? str1[1..] : str1;
        return s1.Equals(str2, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Case-insensitive match of <paramref name="str"/> (optional leading '@' stripped) against any
    /// of the supplied aliases.
    /// </summary>
    public static bool StrEqualsToArray(string str, params string[] arr)
    {
        var s = str.Length > 0 && str[0] == '@' ? str[1..] : str;
        for (var i = 0; i < arr.Length; i++)
        {
            if (s.Equals(arr[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Split into trimmed words on space/comma, removing empty entries. Null input yields an empty
    /// array. Case preserved. Extension method.
    /// </summary>
    public static string[] SplitWords(this string? str)
    {
        if (str is null)
        {
            return [];
        }
        return [.. str
            .Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
        ];
    }

    /// <summary>
    /// As <see cref="SplitWords"/> but each word is lower-cased (invariant). Extension method.
    /// </summary>
    public static string[] SplitWordsLower(this string? str)
    {
        if (str is null)
        {
            return [];
        }
        return [.. str
            .Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().ToLowerInvariant())
        ];
    }

    /// <summary>
    /// Splits <paramref name="str"/> on the first occurrence of <paramref name="sep"/> into a trimmed
    /// key/value pair. Returns false when the separator is absent, or when the key part contains a
    /// character that is not valid in a name (letters, digits, '-', '_', '@').
    /// </summary>
    public static bool SplitBySeparatorChar(string str, char sep, out string part1, out string part2)
    {
        part1 = null!;
        part2 = null!;
        if (str.Contains(sep) is false)
        {
            return false;
        }

        var parts = str.Split(sep, 2);
        if (parts.Length == 2)
        {
            part1 = parts[0].Trim();
            part2 = parts[1].Trim();
            if (ContainsInvalidNameCharacter(part1))
            {
                return false;
            }
            return true;
        }
        return false;
    }

    private static bool ContainsInvalidNameCharacter(string input)
    {
        foreach (char c in input)
        {
            // Allow '@' as a prefix (e.g., "@timeout = 30s"), plus '-' and '_'.
            if (char.IsLetterOrDigit(c) is false && c != '-' && c != '_' && c != '@')
            {
                return true;
            }
        }
        return false;
    }
}
