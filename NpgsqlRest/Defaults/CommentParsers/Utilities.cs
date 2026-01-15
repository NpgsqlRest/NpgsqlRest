using System.Text.RegularExpressions;

namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    // Regex to match path parameters like {param_name} or {paramName}
    [GeneratedRegex(@"\{(\w+)\}", RegexOptions.Compiled)]
    private static partial Regex PathParameterRegex();

    /// <summary>
    /// Extracts parameter names from a path template.
    /// For example: "/products/{p_id}/reviews/{review_id}" returns ["p_id", "review_id"]
    /// </summary>
    public static string[]? ExtractPathParameters(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.Contains('{'))
        {
            return null;
        }

        var matches = PathParameterRegex().Matches(path);
        if (matches.Count == 0)
        {
            return null;
        }

        var result = new string[matches.Count];
        for (int i = 0; i < matches.Count; i++)
        {
            result[i] = matches[i].Groups[1].Value;
        }
        return result;
    }

    public static bool StrEquals(string str1, string str2)
    {
        // Support optional @ prefix for annotations (e.g., "@authorize" == "authorize")
        var s1 = str1.Length > 0 && str1[0] == '@' ? str1[1..] : str1;
        return s1.Equals(str2, StringComparison.OrdinalIgnoreCase);
    }

    public static bool StrEqualsToArray(string str, params string[] arr)
    {
        // Support optional @ prefix for annotations (e.g., "@authorize" matches "authorize")
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
            // Allow @ as first character for annotation prefix (e.g., "@timeout = 30s")
            if (char.IsLetterOrDigit(c) is false && c != '-' && c != '_' && c != '@')
            {
                return true;
            }
        }
        return false;
    }
}
