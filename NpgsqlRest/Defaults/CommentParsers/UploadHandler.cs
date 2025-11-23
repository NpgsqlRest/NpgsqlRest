namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Annotation: upload
    /// Syntax: upload
    ///         upload for [handler_name1, handler_name2 [, ...]]
    ///         upload [param_name] as metadata
    ///
    /// Description: Enable file upload for this endpoint.
    /// </summary>
    private const string UploadKey = "upload";

    private static void HandleUpload(
        Routine routine,
        RoutineEndpoint endpoint,
        string[] wordsLower,
        int len,
        string description)
    {
        if (Options.UploadOptions.UploadHandlers is null || Options.UploadOptions.UploadHandlers.Count == 0)
        {
            Logger?.CommentUploadNoHandlers(description);
        }
        else
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
            if (len >= 3 && StrEquals(wordsLower[1], "for"))
            {
                HashSet<string> existingHandlers = Options.UploadOptions.UploadHandlers?.Keys.ToHashSet() ?? [];
                var handlers = wordsLower[2..]
                    .Select(w =>
                    {
                        var handler = w.TrimEnd(',');
                        bool exists = true;
                        if (existingHandlers.Contains(handler) is false)
                        {
                            Logger?.CommentUploadHandlerNotExists(description, handler, existingHandlers);
                            exists = false;
                        }
                        return new { exists, handler };
                    })
                    .Where(x => x.exists is true)
                    .Select(x => x.handler)
                    .ToArray();

                endpoint.UploadHandlers = handlers;
                if (handlers.Length == 0)
                {
                    var first = Options.UploadOptions.UploadHandlers?.Keys.FirstOrDefault();
                    Logger?.CommentUploadFirstAvaialbleHandler(description, first);
                }
                if (handlers.Length == 1)
                {
                    Logger?.CommentUploadSingleHandler(description, handlers[0]);
                }
                else
                {
                    Logger?.CommentUploadHandlers(description, handlers);
                }
            }

            else if (len >= 4 && StrEquals(wordsLower[2], "as") && StrEquals(wordsLower[3], "metadata"))
            {
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
}
