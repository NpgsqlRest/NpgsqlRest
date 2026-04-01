namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: parameter | param
    /// Syntax: param [param_name1] is hash of [param_name2]
    ///         param [param_name] is upload metadata
    ///         param [param_name] default [value]        — set default value
    ///         param [original] [new_name]              — rename only (simplest form)
    ///         param [original] [new_name] [type]        — rename + retype (simplest form)
    ///         param [original] is [new_name]            — rename only ("is" style)
    ///         param [original] is [new_name] type is [type] — rename + retype ("is" style)
    ///
    /// Description: Configure parameter behavior, such as marking one parameter as a hash of another,
    /// as upload metadata, setting default values, or renaming/retyping parameters.
    /// </summary>
    private static readonly string[] ParameterKey = [
        "parameter",
        "param",
    ];

    /// <summary>
    /// Reserved keywords that cannot be used as parameter names in rename annotations.
    /// These are keywords used within @param annotation parsing that would cause ambiguity.
    /// </summary>
    private static readonly HashSet<string> ReservedParamKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "default", "is", "hash", "of", "upload", "metadata", "type",
    };

    private static void HandleParameter(
        Routine routine,
        RoutineEndpoint endpoint,
        string[] wordsLower,
        string[] words,
        int len,
        string description)
    {
        // param param_name1 is hash of param_name2
        if (len >= 6 && StrEquals(wordsLower[2], "is") && StrEquals(wordsLower[3], "hash") && StrEquals(wordsLower[4], "of"))
        {
            var paramName1 = wordsLower[1];
            var paramName2 = wordsLower[5];

            var found = true;
            NpgsqlRestParameter? param = null;

            if (routine.OriginalParamsHash.Contains(paramName1) is false &&
                routine.ParamsHash.Contains(paramName1) is false)
            {
                Logger?.CommentParamNotExistsCantHash(description, paramName1);
                found = false;
            }

            if (found is true &&
                routine.OriginalParamsHash.Contains(paramName2) is false &&
                routine.ParamsHash.Contains(paramName2) is false)
            {
                Logger?.CommentParamNotExistsCantHash(description, paramName2);
                found = false;
            }

            if (found is true)
            {
                param = routine.Parameters.FirstOrDefault(x =>
                    string.Equals(x.ActualName, paramName1, StringComparison.Ordinal) ||
                    string.Equals(x.ConvertedName, paramName1, StringComparison.Ordinal));
                if (param is not null)
                {
                    param.HashOf = routine.Parameters.FirstOrDefault(x =>
                        string.Equals(x.ActualName, paramName2, StringComparison.Ordinal) ||
                        string.Equals(x.ConvertedName, paramName2, StringComparison.Ordinal));
                    if (param.HashOf is null)
                    {
                        Logger?.CommentParamNotExistsCantHash(description, paramName2);
                    }
                    else
                    {
                        CommentLogger?.CommentParamIsHashOf(description, paramName1, paramName2);
                    }
                }
                else
                {
                    Logger?.CommentParamNotExistsCantHash(description, paramName1);
                }
            }
            return;
        }

        // param param_name1 is upload metadata
        if (len >= 5 && StrEquals(wordsLower[2], "is") && StrEquals(wordsLower[3], "upload") && StrEquals(wordsLower[4], "metadata"))
        {
            if (endpoint.Upload is false)
            {
                endpoint.Upload = true;
                CommentLogger?.CommentUpload(description);
            }
            if (endpoint.RequestParamType != RequestParamType.QueryString)
            {
                endpoint.RequestParamType = RequestParamType.QueryString;
            }
            if (endpoint.Method != Method.POST)
            {
                endpoint.Method = Method.POST;
            }

            var paramName = wordsLower[1];
            NpgsqlRestParameter? param = routine.Parameters.FirstOrDefault(x =>
                    string.Equals(x.ActualName, paramName, StringComparison.Ordinal) ||
                    string.Equals(x.ConvertedName, paramName, StringComparison.Ordinal));
            if (param is null)
            {
                CommentLogger?.CommentUploadWrongMetadataParam(description, paramName);
            }
            else
            {
                param.IsUploadMetadata = true;
                CommentLogger?.CommentUploadMetadataParam(description, paramName);
            }
            return;
        }

        // param [param_name] default [value]
        // Default value: null → DBNull, 'text' → string, number → string, true/false → string
        if (len >= 3 && StrEquals(wordsLower[2], "default"))
        {
            HandleParameterDefault(routine, wordsLower, words, len, description);
            return;
        }

        // Rename / retype forms:
        // param [original] is [new_name]                     — len >= 4, wordsLower[2] == "is"
        // param [original] is [new_name] [type]              — len >= 5, wordsLower[2] == "is"
        // param [original] [new_name]                        — len >= 3
        // param [original] [new_name] [type]                 — len >= 4
        if (len >= 2)
        {
            HandleParameterRename(routine, wordsLower, words, len, description);
        }
    }

    private static void HandleParameterRename(
        Routine routine,
        string[] wordsLower,
        string[] words,
        int len,
        string description)
    {
        var originalName = wordsLower[1];
        string? newName = null;
        string? newType = null;

        // Track whether a trailing "default [value]" was found after rename
        int defaultStartIndex = -1;

        if (len >= 4 && StrEquals(wordsLower[2], "is"))
        {
            // "is" style: param [original] is [new_name] [type | default ...]
            newName = words[3]; // preserve original case for the new name

            if (len >= 5 && StrEquals(wordsLower[4], "default"))
            {
                // "param $1 is _name default [value]" — rename + default
                defaultStartIndex = 5;
            }
            else if (len >= 5)
            {
                newType = wordsLower[4];
                // Check for "default" after type: "param $1 is _name integer default [value]"
                if (len >= 6 && StrEquals(wordsLower[5], "default"))
                {
                    defaultStartIndex = 6;
                }
            }
        }
        else if (len == 3 && StrEquals(wordsLower[2], "is"))
        {
            // "param $1 is" — rename to literal "is"
            newName = words[2]; // "is"
        }
        else if (len >= 3 && !StrEquals(wordsLower[2], "is"))
        {
            // Simplest form: param [original] [new_name] [type | default ...]
            newName = words[2]; // preserve original case

            if (len >= 4 && StrEquals(wordsLower[3], "default"))
            {
                // "param $1 _name default [value]" — rename + default
                defaultStartIndex = 4;
            }
            else if (len >= 4)
            {
                newType = wordsLower[3];
                // Check for "default" after type: "param $1 _name integer default [value]"
                if (len >= 5 && StrEquals(wordsLower[4], "default"))
                {
                    defaultStartIndex = 5;
                }
            }
        }
        else if (len == 2)
        {
            // Just "param $1" — not enough tokens for rename, skip
            return;
        }

        if (newName is null)
        {
            return;
        }

        // Validate the new parameter name
        var validationError = ValidateParameterName(newName);
        if (validationError is not null)
        {
            Logger?.CommentParamInvalidName(description, newName, validationError);
            return;
        }

        // Find the parameter by original name (matching ActualName or ConvertedName)
        var param = routine.Parameters.FirstOrDefault(x =>
            string.Equals(x.ActualName, originalName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.ConvertedName, originalName, StringComparison.OrdinalIgnoreCase));

        if (param is null)
        {
            Logger?.CommentParamNotExistsCantRename(description, originalName);
            return;
        }

        // Apply rename
        var oldConvertedName = param.ConvertedName;
        var oldActualName = param.ActualName;
        param.ConvertedName = newName;

        // Always update ActualName — the annotation is authoritative for the parameter identity.
        // SQL formatting uses OriginalName (set once at construction, never changes).
        if (oldActualName is null || !string.Equals(oldActualName, newName, StringComparison.Ordinal))
        {
            param.ActualName = newName;
            if (oldActualName is not null)
            {
                routine.OriginalParamsHash.Remove(oldActualName);
            }
            routine.OriginalParamsHash.Add(newName);
        }

        // Update ParamsHash: remove old, add new
        routine.ParamsHash.Remove(oldConvertedName);
        routine.ParamsHash.Add(newName);

        // Re-evaluate claim mappings and other name-based bindings against the new name.
        // At construction time, these were checked against the original ActualName (e.g., "$1").
        // After rename, the new name may match a claim mapping (e.g., "_user_id").
        if (Options.AuthenticationOptions.ParameterNameClaimsMapping.TryGetValue(newName, out var claimName))
        {
            param.UserClaim = claimName;
        }
        if (string.Equals(Options.AuthenticationOptions.IpAddressParameterName, newName, StringComparison.OrdinalIgnoreCase))
        {
            param.IsIpAddress = true;
        }
        if (string.Equals(Options.AuthenticationOptions.ClaimsJsonParameterName, newName, StringComparison.OrdinalIgnoreCase))
        {
            param.IsUserClaims = true;
        }

        CommentLogger?.CommentParamRenamed(description, originalName, newName);

        // Apply retype if specified
        if (newType is not null)
        {
            var typeDescriptor = new TypeDescriptor(newType);
            if (typeDescriptor.DbType != NpgsqlTypes.NpgsqlDbType.Unknown)
            {
                param.NpgsqlDbType = typeDescriptor.ActualDbType;
                if (param.TypeDescriptor.HasDefault)
                {
                    typeDescriptor.SetHasDefault();
                }
                param.TypeDescriptor = typeDescriptor;
                CommentLogger?.CommentParamRetyped(description, newName, newType);
            }
            else
            {
                Logger?.CommentParamNotExistsCantRename(description, $"unknown type '{newType}' for parameter {originalName}");
            }
        }

        // Apply inline default value if "default [value]" was found after name/type
        if (defaultStartIndex >= 0)
        {
            object defaultValue;
            if (defaultStartIndex >= len)
            {
                // "param $1 _name default" with no value → null
                defaultValue = DBNull.Value;
                CommentLogger?.CommentParamDefault(description, newName, "NULL");
            }
            else
            {
                defaultValue = ParseDefaultValue(words, wordsLower, defaultStartIndex, len);
                CommentLogger?.CommentParamDefault(description, newName, defaultValue is DBNull ? "NULL" : defaultValue?.ToString() ?? "NULL");
            }
            param.TypeDescriptor.SetHasDefault();
            param.DefaultValue = defaultValue;
        }
    }

    /// <summary>
    /// Handle: param [param_name] default [value]
    /// Values: null → DBNull.Value, 'quoted text' → string, unquoted → string
    /// </summary>
    private static void HandleParameterDefault(
        Routine routine,
        string[] wordsLower,
        string[] words,
        int len,
        string description)
    {
        var paramName = wordsLower[1];

        var param = routine.Parameters.FirstOrDefault(x =>
            string.Equals(x.ActualName, paramName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.ConvertedName, paramName, StringComparison.OrdinalIgnoreCase));

        if (param is null)
        {
            Logger?.CommentParamNotExistsCantDefault(description, paramName);
            return;
        }

        // "param x default" with no value → treat as default null
        if (len == 3)
        {
            param.TypeDescriptor.SetHasDefault();
            param.DefaultValue = DBNull.Value;
            CommentLogger?.CommentParamDefault(description, paramName, "NULL");
            return;
        }

        // Parse the default value from words[3..]
        var defaultValue = ParseDefaultValue(words, wordsLower, 3, len);
        param.TypeDescriptor.SetHasDefault();
        param.DefaultValue = defaultValue;
        CommentLogger?.CommentParamDefault(description, paramName, defaultValue is DBNull ? "NULL" : defaultValue?.ToString() ?? "NULL");
    }

    /// <summary>
    /// Parse a default value from annotation words starting at the given index.
    /// - "null" (unquoted, case-insensitive) → DBNull.Value
    /// - 'single quoted' → string (supports multi-word: joins tokens between opening and closing quote)
    /// - anything else → raw string value
    /// </summary>
    private static object ParseDefaultValue(string[] words, string[] wordsLower, int startIndex, int len)
    {
        // Single token
        if (len == startIndex + 1)
        {
            var token = words[startIndex];
            var tokenLower = wordsLower[startIndex];

            if (tokenLower == "null")
            {
                return DBNull.Value;
            }

            // Strip surrounding single quotes from a single-word quoted value: 'value'
            if (token.Length >= 2 && token[0] == '\'' && token[^1] == '\'')
            {
                return token[1..^1];
            }

            return token;
        }

        // Multiple tokens — check for single-quoted multi-word string: 'hello world'
        var first = words[startIndex];
        var last = words[len - 1];

        if (first.Length >= 1 && first[0] == '\'' && last.Length >= 1 && last[^1] == '\'')
        {
            // Join all tokens, strip the surrounding quotes
            var joined = string.Join(" ", words[startIndex..len]);
            return joined[1..^1];
        }

        // Not quoted — join as-is (unusual but handle gracefully)
        return string.Join(" ", words[startIndex..len]);
    }

    /// <summary>
    /// Validate that a parameter name is a valid PostgreSQL identifier and not a reserved annotation keyword.
    /// Valid: starts with letter or underscore, followed by letters, digits, underscores, or dollar signs.
    /// Also allows positional parameters ($1, $2, etc.).
    /// Returns null if valid, or an error reason string if invalid.
    /// </summary>
    private static string? ValidateParameterName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "name is empty";
        }

        // Allow positional parameters: $1, $2, etc.
        if (name[0] == '$')
        {
            for (int i = 1; i < name.Length; i++)
            {
                if (!char.IsAsciiDigit(name[i]))
                {
                    return $"positional parameter '{name}' contains non-digit character '{name[i]}'";
                }
            }
            return name.Length > 1 ? null : "positional parameter '$' has no number";
        }

        // Check reserved keywords
        if (ReservedParamKeywords.Contains(name))
        {
            return $"'{name}' is a reserved annotation keyword";
        }

        // First character: letter or underscore
        var first = name[0];
        if (!char.IsLetter(first) && first != '_')
        {
            return $"must start with a letter or underscore, not '{first}'";
        }

        // Subsequent characters: letter, digit, underscore, or dollar sign
        for (int i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '$')
            {
                return $"contains invalid character '{c}' at position {i}";
            }
        }

        return null;
    }
}
