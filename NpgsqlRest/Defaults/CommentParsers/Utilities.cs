namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    public static bool StrEquals(string str1, string str2) =>
        str1.Equals(str2, StringComparison.OrdinalIgnoreCase);

    public static bool StrEqualsToArray(string str, params string[] arr)
    {
        for (var i = 0; i < arr.Length; i++)
        {
            if (str.Equals(arr[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public static string[] SplitWordsLower(this string str)
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

    public static string[] SplitWords(this string str)
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
            if (ContainsValidNameCharacter(part1))
            {
                return false;
            }
            return true;
        }
        return false;
    }

    private static bool ContainsValidNameCharacter(string input)
    {
        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c) is false && c != '-' && c != '_')
            {
                return true;
            }
        }
        return false;
    }
}
