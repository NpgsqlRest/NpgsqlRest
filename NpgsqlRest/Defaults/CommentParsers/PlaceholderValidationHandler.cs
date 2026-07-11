namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Build-time validation for `{name}` parameter-value placeholders (response headers, custom parameters).
    /// At request time an unknown placeholder is left as literal text, so a typo silently ships e.g.
    /// `{_fil}` into a header/path. This warns when a value contains a placeholder that LOOKS like an
    /// identifier but matches no routine parameter (converted or original name, or a custom type name).
    /// Non-identifier braces (e.g. JSON `{"a":1}`) are ignored to avoid false positives.
    /// </summary>
    private static void WarnUnknownPlaceholders(Routine routine, string value, string description)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf(Consts.OpenBrace) < 0)
        {
            return;
        }

        var span = value.AsSpan();
        var pos = 0;
        while (pos < span.Length)
        {
            var open = span[pos..].IndexOf(Consts.OpenBrace);
            if (open < 0)
            {
                break;
            }
            open += pos;
            var rel = span[(open + 1)..].IndexOf(Consts.CloseBrace);
            if (rel < 0)
            {
                break;
            }
            var close = open + 1 + rel;
            var token = span[(open + 1)..close];
            pos = close + 1;

            // Strict forms {!name} / {!name:fallback}: validate just the name part - the fallback
            // text after the first ':' is arbitrary literal content.
            if (token.Length > 1 && token[0] == '!')
            {
                var colon = token.IndexOf(':');
                token = colon < 0 ? token[1..] : token[1..colon];
            }

            // Only flag tokens that look like a parameter name; skip literal/JSON braces.
            if (IsPlaceholderIdentifier(token) && !IsKnownParameter(routine, token))
            {
                Logger?.CommentUnknownPlaceholder(description, token.ToString());
            }
        }
    }

    private static bool IsPlaceholderIdentifier(ReadOnlySpan<char> token)
    {
        if (token.Length == 0 || !(char.IsAsciiLetter(token[0]) || token[0] == '_'))
        {
            return false;
        }
        for (var i = 1; i < token.Length; i++)
        {
            if (!(char.IsAsciiLetterOrDigit(token[i]) || token[i] == '_'))
            {
                return false;
            }
        }
        return true;
    }

    // Matches the exact keys the request-time lookup is built from (ActualName, ConvertedName,
    // CustomTypeName, and allowlisted env-var names), case-insensitively — consistent with the
    // case-insensitive substitution.
    private static bool IsKnownParameter(Routine routine, ReadOnlySpan<char> token)
    {
        foreach (var p in routine.Parameters)
        {
            if (token.Equals(p.ActualName, StringComparison.OrdinalIgnoreCase) ||
                token.Equals(p.ConvertedName, StringComparison.OrdinalIgnoreCase) ||
                (p.TypeDescriptor.CustomTypeName is not null &&
                 token.Equals(p.TypeDescriptor.CustomTypeName, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        // Allowlisted env vars are also valid placeholders (the dict is case-insensitive).
        return Options.SubstitutionEnvironmentVariables is { Count: > 0 } envVars
            && envVars.ContainsKey(token.ToString());
    }
}
