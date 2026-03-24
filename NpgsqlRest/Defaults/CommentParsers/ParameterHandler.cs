namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: parameter | param
    /// Syntax: param [param_name1] is hash of [param_name2]
    ///         param [param_name] is upload metadata
    ///         param [original] [new_name]              — rename only (simplest form)
    ///         param [original] [new_name] [type]        — rename + retype (simplest form)
    ///         param [original] is [new_name]            — rename only ("is" style)
    ///         param [original] is [new_name] type is [type] — rename + retype ("is" style)
    ///
    /// Description: Configure parameter behavior, such as marking one parameter as a hash of another,
    /// as upload metadata, or renaming/retyping parameters.
    /// </summary>
    private static readonly string[] ParameterKey = [
        "parameter",
        "param",
    ];

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

        if (len >= 4 && StrEquals(wordsLower[2], "is"))
        {
            // "is" style: param [original] is [new_name] [type]
            newName = words[3]; // preserve original case for the new name

            if (len >= 5)
            {
                newType = wordsLower[4];
            }
        }
        else if (len == 3 && StrEquals(wordsLower[2], "is"))
        {
            // "param $1 is" — rename to literal "is"
            newName = words[2]; // "is"
        }
        else if (len >= 3 && !StrEquals(wordsLower[2], "is"))
        {
            // Simplest form: param [original] [new_name] [type]
            newName = words[2]; // preserve original case

            if (len >= 4)
            {
                newType = wordsLower[3];
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
                CommentLogger?.CommentParamRetyped(description, newName, newType);
            }
            else
            {
                Logger?.CommentParamNotExistsCantRename(description, $"unknown type '{newType}' for parameter {originalName}");
            }
        }
    }
}
