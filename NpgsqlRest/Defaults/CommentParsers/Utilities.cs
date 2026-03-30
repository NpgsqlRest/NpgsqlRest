using System.Text.RegularExpressions;

namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Returns Logger only when DebugLogCommentAnnotationEvents is enabled, otherwise null.
    /// This allows comment annotation trace logs to be suppressed when the option is disabled.
    /// </summary>
    private static ILogger? CommentLogger => Options.DebugLogCommentAnnotationEvents ? Logger : null;

    /// <summary>
    /// Thread-static list collecting short annotation labels during Parse().
    /// Used to emit a single aggregated Debug log per endpoint instead of many per-annotation Trace logs.
    /// </summary>
    [ThreadStatic]
    private static List<string>? _annotationLabels;

    private static void TrackAnnotation(string label)
    {
        _annotationLabels?.Add(label);
    }
    // Regex to match path parameters like {param_name}, {paramName}, or {param_name?} (optional)
    [GeneratedRegex(@"\{(\w+)\??\}", RegexOptions.Compiled)]
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

    /// <summary>
    /// For SQL file routines, update column type descriptors when @param annotations have
    /// retyped parameters to non-text types. The Describe step runs before annotations, so
    /// columns from "select $1 as col" are initially typed as text when the parameter type
    /// was unknown. After retype, the column descriptor should reflect the new type so that
    /// JSON serialization produces correctly typed output (numbers, booleans, not strings).
    ///
    /// Matches parameters to columns by scanning the SQL expression for $N references in
    /// the SELECT list. Each $N at column position i gets the retyped parameter's type.
    /// </summary>
    private static void UpdateColumnDescriptorsFromRetypedParams(Routine routine)
    {
        if (routine.ColumnsTypeDescriptor.Length == 0 || routine.ParamCount == 0)
        {
            return;
        }

        // Build map of param OriginalName ($N) → retyped type name for parameters
        // where @param annotation changed the type (NpgsqlDbType differs from TypeDescriptor)
        Dictionary<string, string>? retypedParams = null;
        foreach (var param in routine.Parameters)
        {
            if (param.TypeDescriptor.ActualDbType != param.NpgsqlDbType &&
                param.OriginalName is not null &&
                param.OriginalName.StartsWith('$'))
            {
                // Reverse-lookup: find the type name from the new NpgsqlDbType
                var typeName = MapNpgsqlDbTypeToTypeName(param.NpgsqlDbType);
                if (typeName is not null)
                {
                    retypedParams ??= new();
                    retypedParams[param.OriginalName] = typeName;
                }
            }
        }

        if (retypedParams is null)
        {
            return;
        }

        // Parse the SQL SELECT list to map column positions to $N references.
        // For "select $1 as a, upper($2) as b, $3 as c" → column 0 ← $1, column 2 ← $3
        // (column 1 has a function call, not a bare $N — skip)
        var expression = routine.Expression;
        var selectItems = ParseSelectListItems(expression);
        if (selectItems is null)
        {
            return;
        }

        for (int colIndex = 0; colIndex < selectItems.Length && colIndex < routine.ColumnsTypeDescriptor.Length; colIndex++)
        {
            var item = selectItems[colIndex];
            // Check if this select item is a bare $N (optionally with "as alias")
            // e.g., "$1 as status_code" or just "$1"
            var paramRef = ExtractBareParamRef(item);
            if (paramRef is not null && retypedParams.TryGetValue(paramRef, out var typeName))
            {
                // Only update if the column currently has text/unknown type
                var currentDescriptor = routine.ColumnsTypeDescriptor[colIndex];
                if (currentDescriptor.IsText || currentDescriptor.ActualDbType == NpgsqlTypes.NpgsqlDbType.Unknown)
                {
                    routine.ColumnsTypeDescriptor[colIndex] = new TypeDescriptor(typeName);
                }
            }
        }
    }

    /// <summary>
    /// Parse the SELECT list from a SQL expression into individual items.
    /// Returns null if the expression doesn't start with SELECT.
    /// Respects parentheses nesting (commas inside function calls are not split).
    /// </summary>
    private static string[]? ParseSelectListItems(string sql)
    {
        // Find "select" keyword (case-insensitive)
        var trimmed = sql.TrimStart();
        if (!trimmed.StartsWith("select", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Skip past "select" + whitespace
        var afterSelect = trimmed[6..].TrimStart();

        // Find the end of the SELECT list (FROM, WHERE, ;, or end of string)
        var listEnd = FindSelectListEnd(afterSelect);
        var selectList = afterSelect[..listEnd].Trim();
        if (selectList.Length == 0)
        {
            return null;
        }

        // Split by commas, respecting parentheses
        var items = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < selectList.Length; i++)
        {
            var c = selectList[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                items.Add(selectList[start..i].Trim());
                start = i + 1;
            }
        }
        items.Add(selectList[start..].Trim());

        return items.ToArray();
    }

    /// <summary>
    /// Find the end of the SELECT list (position of FROM, WHERE, ORDER BY, GROUP BY, LIMIT, ;, or end).
    /// </summary>
    private static int FindSelectListEnd(string sql)
    {
        int depth = 0;
        string[] keywords = ["from", "where", "order", "group", "having", "limit", "offset", "union", "intersect", "except"];

        for (int i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ';' && depth == 0) return i;
            else if (depth == 0 && char.IsWhiteSpace(c))
            {
                // Check if a keyword starts here
                foreach (var kw in keywords)
                {
                    if (i + 1 + kw.Length <= sql.Length &&
                        sql.AsSpan(i + 1, kw.Length).Equals(kw.AsSpan(), StringComparison.OrdinalIgnoreCase) &&
                        (i + 1 + kw.Length >= sql.Length || !char.IsLetterOrDigit(sql[i + 1 + kw.Length])))
                    {
                        return i;
                    }
                }
            }
        }
        return sql.Length;
    }

    /// <summary>
    /// Extract a bare $N reference from a SELECT item. Returns "$N" if the item is just
    /// "$N" or "$N as alias" (optionally with a ::type cast that we ignore).
    /// Returns null if the item has function calls, operators, or other complexity.
    /// </summary>
    private static string? ExtractBareParamRef(string item)
    {
        // Strip "as alias" suffix
        var asIndex = item.IndexOf(" as ", StringComparison.OrdinalIgnoreCase);
        var expr = asIndex >= 0 ? item[..asIndex].Trim() : item.Trim();

        // Strip ::type cast suffix (e.g., "$1::integer" → "$1")
        var castIndex = expr.IndexOf("::", StringComparison.Ordinal);
        if (castIndex >= 0)
        {
            expr = expr[..castIndex].Trim();
        }

        // Check if what remains is a bare $N reference
        if (expr.Length >= 2 && expr[0] == '$' && int.TryParse(expr[1..], out _))
        {
            return expr;
        }

        return null;
    }

    private static string? MapNpgsqlDbTypeToTypeName(NpgsqlTypes.NpgsqlDbType dbType)
    {
        return dbType switch
        {
            NpgsqlTypes.NpgsqlDbType.Smallint => "smallint",
            NpgsqlTypes.NpgsqlDbType.Integer => "integer",
            NpgsqlTypes.NpgsqlDbType.Bigint => "bigint",
            NpgsqlTypes.NpgsqlDbType.Numeric => "numeric",
            NpgsqlTypes.NpgsqlDbType.Real => "real",
            NpgsqlTypes.NpgsqlDbType.Double => "double precision",
            NpgsqlTypes.NpgsqlDbType.Boolean => "boolean",
            NpgsqlTypes.NpgsqlDbType.Text => "text",
            NpgsqlTypes.NpgsqlDbType.Varchar => "character varying",
            NpgsqlTypes.NpgsqlDbType.Json => "json",
            NpgsqlTypes.NpgsqlDbType.Jsonb => "jsonb",
            NpgsqlTypes.NpgsqlDbType.Uuid => "uuid",
            NpgsqlTypes.NpgsqlDbType.Date => "date",
            NpgsqlTypes.NpgsqlDbType.Timestamp => "timestamp",
            NpgsqlTypes.NpgsqlDbType.TimestampTz => "timestamptz",
            NpgsqlTypes.NpgsqlDbType.Time => "time",
            NpgsqlTypes.NpgsqlDbType.Interval => "interval",
            NpgsqlTypes.NpgsqlDbType.Bytea => "bytea",
            _ => null
        };
    }
}
