namespace NpgsqlRest.UploadHandlers.Handlers;

public abstract class BaseUploadHandler
{
    protected HashSet<string> SkipFileNames = new(StringComparer.OrdinalIgnoreCase);

    protected string? Type = null;
    protected bool CheckMimeTypes(string contentType)
    {
        // File must match AT LEAST ONE included pattern
        if (IncludedMimeTypePatterns is not null && IncludedMimeTypePatterns.Length > 0)
        {
            bool matchesAny = false;
            for (int j = 0; j < IncludedMimeTypePatterns.Length; j++)
            {
                if (Parser.IsPatternMatch(contentType, IncludedMimeTypePatterns[j]))
                {
                    matchesAny = true;
                    break;
                }
            }

            if (!matchesAny)
            {
                return false;
            }
        }

        // File must NOT match ANY excluded patterns
        if (ExcludedMimeTypePatterns is not null)
        {
            for (int j = 0; j < ExcludedMimeTypePatterns.Length; j++)
            {
                if (Parser.IsPatternMatch(contentType, ExcludedMimeTypePatterns[j]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    protected bool TryGetParam(Dictionary<string, string> parameters, string key, out string value)
    {
        if (parameters.TryGetValue(key, out var val))
        {
            value = val;
            return true;
        }
        if (parameters.TryGetValue(string.Concat(Type, "_", key), out val))
        {
            value = val;
            return true;
        }
        value = default!;
        return false;
    }

    protected abstract IEnumerable<string> GetParameters();

    protected string[]? IncludedMimeTypePatterns = null;
    protected string[]? ExcludedMimeTypePatterns = null;
    protected int BufferSize = 0;
    protected bool StopAfterFirstSuccess = false;

    public void ParseSharedParameters(NpgsqlRestUploadOptions options, Dictionary<string, string>? parameters)
    {
        IncludedMimeTypePatterns = options.DefaultUploadHandlerOptions.IncludedMimeTypePatterns;
        ExcludedMimeTypePatterns = options.DefaultUploadHandlerOptions.ExcludedMimeTypePatterns;
        BufferSize = options.DefaultUploadHandlerOptions.BufferSize;
        StopAfterFirstSuccess = options.DefaultUploadHandlerOptions.StopAfterFirstSuccess;

        if (parameters is not null)
        {
            if (TryGetParam(parameters, IncludedMimeTypeParam, out var includedMimeTypeStr) && includedMimeTypeStr is not null)
            {
                IncludedMimeTypePatterns = includedMimeTypeStr.SplitParameter();
            }
            if (TryGetParam(parameters, ExcludedMimeTypeParam, out var excludedMimeTypeStr) && excludedMimeTypeStr is not null)
            {
                ExcludedMimeTypePatterns = excludedMimeTypeStr.SplitParameter();
            }
            if (TryGetParam(parameters, BufferSizeParam, out var bufferSizeStr) && int.TryParse(bufferSizeStr, out var bufferSizeParsed))
            {
                BufferSize = bufferSizeParsed;
            }
            if (TryGetParam(parameters, StopAfterFirstParam, out var stopAfterFirstSuccessStr) && bool.TryParse(stopAfterFirstSuccessStr, out var stopAfterFirstSuccessParsed))
            {
                StopAfterFirstSuccess = stopAfterFirstSuccessParsed;
            }
        }
    }

    public IUploadHandler SetType(string type)
    {
        Type = type;
        return (this as IUploadHandler)!;
    }

    public IEnumerable<string> Parameters
    {
        get
        {
            yield return StopAfterFirstParam;
            foreach (var param in GetParameters())
            {
                yield return param;
            }
            foreach (var param in GetParameters())
            {
                yield return string.Concat(Type, "_", param);
            }
        }
    }

    public const string StopAfterFirstParam = "stop_after_first_success";
    public const string IncludedMimeTypeParam = "included_mime_types";
    public const string ExcludedMimeTypeParam = "excluded_mime_types";
    public const string BufferSizeParam = "buffer_size";

    public bool StopAfterFirst => StopAfterFirstSuccess;
    
    public void SetSkipFileNames(HashSet<string> skipFileNames)
    {
        SkipFileNames = skipFileNames;
    }

    public HashSet<string> GetSkipFileNames => SkipFileNames;
}
