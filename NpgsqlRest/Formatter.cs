using System.Buffers;

namespace NpgsqlRest;

public static class Formatter
{
    // SIMD-accelerated search for brace characters
    private static readonly SearchValues<char> BraceChars = SearchValues.Create("{}");
    public static ReadOnlySpan<char> FormatString(ReadOnlySpan<char> input, Dictionary<string, string> replacements)
    {
        if (replacements is null || replacements.Count == 0)
        {
            return input;
        }

        int inputLength = input.Length;

        if (inputLength == 0)
        {
            return input;
        }

        return FormatString(input, replacements.GetAlternateLookup<ReadOnlySpan<char>>());
    }

    public static ReadOnlySpan<char> FormatString(ReadOnlySpan<char> input, Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> lookup)
    {
        int inputLength = input.Length;

        if (inputLength == 0)
        {
            return input;
        }

        // First pass: calculate result length using SIMD-accelerated search
        int resultLength = 0;
        bool inside = false;
        int startIndex = 0;
        int i = 0;

        while (i < inputLength)
        {
            // Use SIMD to find the next brace character
            var remaining = input.Slice(i);
            int nextBraceOffset = remaining.IndexOfAny(BraceChars);

            if (nextBraceOffset == -1)
            {
                // No more braces, count remaining chars
                if (!inside)
                {
                    resultLength += remaining.Length;
                }
                else
                {
                    // Unclosed brace, will be handled at the end
                }
                break;
            }

            int braceIndex = i + nextBraceOffset;
            char ch = input[braceIndex];

            if (ch == Consts.OpenBrace)
            {
                if (!inside)
                {
                    // Count chars before the brace
                    resultLength += nextBraceOffset;
                }
                else
                {
                    // Nested open brace - treat previous content as literal
                    resultLength += braceIndex - startIndex;
                }
                inside = true;
                startIndex = braceIndex;
                i = braceIndex + 1;
            }
            else // CloseBrace
            {
                if (inside)
                {
                    inside = false;
                    if (lookup.TryGetValue(input[(startIndex + 1)..braceIndex], out var value))
                    {
                        resultLength += value.Length;
                    }
                    else
                    {
                        resultLength += braceIndex - startIndex + 1;
                    }
                    i = braceIndex + 1;
                }
                else
                {
                    // Unmatched close brace, count chars including brace
                    resultLength += nextBraceOffset + 1;
                    i = braceIndex + 1;
                }
            }
        }

        if (inside)
        {
            resultLength += inputLength - startIndex;
        }

        // Second pass: build result using SIMD-accelerated search
        char[] resultArray = new char[resultLength];
        Span<char> result = resultArray;
        int resultPos = 0;

        inside = false;
        startIndex = 0;
        i = 0;

        while (i < inputLength)
        {
            var remaining = input.Slice(i);
            int nextBraceOffset = remaining.IndexOfAny(BraceChars);

            if (nextBraceOffset == -1)
            {
                if (!inside)
                {
                    remaining.CopyTo(result.Slice(resultPos));
                    resultPos += remaining.Length;
                }
                break;
            }

            int braceIndex = i + nextBraceOffset;
            char ch = input[braceIndex];

            if (ch == Consts.OpenBrace)
            {
                if (!inside)
                {
                    // Copy chars before the brace
                    if (nextBraceOffset > 0)
                    {
                        remaining.Slice(0, nextBraceOffset).CopyTo(result.Slice(resultPos));
                        resultPos += nextBraceOffset;
                    }
                }
                else
                {
                    // Nested open brace - copy previous content as literal
                    var literal = input.Slice(startIndex, braceIndex - startIndex);
                    literal.CopyTo(result.Slice(resultPos));
                    resultPos += literal.Length;
                }
                inside = true;
                startIndex = braceIndex;
                i = braceIndex + 1;
            }
            else // CloseBrace
            {
                if (inside)
                {
                    inside = false;
                    if (lookup.TryGetValue(input[(startIndex + 1)..braceIndex], out var value))
                    {
                        value.AsSpan().CopyTo(result.Slice(resultPos));
                        resultPos += value.Length;
                    }
                    else
                    {
                        var literal = input.Slice(startIndex, braceIndex - startIndex + 1);
                        literal.CopyTo(result.Slice(resultPos));
                        resultPos += literal.Length;
                    }
                    i = braceIndex + 1;
                }
                else
                {
                    // Unmatched close brace, copy chars including brace
                    remaining.Slice(0, nextBraceOffset + 1).CopyTo(result.Slice(resultPos));
                    resultPos += nextBraceOffset + 1;
                    i = braceIndex + 1;
                }
            }
        }

        if (inside)
        {
            input.Slice(startIndex).CopyTo(result.Slice(resultPos));
        }

        return resultArray;
    }

    /// <summary>
    /// Converts a SQL expression template with {placeholder} syntax into a parameterized SQL string
    /// with $N positional parameters. Returns the parameterized SQL and a list of (name, value) pairs.
    /// Placeholders are looked up from the command parameters by ActualName or ConvertedName.
    /// Uses SIMD-accelerated brace search via the shared BraceChars SearchValues.
    /// </summary>
    internal static (string Sql, List<(string Name, object? Value)> Parameters) ParameterizeSqlExpression(
        string expression,
        Npgsql.NpgsqlParameterCollection commandParams)
    {
        ReadOnlySpan<char> span = expression;
        var result = new System.Text.StringBuilder(expression.Length);
        var sqlParams = new List<(string Name, object? Value)>();
        int paramIndex = 0;
        int pos = 0;

        while (pos < span.Length)
        {
            var remaining = span.Slice(pos);
            int braceOffset = remaining.IndexOfAny(BraceChars);

            if (braceOffset == -1)
            {
                result.Append(remaining);
                break;
            }

            int braceIndex = pos + braceOffset;

            if (span[braceIndex] == Consts.OpenBrace)
            {
                // Append everything before the open brace
                if (braceOffset > 0)
                {
                    result.Append(remaining[..braceOffset]);
                }

                // Find closing brace
                var afterBrace = span[(braceIndex + 1)..];
                int closeOffset = afterBrace.IndexOf(Consts.CloseBrace);

                if (closeOffset == -1)
                {
                    // No closing brace, append open brace as literal
                    result.Append(Consts.OpenBrace);
                    pos = braceIndex + 1;
                    continue;
                }

                var placeholderSpan = span.Slice(braceIndex + 1, closeOffset);

                // Look up value from command parameters using span comparison (no allocation)
                object? value = DBNull.Value;
                string? matchedName = null;
                for (int p = 0; p < commandParams.Count; p++)
                {
                    var param = (NpgsqlRestParameter)commandParams[p];
                    if (placeholderSpan.Equals(param.ActualName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = param.Value ?? DBNull.Value;
                        matchedName = param.ActualName;
                        break;
                    }
                    if (placeholderSpan.Equals(param.ConvertedName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = param.Value ?? DBNull.Value;
                        matchedName = param.ConvertedName;
                        break;
                    }
                }

                paramIndex++;
                result.Append('$');
                result.Append(paramIndex);
                // Reuse matched parameter name string to avoid allocation; fall back to ToString only if unmatched
                sqlParams.Add((matchedName ?? placeholderSpan.ToString(), value));

                pos = braceIndex + 1 + closeOffset + 1;
            }
            else
            {
                // Stray close brace - append everything up to and including it
                result.Append(remaining[..(braceOffset + 1)]);
                pos = braceIndex + 1;
            }
        }

        return (result.ToString(), sqlParams);
    }
}