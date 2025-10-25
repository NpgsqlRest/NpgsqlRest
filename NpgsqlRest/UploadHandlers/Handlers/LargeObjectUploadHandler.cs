using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace NpgsqlRest.UploadHandlers.Handlers;

public class LargeObjectUploadHandler(RetryStrategy? retryStrategy) : BaseUploadHandler, IUploadHandler
{
    private const string OidParam = "oid";
    protected override IEnumerable<string> GetParameters()
    {
        yield return IncludedMimeTypeParam;
        yield return ExcludedMimeTypeParam;
        yield return BufferSizeParam;
        yield return OidParam;
        yield return FileCheckExtensions.CheckTextParam;
        yield return FileCheckExtensions.CheckImageParam;
        yield return FileCheckExtensions.TestBufferSizeParam;
        yield return FileCheckExtensions.NonPrintableThresholdParam;
    }

    public bool RequiresTransaction => true;

    public async Task<string> UploadAsync(NpgsqlConnection connection, HttpContext context, Dictionary<string, string>? parameters)
    {
        long? oid = null;
        bool checkText = Options.UploadOptions.DefaultUploadHandlerOptions.LargeObjectCheckText;
        bool checkImage = Options.UploadOptions.DefaultUploadHandlerOptions.LargeObjectCheckImage;
        int testBufferSize = Options.UploadOptions.DefaultUploadHandlerOptions.TextTestBufferSize;
        int nonPrintableThreshold = Options.UploadOptions.DefaultUploadHandlerOptions.TextNonPrintableThreshold;
        AllowedImageTypes allowedImage = Options.UploadOptions.DefaultUploadHandlerOptions.AllowedImageTypes;

        if (parameters is not null)
        {
            if (TryGetParam(parameters, OidParam, out var oidStr) && long.TryParse(oidStr, out var oidParsed))
            {
                oid = oidParsed;
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
            Logger?.LogDebug("Upload for {_type}: includedMimeTypePatterns={includedMimeTypePatterns}, excludedMimeTypePatterns={excludedMimeTypePatterns}, bufferSize={bufferSize}, oid={oid}, checkText={checkText}, checkImage={checkImage}, allowedImage={allowedImage}, testBufferSize={testBufferSize}, nonPrintableThreshold={nonPrintableThreshold}", 
                Type, IncludedMimeTypePatterns, ExcludedMimeTypePatterns, BufferSize, oid, checkText, checkImage, allowedImage, testBufferSize, nonPrintableThreshold);
        }

        StringBuilder result = new(context.Request.Form.Files.Count*100);
        result.Append('[');
        int fileId = 0;
        for (int i = 0; i < context.Request.Form.Files.Count; i++)
        {
            IFormFile formFile = context.Request.Form.Files[i];
            if (fileId > 0)
            {
                result.Append(',');
            }

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

            UploadFileStatus status = UploadFileStatus.Ok;
            if (StopAfterFirstSuccess is true && SkipFileNames.Contains(formFile.FileName, StringComparer.OrdinalIgnoreCase))
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
            if (status != UploadFileStatus.Ok)
            {
                Logger?.FileUploadFailed(Type, formFile.FileName, formFile.ContentType, formFile.Length, status);
                result.Append(",\"oid\":null}");
                fileId++;
                continue;
            }
            if (StopAfterFirstSuccess is true)
            {
                SkipFileNames.Add(formFile.FileName);
            }

            result.Append(",\"oid\":");
            using var command = new NpgsqlCommand(oid is null ? "select lo_create(0)" : string.Concat("select lo_create(", oid.ToString(), ")"), connection);
            var resultOid = await command.ExecuteScalarWithRetryAsync(retryStrategy);
            
            result.Append(resultOid);
            result.Append('}');

            command.CommandText = "select lo_put($1,$2,$3)";
            command.Parameters.Add(NpgsqlRestParameter.CreateParamWithType(NpgsqlDbType.Oid));
            command.Parameters[0].Value = resultOid;
            command.Parameters.Add(NpgsqlRestParameter.CreateParamWithType(NpgsqlDbType.Bigint));
            command.Parameters.Add(NpgsqlRestParameter.CreateParamWithType(NpgsqlDbType.Bytea));

            using var fileStream = formFile.OpenReadStream();
            byte[] buffer = new byte[BufferSize];
            int bytesRead;
            long offset = 0;
            while ((bytesRead = await fileStream.ReadAsync(buffer)) > 0)
            {
                command.Parameters[1].Value = offset;
                command.Parameters[2].Value = buffer.Take(bytesRead).ToArray();
                await command.ExecuteNonQueryWithRetryAsync(retryStrategy);
                offset += bytesRead;
            }
            if (Options.UploadOptions.LogUploadEvent)
            {
                Logger?.UploadedFileToLargeObject(formFile.FileName, formFile.ContentType, formFile.Length, resultOid);
            }
            fileId++;
        }

        result.Append(']');
        return result.ToString();
    }

    public void OnError(NpgsqlConnection? connection, HttpContext context, Exception? exception)
    {
    }
}
