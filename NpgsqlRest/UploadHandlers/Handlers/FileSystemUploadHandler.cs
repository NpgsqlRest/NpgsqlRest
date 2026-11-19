using System.Text;
using Npgsql;

namespace NpgsqlRest.UploadHandlers.Handlers;

public class FileSystemUploadHandler() : BaseUploadHandler, IUploadHandler
{
    private string[]? _uploadedFiles = null;

    private const string PathParam = "path";
    private const string FileParam = "file";
    private const string UniqueNameParam = "unique_name";
    private const string CreatePathParam = "create_path";
    protected override IEnumerable<string> GetParameters()
    {
        yield return IncludedMimeTypeParam;
        yield return ExcludedMimeTypeParam;
        yield return BufferSizeParam;
        yield return PathParam;
        yield return FileParam;
        yield return UniqueNameParam;
        yield return CreatePathParam;
        yield return FileCheckExtensions.CheckTextParam;
        yield return FileCheckExtensions.CheckImageParam;
        yield return FileCheckExtensions.TestBufferSizeParam;
        yield return FileCheckExtensions.NonPrintableThresholdParam;
    }

    public bool RequiresTransaction => false;

    public async Task<string> UploadAsync(NpgsqlConnection connection, HttpContext context, Dictionary<string, string>? parameters, CancellationToken cancellationToken = default)
    {
        var basePath = Options.UploadOptions.DefaultUploadHandlerOptions.FileSystemPath;
        var useUniqueFileName = Options.UploadOptions.DefaultUploadHandlerOptions.FileSystemUseUniqueFileName;
        string? newFileName = null;
        bool createPathIfNotExists = Options.UploadOptions.DefaultUploadHandlerOptions.FileSystemCreatePathIfNotExists;
        bool checkText = Options.UploadOptions.DefaultUploadHandlerOptions.FileSystemCheckText;
        bool checkImage = Options.UploadOptions.DefaultUploadHandlerOptions.FileSystemCheckImage;
        int testBufferSize = Options.UploadOptions.DefaultUploadHandlerOptions.TextTestBufferSize;
        int nonPrintableThreshold = Options.UploadOptions.DefaultUploadHandlerOptions.TextNonPrintableThreshold;

        AllowedImageTypes allowedImage = Options.UploadOptions.DefaultUploadHandlerOptions.AllowedImageTypes;

        if (parameters is not null)
        {
            if (TryGetParam(parameters, PathParam, out var path) && !string.IsNullOrEmpty(path))
            {
                basePath = path;
            }
            if (TryGetParam(parameters, FileParam, out var newFileNameStr) && !string.IsNullOrEmpty(newFileNameStr))
            {
                newFileName = newFileNameStr;
            }
            if (TryGetParam(parameters, UniqueNameParam, out var useUniqueFileNameStr) 
                && bool.TryParse(useUniqueFileNameStr, out var useUniqueFileNameParsed))
            {
                useUniqueFileName = useUniqueFileNameParsed;
            }
            if (TryGetParam(parameters, CreatePathParam, out var createPathIfNotExistsStr)
                && bool.TryParse(createPathIfNotExistsStr, out var createPathIfNotExistsParsed))
            {
                createPathIfNotExists = createPathIfNotExistsParsed;
            }
            if (TryGetParam(parameters, FileCheckExtensions.CheckTextParam, out var checkTextParamStr)
                && bool.TryParse(checkTextParamStr, out var checkTextParamParsed))
            {
                checkText = checkTextParamParsed;
            }
            if (TryGetParam(parameters, FileCheckExtensions.CheckImageParam, out var checkImageParamStr))
            {
                if (bool.TryParse(checkImageParamStr, out var checkImageParamParsed))
                {
                    checkImage = checkImageParamParsed;
                }
                else
                {
                    checkImage = true;
                    allowedImage = checkImageParamStr.ParseImageTypes() ?? Options.UploadOptions.DefaultUploadHandlerOptions.AllowedImageTypes;
                }
            }
            if (TryGetParam(parameters, FileCheckExtensions.TestBufferSizeParam, out var testBufferSizeStr) && int.TryParse(testBufferSizeStr, out var testBufferSizeParsed))
            {
                testBufferSize = testBufferSizeParsed;
            }
            if (TryGetParam(parameters, FileCheckExtensions.NonPrintableThresholdParam, out var nonPrintableThresholdStr) && int.TryParse(nonPrintableThresholdStr, out var nonPrintableThresholdParsed))
            {
                nonPrintableThreshold = nonPrintableThresholdParsed;
            }
        }

        if (Options.UploadOptions.LogUploadParameters is true)
        {
            Logger?.LogTrace("Upload for {_type}: includedMimeTypePatterns={includedMimeTypePatterns}, excludedMimeTypePatterns={excludedMimeTypePatterns}, bufferSize={bufferSize}, basePath={basePath}, useUniqueFileName={useUniqueFileName}, newFileName={newFileName}, createPathIfNotExists={createPathIfNotExists}, checkText={checkText}, checkImage={checkImage}, allowedImage={allowedImage}, testBufferSize={testBufferSize}, nonPrintableThreshold={nonPrintableThreshold}",
                Type, IncludedMimeTypePatterns, ExcludedMimeTypePatterns, BufferSize, basePath, useUniqueFileName, newFileName, createPathIfNotExists, checkText, checkImage, allowedImage, testBufferSize, nonPrintableThreshold);
        }

        // The upload directory and the configured file name may be templated from request values (custom parameters
        // such as "file_system_path = {_path}"), so neither may navigate with ".." - and every final file path must
        // stay inside the upload directory (checked per file below).
        var basePathIsSafe = HasNoParentSegments(basePath);
        if (basePathIsSafe is false)
        {
            Logger?.LogWarning("Upload rejected: the upload path {path} contains \"..\" segments", basePath);
        }

        if (basePathIsSafe && createPathIfNotExists is true && Directory.Exists(basePath) is false)
        {
            Directory.CreateDirectory(basePath);
        }

        _uploadedFiles = new string[context.Request.Form.Files.Count];
        StringBuilder result = new(context.Request.Form.Files.Count * 100);
        result.Append('[');
        
        int fileId = 0;
        for (int i = 0; i < context.Request.Form.Files.Count; i++)
        {
            IFormFile formFile = context.Request.Form.Files[i];

            if (fileId > 0)
            {
                result.Append(',');
            }
            var currentFilePath = basePathIsSafe
                ? ResolveSafeFilePath(basePath, newFileName, formFile.FileName, useUniqueFileName)
                : null;

            // Build the result JSON
            if (Type is not null)
            {
                result.Append("{\"type\":");
                result.Append(SerializeString(Type));
                result.Append(",\"fileName\":");
            }
            else
            {
                result.Append("{\"fileName\":");
            }

            result.Append(SerializeString(formFile.FileName));
            result.Append(",\"contentType\":");
            result.Append(SerializeString(formFile.ContentType));
            result.Append(",\"size\":");
            result.Append(formFile.Length);
            result.Append(",\"filePath\":");
            result.Append(SerializeString(currentFilePath));

            UploadFileStatus status = UploadFileStatus.Ok;
            if (currentFilePath is null)
            {
                status = UploadFileStatus.InvalidFileName;
            }
            if (status == UploadFileStatus.Ok && StopAfterFirstSuccess is true && SkipFileNames.Contains(formFile.FileName, StringComparer.OrdinalIgnoreCase))
            {
                status = UploadFileStatus.Ignored;
            }
            if (status == UploadFileStatus.Ok && this.CheckMimeTypes(formFile.ContentType) is false)
            {
                status = UploadFileStatus.InvalidMimeType;
            }
            if (status == UploadFileStatus.Ok && (checkText is true || checkImage is true))
            {
                if (checkText is true)
                {
                    status = await formFile.CheckTextContentStatus(testBufferSize, nonPrintableThreshold, checkNewLines: false);
                }
                if (status == UploadFileStatus.Ok && checkImage is true)
                {
                    if (await formFile.IsImage(allowedImage) is false)
                    {
                        status = UploadFileStatus.InvalidImage;
                    }
                }
            }
            result.Append(",\"success\":");
            result.Append(status == UploadFileStatus.Ok ? "true" : "false");
            result.Append(",\"status\":");
            result.Append(SerializeString(status.ToString()));
            result.Append('}');
            if (status != UploadFileStatus.Ok)
            {
                Logger?.FileUploadFailed(Type, formFile.FileName, formFile.ContentType, formFile.Length, status);
                fileId++;
                continue;
            }
            if (StopAfterFirstSuccess is true)
            {
                SkipFileNames.Add(formFile.FileName);
            }

            using (var fileStream = new FileStream(currentFilePath!, FileMode.Create))
            {
                byte[] buffer = new byte[BufferSize];
                int bytesRead;
                using var sourceStream = formFile.OpenReadStream();

                while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }
            }
            _uploadedFiles[i] = currentFilePath!;

            if (Options.UploadOptions.LogUploadEvent)
            {
                Logger?.UploadedFileToFileSystem(formFile.FileName, formFile.ContentType, formFile.Length, currentFilePath!);
            }
            fileId++;
        }

        result.Append(']');
        return result.ToString();
    }

    /// <summary>
    /// The path to write an uploaded file to (<paramref name="basePath"/> combined with the file name, in the same form
    /// as before), or null when the name is not allowed.
    /// - a generated unique name keeps only the extension (never a directory part);
    /// - a configured name (the "file" parameter, possibly templated) must be relative and must not contain ".." segments;
    /// - the client-supplied name (multipart Content-Disposition) is reduced to its last segment, with both / and \\
    ///   treated as separators.
    /// The resulting full path must be inside <paramref name="basePath"/>.
    /// </summary>
    internal static string? ResolveSafeFilePath(string basePath, string? configuredFileName, string? clientFileName, bool useUniqueFileName)
    {
        string? relative;
        if (useUniqueFileName)
        {
            var source = LastSegment(configuredFileName ?? clientFileName);
            var extension = source is null ? "" : Path.GetExtension(source);
            relative = string.Concat(Guid.NewGuid().ToString(), extension);
        }
        else if (configuredFileName is not null)
        {
            var normalized = configuredFileName.Replace('\\', '/');
            relative = Path.IsPathRooted(normalized) || normalized.StartsWith('/') || HasNoParentSegments(normalized) is false
                ? null
                : normalized;
        }
        else
        {
            relative = LastSegment(clientFileName);
        }
        if (string.IsNullOrWhiteSpace(relative) || relative is "." or "..")
        {
            return null;
        }
        var filePath = Path.Combine(basePath, relative);
        var fullBasePath = Path.GetFullPath(basePath);
        var root = Path.EndsInDirectorySeparator(fullBasePath) ? fullBasePath : string.Concat(fullBasePath, Path.DirectorySeparatorChar.ToString());
        return Path.GetFullPath(filePath).StartsWith(root, StringComparison.Ordinal) ? filePath : null;
    }

    private static string? LastSegment(string? name)
    {
        if (name is null)
        {
            return null;
        }
        var normalized = name.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        return slash < 0 ? normalized : normalized[(slash + 1)..];
    }

    private static bool HasNoParentSegments(string path)
    {
        foreach (var segment in path.Replace('\\', '/').Split('/'))
        {
            if (segment == "..")
            {
                return false;
            }
        }
        return true;
    }

    public void OnError(NpgsqlConnection? connection, HttpContext context, Exception? exception)
    {
        if (_uploadedFiles is not null)
        {
            for(int i = 0; i < _uploadedFiles.Length; i++)
            {
                var filePath = _uploadedFiles[i];
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                }
                catch
                {
                    // Ignore any exceptions during cleanup
                }
            }
        }
    }
}