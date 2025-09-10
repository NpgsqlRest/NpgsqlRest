using System.Text;
using Microsoft.VisualBasic.FileIO;
using Npgsql;
using NpgsqlTypes;
using static NpgsqlRest.PgConverters;

namespace NpgsqlRest.UploadHandlers.Handlers;

public class CsvUploadHandler(
    NpgsqlRestUploadOptions options, 
    RetryStrategy? retryStrategy,
    ILogger? logger) : BaseUploadHandler, IUploadHandler
{
    private const string CheckFileParam = "check_format";
    private const string DelimitersParam = "delimiters";
    private const string HasFieldsEnclosedInQuotesParam = "has_fields_enclosed_in_quotes";
    private const string SetWhiteSpaceToNullParam = "set_white_space_to_null";
    private const string RowCommandParam = "row_command";
    
    protected override IEnumerable<string> GetParameters()
    {
        yield return IncludedMimeTypeParam;
        yield return ExcludedMimeTypeParam;
        yield return CheckFileParam;
        yield return FileCheckExtensions.TestBufferSizeParam;
        yield return FileCheckExtensions.NonPrintableThresholdParam;
        yield return DelimitersParam;
        yield return HasFieldsEnclosedInQuotesParam;
        yield return SetWhiteSpaceToNullParam;
        yield return RowCommandParam;
    }

    public bool RequiresTransaction => true;

    public async Task<string> UploadAsync(NpgsqlConnection connection, HttpContext context, Dictionary<string, string>? parameters)
    {
        bool checkFileStatus = options.DefaultUploadHandlerOptions.CsvUploadCheckFileStatus;
        int testBufferSize = options.DefaultUploadHandlerOptions.TextTestBufferSize;
        int nonPrintableThreshold = options.DefaultUploadHandlerOptions.TextNonPrintableThreshold;
        string delimiters = options.DefaultUploadHandlerOptions.CsvUploadDelimiterChars;
        bool hasFieldsEnclosedInQuotes = options.DefaultUploadHandlerOptions.CsvUploadHasFieldsEnclosedInQuotes;
        bool setWhiteSpaceToNull = options.DefaultUploadHandlerOptions.CsvUploadSetWhiteSpaceToNull;
        string rowCommand = options.DefaultUploadHandlerOptions.CsvUploadRowCommand;

        if (parameters is not null)
        {
            if (TryGetParam(parameters, CheckFileParam, out var checkFileStatusStr) && bool.TryParse(checkFileStatusStr, out var checkFileStatusParsed))
            {
                checkFileStatus = checkFileStatusParsed;
            }
            if (TryGetParam(parameters, FileCheckExtensions.TestBufferSizeParam, out var testBufferSizeStr) && int.TryParse(testBufferSizeStr, out var testBufferSizeParsed))
            {
                testBufferSize = testBufferSizeParsed;
            }
            if (TryGetParam(parameters, FileCheckExtensions.NonPrintableThresholdParam, out var nonPrintableThresholdStr) && int.TryParse(nonPrintableThresholdStr, out var nonPrintableThresholdParsed))
            {
                nonPrintableThreshold = nonPrintableThresholdParsed;
            }
            if (TryGetParam(parameters, DelimitersParam, out var delimitersStr) && delimitersStr is not null)
            {
                delimiters = delimitersStr!;
            }
            if (TryGetParam(parameters, HasFieldsEnclosedInQuotesParam, out var hasFieldsEnclosedInQuotesStr) && bool.TryParse(hasFieldsEnclosedInQuotesStr, out var hasFieldsEnclosedInQuotesParsed))
            {
                hasFieldsEnclosedInQuotes = hasFieldsEnclosedInQuotesParsed;
            }
            if (TryGetParam(parameters, SetWhiteSpaceToNullParam, out var setWhiteSpaceToNullStr) && bool.TryParse(setWhiteSpaceToNullStr, out var setWhiteSpaceToNullParsed))
            {
                setWhiteSpaceToNull = setWhiteSpaceToNullParsed;
            }
            if (TryGetParam(parameters, RowCommandParam, out var rowCommandStr) && rowCommandStr is not null)
            {
                rowCommand = rowCommandStr;
            }
        }

        if (options.LogUploadParameters is true)
        {
            logger?.LogDebug("Upload for {_type}: includedMimeTypePatterns={includedMimeTypePatterns}, excludedMimeTypePatterns={excludedMimeTypePatterns}, checkFileStatus={checkFileStatus}, testBufferSize={testBufferSize}, nonPrintableThreshold={nonPrintableThreshold}, delimiters={delimiters}, hasFieldsEnclosedInQuotes={hasFieldsEnclosedInQuotes}, setWhiteSpaceToNull={setWhiteSpaceToNull}, rowCommand={rowCommand}",
                _type, _includedMimeTypePatterns, _excludedMimeTypePatterns, checkFileStatus, testBufferSize, nonPrintableThreshold, delimiters, hasFieldsEnclosedInQuotes, setWhiteSpaceToNull, rowCommand);
        }

        string[] delimitersArr = [.. delimiters.Select(c => c.ToString())];
        using var command = new NpgsqlCommand(rowCommand, connection);
        var paramCount = rowCommand.PgCountParams();
        if (paramCount >= 1) command.Parameters.Add(NpgsqlRestParameter.CreateParamWithType(NpgsqlDbType.Integer));
        if (paramCount >= 2) command.Parameters.Add(NpgsqlRestParameter.CreateParamWithType(NpgsqlDbType.Text | NpgsqlDbType.Array));
        if (paramCount >= 3) command.Parameters.Add(new NpgsqlParameter());
        if (paramCount >= 4) command.Parameters.Add(NpgsqlRestParameter.CreateParamWithType(NpgsqlDbType.Json));

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

            StringBuilder fileJson = new(100);
            if (_type is not null)
            {
                fileJson.Append("{\"type\":");
                fileJson.Append(SerializeString(_type));
                fileJson.Append(",\"fileName\":");
            }
            else
            {
                fileJson.Append("{\"fileName\":");
            }
            fileJson.Append(SerializeString(formFile.FileName));
            fileJson.Append(",\"contentType\":");
            fileJson.Append(SerializeString(formFile.ContentType));
            fileJson.Append(",\"size\":");
            fileJson.Append(formFile.Length);

            UploadFileStatus status = UploadFileStatus.Ok;
            if (_stopAfterFirstSuccess is true && _skipFileNames.Contains(formFile.FileName, StringComparer.OrdinalIgnoreCase))
            {
                status = UploadFileStatus.Ignored;
            }
            if (status == UploadFileStatus.Ok && this.CheckMimeTypes(formFile.ContentType) is false)
            {
                status = UploadFileStatus.InvalidMimeType;
            }
            if (status == UploadFileStatus.Ok && checkFileStatus is true)
            {
                status = await formFile.CheckTextContentStatus(testBufferSize, nonPrintableThreshold, checkNewLines: true);
            }
            fileJson.Append(",\"success\":");
            fileJson.Append(status == UploadFileStatus.Ok ? "true" : "false");
            fileJson.Append(",\"status\":");
            fileJson.Append(SerializeString(status.ToString()));
            fileJson.Append('}');
            if (status != UploadFileStatus.Ok)
            {
                logger?.FileUploadFailed(_type, formFile.FileName, formFile.ContentType, formFile.Length, status);
                result.Append(fileJson);
                fileId++;
                continue;
            }
            if (_stopAfterFirstSuccess is true)
            {
                _skipFileNames.Add(formFile.FileName);
            }

            using var fileStream = formFile.OpenReadStream();
            using var streamReader = new StreamReader(fileStream);

            int rowIndex = 1;
            object? commandResult = null;
            while (await streamReader.ReadLineAsync() is { } line)
            {
                using var parser = new TextFieldParser(new StringReader(line));
                parser.SetDelimiters(delimitersArr);
                parser.HasFieldsEnclosedInQuotes = hasFieldsEnclosedInQuotes;
                string?[]? values = setWhiteSpaceToNull ? 
                    parser.ReadFields()?.Select(field => string.IsNullOrWhiteSpace(field) ? null : field).ToArray() :
                    parser.ReadFields()?.ToArray();

                if (paramCount >= 1)
                {
                    command.Parameters[0].Value = rowIndex;
                }
                if (paramCount >= 2)
                {
                    command.Parameters[1].Value = values ?? (object)DBNull.Value;
                }
                if (paramCount >= 3)
                {
                    command.Parameters[2].Value = commandResult ?? DBNull.Value;
                }
                if (paramCount >= 4)
                {
                    command.Parameters[3].Value = fileJson.ToString();
                }
                commandResult = await command.ExecuteScalarWithRetryAsync(retryStrategy, logger);

                rowIndex++;
            }
            fileJson[^1] = ',';
            fileJson.Append("\"lastResult\":");
            fileJson.Append(SerializeDatbaseObject(commandResult));
            fileJson.Append('}');

            if (options.LogUploadEvent)
            {
                logger?.UploadedCsvFile(formFile.FileName, formFile.ContentType, formFile.Length, rowCommand);
            }
            result.Append(fileJson);
            fileId++;
        }

        result.Append(']');
        return result.ToString();
    }

    public void OnError(NpgsqlConnection? connection, HttpContext context, Exception? exception)
    {
    }
}
