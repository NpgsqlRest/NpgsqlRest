using System.Text;
using ExcelDataReader;
using Npgsql;
using NpgsqlRest;
using NpgsqlRest.UploadHandlers;
using NpgsqlRest.UploadHandlers.Handlers;
using NpgsqlTypes;

namespace NpgsqlRestClient;

public class ExcelUploadOptions
{
    private static readonly ExcelUploadOptions _instance = new();
    public static ExcelUploadOptions Instance => _instance;

    public string? ExcelSheetName { get; set; } = null;
    public bool ExcelAllSheets { get; set; } = false;
    public string ExcelTimeFormat { get; set; } = "HH:mm:ss";
    public string ExcelDateFormat { get; set; } = "yyyy-MM-dd";
    public string ExcelDateTimeFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";
    public bool ExcelRowDataAsJson { get; set; } = false;
    public string ExcelUploadRowCommand { get; set; } = "call process_excel_row($1,$2,$3,$4)";
}

public class ExcelUploadHandler(
    NpgsqlRestUploadOptions options, 
    RetryStrategy? cmdRetryStrategy,
    ILogger? logger) : BaseUploadHandler, IUploadHandler
{
    private const string SheetNameParam = "sheet_name";
    private const string AllSheetsParam = "all_sheets";
    private const string TimeFormatParam = "time_format";
    private const string DateFormatParam = "date_format";
    private const string DateTimeFormatParam = "datetime_format";
    private const string JsonRowDataParam = "row_is_json";
    private const string RowCommandParam = "row_command";

    protected override IEnumerable<string> GetParameters()
    {
        yield return IncludedMimeTypeParam;
        yield return ExcludedMimeTypeParam;
        yield return SheetNameParam;
        yield return AllSheetsParam;
        yield return TimeFormatParam;
        yield return DateFormatParam;
        yield return DateTimeFormatParam;
        yield return JsonRowDataParam;
        yield return RowCommandParam;
    }

    public bool RequiresTransaction => true;

    private string _timeFormat = default!;
    private string _dateFormat = default!;
    private string _dateTimeFormat = default!;

    public async Task<string> UploadAsync(NpgsqlConnection connection, HttpContext context, Dictionary<string, string>? parameters)
    {
        string? targetSheetName = ExcelUploadOptions.Instance.ExcelSheetName;
        bool allSheets = ExcelUploadOptions.Instance.ExcelAllSheets;
        bool dataAsJson = ExcelUploadOptions.Instance.ExcelRowDataAsJson;
        string rowCommand = ExcelUploadOptions.Instance.ExcelUploadRowCommand;

        _timeFormat = ExcelUploadOptions.Instance.ExcelTimeFormat;
        _dateFormat = ExcelUploadOptions.Instance.ExcelDateFormat;
        _dateTimeFormat = ExcelUploadOptions.Instance.ExcelDateTimeFormat;

        if (parameters is not null)
        {
            if (TryGetParam(parameters, SheetNameParam, out var sheetNameStr))
            {
                targetSheetName = sheetNameStr;
            }
            if (TryGetParam(parameters, AllSheetsParam, out var allSheetsStr) && bool.TryParse(allSheetsStr, out var allSheetsParsed))
            {
                allSheets = allSheetsParsed;
            }
            if (TryGetParam(parameters, TimeFormatParam, out var timeFormatStr))
            {
                _timeFormat = timeFormatStr;
            }
            if (TryGetParam(parameters, DateFormatParam, out var dateFormatStr))
            {
                _dateFormat = dateFormatStr;
            }
            if (TryGetParam(parameters, DateTimeFormatParam, out var dateTimeFormatStr))
            {
                _dateTimeFormat = dateTimeFormatStr;
            }
            if (TryGetParam(parameters, JsonRowDataParam, out var dataAsJsonStr) && bool.TryParse(dataAsJsonStr, out var dataAsJsonParsed))
            {
                dataAsJson = dataAsJsonParsed;
            }
            if (TryGetParam(parameters, RowCommandParam, out var rowCommandStr))
            {
                rowCommand = rowCommandStr;
            }
        }

        if (options.LogUploadParameters is true)
        {
            logger?.LogDebug("Upload for Excel: includedMimeTypePatterns={includedMimeTypePatterns}, excludedMimeTypePatterns={excludedMimeTypePatterns}, targetSheetName={targetSheetName}, allSheets={allSheets}, timeFormat={timeFormat}, dateFormat={dateFormat}, dateTimeFormat={dateTimeFormat}, rowCommand={rowCommand}",
                _includedMimeTypePatterns, _excludedMimeTypePatterns, targetSheetName, allSheets, _timeFormat, _dateFormat, _dateTimeFormat, rowCommand);
        }

        using var command = new NpgsqlCommand(rowCommand, connection);
        var paramCount = rowCommand.PgCountParams();
        if (paramCount >= 1) command.Parameters.Add(NpgsqlRestParameter.CreateParamWithType(NpgsqlDbType.Integer));
        if (paramCount >= 2)
        {
            if (dataAsJson is true)
            {
                command.Parameters.Add(NpgsqlRestParameter.CreateParamWithType(NpgsqlDbType.Json));
            }
            else
            {
                command.Parameters.Add(NpgsqlRestParameter.CreateParamWithType(NpgsqlDbType.Text | NpgsqlDbType.Array));
            }
        }
        if (paramCount >= 3) command.Parameters.Add(new NpgsqlParameter());
        if (paramCount >= 4) command.Parameters.Add(NpgsqlRestParameter.CreateParamWithType(NpgsqlDbType.Json));

        StringBuilder result = new(context.Request.Form.Files.Count * 100);
        result.Append('[');
        int fileId = 0;

        for (int i = 0; i < context.Request.Form.Files.Count; i++)
        {
            IFormFile formFile = context.Request.Form.Files[i];

            StringBuilder fileJson = new(100);
            if (_type is not null)
            {
                fileJson.Append("{\"type\":");
                fileJson.Append(PgConverters.SerializeString(_type));
                fileJson.Append(",\"fileName\":");
            }
            else
            {
                fileJson.Append("{\"fileName\":");
            }
            fileJson.Append(PgConverters.SerializeString(formFile.FileName));
            fileJson.Append(",\"contentType\":");
            fileJson.Append(PgConverters.SerializeString(formFile.ContentType));
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

            if (status != UploadFileStatus.Ok)
            {
                fileJson.Append(",\"success\":false,\"status\":");
                fileJson.Append(PgConverters.SerializeString(status.ToString()));
                fileJson.Append('}');
                logger?.FileUploadFailed(_type, formFile.FileName, formFile.ContentType, formFile.Length, status);
                result.Append(fileJson);
                if (i < context.Request.Form.Files.Count - 1) result.Append(',');
                continue;
            }
            if (_stopAfterFirstSuccess is true)
            {
                _skipFileNames.Add(formFile.FileName);
            }

            try
            {
                using var stream = formFile.OpenReadStream();
                using IExcelDataReader reader = ExcelReaderFactory.CreateReader(stream);

                do
                {
                    string sheetName = reader.Name;

                    if (allSheets is false && 
                        string.IsNullOrEmpty(targetSheetName) is false && 
                        string.Equals(sheetName, targetSheetName, StringComparison.OrdinalIgnoreCase) is false)
                    {
                        continue; // Skip this sheet
                    }

                    if (fileId > 0)
                    {
                        result.Append(',');
                    }

                    var rowMeta = string.Concat(fileJson.ToString(),
                        ",\"sheet\":",
                        PgConverters.SerializeDatbaseObject(sheetName));

                    object? commandResult = null;
                    int excelRowIndex = 0, rowIndex = 0;

                    while (reader.Read())
                    {
                        excelRowIndex++;
                        object? values = dataAsJson is true ? GetJsonFromReader(reader, excelRowIndex) : GetValuesFromReader(reader);

                        if (values is null)
                        {
                            continue;
                        }

                        rowIndex++;

                        if (paramCount >= 1)
                        {
                            command.Parameters[0].Value = rowIndex;
                        }
                        if (paramCount >= 2)
                        {
                            command.Parameters[1].Value = values ?? DBNull.Value;
                        }
                        if (paramCount >= 3)
                        {
                            command.Parameters[2].Value = commandResult ?? DBNull.Value;
                        }
                        if (paramCount >= 4)
                        {
                            command.Parameters[3].Value = string.Concat(rowMeta, ",\"rowIndex\":", excelRowIndex, '}');
                        }

                        commandResult = await command.ExecuteScalarWithRetryAsync(cmdRetryStrategy, logger);
                    }

                    var finalJson = string.Concat(rowMeta,
                        ",\"success\":true,\"rows\":",
                        rowIndex - 1,
                        ",\"result\":",
                        PgConverters.SerializeDatbaseObject(commandResult),
                        '}');

                    result.Append(finalJson);
                    fileId++;

                    if (allSheets is false &&
                        string.IsNullOrEmpty(targetSheetName) is true)
                    {
                        break; // Only process the first sheet if not all sheets are requested
                    }

                } while (reader.NextResult());
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error processing Excel file {fileName}", formFile.FileName);
                fileJson.Append(",\"success\":false,\"status\":");
                fileJson.Append(PgConverters.SerializeString(UploadFileStatus.InvalidFormat.ToString()));
                fileJson.Append('}');
                result.Append(fileJson);
                if (fileId < context.Request.Form.Files.Count - 1)
                {
                    result.Append(',');
                }
            }
        }

        result.Append(']');
        return result.ToString();
    }

    public void OnError(NpgsqlConnection? connection, HttpContext context, Exception? exception)
    {
    }

    private string GetJsonFromReader(IExcelDataReader reader, int rowIndex)
    {
        StringBuilder sb = new(100);
        sb.Append('{');
        bool first = true;
        for (int i = 0; i < reader.FieldCount; i++)
        {
            if (reader.IsDBNull(i))
            {
                continue; // Skip null values
            }
            if (first)
            {
                first = false;
            }
            else
            {
                sb.Append(',');
            }
            sb.Append('"');
            sb.Append(GetColumnLetter(i));
            sb.Append(rowIndex);
            sb.Append('"');
            sb.Append(':');
            sb.Append(PgConverters.SerializeDatbaseObject(GetValueFromReader(reader, i)));
        }
        if (first)
        {
            return null!;
        }
        sb.Append('}');
        return sb.ToString();
    }

    private string?[]? GetValuesFromReader(IExcelDataReader reader)
    {
        var values = new string?[reader.FieldCount];
        int nullCount = 0;
        for (int i = 0; i < reader.FieldCount; i++)
        {
            if (reader.IsDBNull(i))
            {
                values[i] = null;
                nullCount++;
            }
            else
            {
                values[i] = GetValueFromReader(reader, i);
            }
        }
        return nullCount == reader.FieldCount ? null : values;
    }

    private string? GetValueFromReader(IExcelDataReader reader, int i)
    {
        if (reader.GetFieldType(i) == typeof(DateTime))
        {
            var date = reader.GetDateTime(i);
            if (date.TimeOfDay == TimeSpan.Zero)
            {
                return date.ToString(_dateFormat);
            }
            if (date.Year == 1899 && date.Month == 12 && date.Day == 31)
            {
                return date.ToString(_timeFormat);
            }
            return date.ToString(_dateTimeFormat);
        }
        return reader.GetValue(i)?.ToString();
    }

    private static string GetColumnLetter(int columnIndex)
    {
        string columnName = "";
        while (columnIndex >= 0)
        {
            columnName = (char)('A' + columnIndex % 26) + columnName;
            columnIndex = columnIndex / 26 - 1;
        }
        return columnName;
    }
}
