namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: parameter | param
    /// Syntax: param [param_name1] is hash of [param_name2]
    ///         param [param_name] is upload metadata
    ///
    /// Description: Configure parameter behavior, such as marking one parameter as a hash of another or as upload metadata.
    /// </summary>
    private static readonly string[] ParameterKey = [
        "parameter",
        "param",
    ];

    private static void HandleParameter(
        Routine routine,
        RoutineEndpoint endpoint,
        string[] wordsLower,
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
                        Logger?.CommentParamIsHashOf(description, paramName1, paramName2);
                    }
                }
                else
                {
                    Logger?.CommentParamNotExistsCantHash(description, paramName1);
                }
            }
        }

        // param param_name1 is upload metadata
        if (len >= 5 && (
            StrEquals(wordsLower[2], "is") && StrEquals(wordsLower[3], "upload") && StrEquals(wordsLower[4], "metadata")
            ))
        {
            if (endpoint.Upload is false)
            {
                endpoint.Upload = true;
                Logger?.CommentUpload(description);
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
                Logger?.CommentUploadWrongMetadataParam(description, paramName);
            }
            else
            {
                param.IsUploadMetadata = true;
                Logger?.CommentUploadMetadataParam(description, paramName);
            }
        }
    }
}
