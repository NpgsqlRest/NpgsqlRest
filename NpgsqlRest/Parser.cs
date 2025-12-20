using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace NpgsqlRest;

public static partial class Parser
{
    // SIMD-accelerated search for wildcard characters
    private static readonly SearchValues<char> WildcardChars = SearchValues.Create("*?");

    [GeneratedRegex(@"^(\d*\.?\d+)\s*([a-z]*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex IntervalRegex();

    public static TimeSpan? ParsePostgresInterval(string? interval)
    {
        if (string.IsNullOrWhiteSpace(interval))
        {
            return null;
        }

        interval = interval.Trim().ToLowerInvariant();

        // Match number (integer or decimal) followed by optional space and optional unit
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

        // If no unit provided, default to seconds
        if (string.IsNullOrEmpty(unitPart))
        {
            return TimeSpan.FromSeconds(value);
        }

        // Map PostgreSQL units to TimeSpan conversions
        return unitPart switch
        {
            "us" or "usec" or "microsecond" or "microseconds" => TimeSpan.FromMicroseconds(value),
            "ms" or "msec" or "millisecond" or "milliseconds" => TimeSpan.FromMilliseconds(value),
            "s" or "sec" or "second" or "seconds" => TimeSpan.FromSeconds(value),
            "m" or "min" or "minute" or "minutes" => TimeSpan.FromMinutes(value),
            "h" or "hour" or "hours" => TimeSpan.FromHours(value),
            "d" or "day" or "days" => TimeSpan.FromDays(value),
            "w" or "week" or "weeks" => TimeSpan.FromDays(value * 7),
            _ => null
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPatternMatch(string name, string pattern)
    {
        if (name == null || pattern == null) return false;
        int nl = name.Length, pl = pattern.Length;
        if (nl == 0 || pl == 0) return false;

        // Fast path: extension matching (*.ext)
        if (pl > 1 && pattern[0] == Consts.Multiply && pattern[1] == Consts.Dot)
        {
            ReadOnlySpan<char> ext = pattern.AsSpan(1);
            return nl > ext.Length && name.AsSpan(nl - ext.Length).Equals(ext, StringComparison.OrdinalIgnoreCase);
        }

        // Check if pattern has wildcards using SIMD
        ReadOnlySpan<char> patternSpan = pattern.AsSpan();
        int firstWildcard = patternSpan.IndexOfAny(WildcardChars);

        // Fast path: no wildcards - simple case-insensitive comparison
        if (firstWildcard == -1)
        {
            return name.AsSpan().Equals(patternSpan, StringComparison.OrdinalIgnoreCase);
        }

        // Fast path: pattern starts with literal segment
        if (firstWildcard > 0)
        {
            // Check if name starts with the literal prefix
            if (!name.AsSpan(0, Math.Min(firstWildcard, nl)).Equals(patternSpan.Slice(0, Math.Min(firstWildcard, nl)), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Standard wildcard matching algorithm
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