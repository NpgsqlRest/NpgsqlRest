﻿using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace NpgsqlRest;

public static partial class Parser
{
    [GeneratedRegex(@"^(\d*\.?\d+)\s*([a-z]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex IntervalRegex();

    public static TimeSpan? ParsePostgresInterval(string interval)
    {
        if (string.IsNullOrWhiteSpace(interval))
        {
            return null;
        }

        interval = interval.Trim().ToLowerInvariant();

        // Match number (integer or decimal) followed by optional space and unit
        var match = IntervalRegex().Match(interval);
        if (!match.Success)
        {
            return null;
        }

        string numberPart = match.Groups[1].Value;
        string unitPart = match.Groups[2].Value;

        if (!double.TryParse(numberPart, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double value))
        {
            return null;
        }

        // Map PostgreSQL units to TimeSpan conversions
        return unitPart switch
        {
            "s" or "sec" or "second" or "seconds" => TimeSpan.FromSeconds(value),
            "m" or "min" or "minute" or "minutes" => TimeSpan.FromMinutes(value),
            "h" or "hour" or "hours" => TimeSpan.FromHours(value),
            "d" or "day" or "days" => TimeSpan.FromDays(value),
            _ => null
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPatternMatch(string name, string pattern)
    {
        if (name == null || pattern == null) return false;
        int nl = name.Length, pl = pattern.Length;
        if (nl == 0 || pl == 0) return false;

        if (pl > 1 && pattern[0] == Consts.Multiply && pattern[1] == Consts.Dot)
        {
            ReadOnlySpan<char> ext = pattern.AsSpan(1);
            return nl > ext.Length && name.AsSpan(nl - ext.Length).Equals(ext, StringComparison.OrdinalIgnoreCase);
        }

        int ni = 0, pi = 0;
        int lastStar = -1, lastMatch = 0;

        while (ni < nl)
        {
            if (pi < pl)
            {
                char pc = pattern[pi];
                if (pc == Consts.Multiply)
                {
                    lastStar = pi++;
                    lastMatch = ni;
                    continue;
                }
                if (pc == Consts.Question ? ni < nl : char.ToLowerInvariant(pc) == char.ToLowerInvariant(name[ni]))
                {
                    ni++;
                    pi++;
                    continue;
                }
            }
            if (lastStar >= 0)
            {
                pi = lastStar + 1;
                ni = ++lastMatch;
                continue;
            }
            return false;
        }

        while (pi < pl && pattern[pi] == Consts.Multiply) pi++;
        return pi == pl;
    }
}