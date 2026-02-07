using System.Globalization;
using System.IO.Pipelines;
using Npgsql;
using NpgsqlRest;
using NpgsqlRest.TableFormatHandlers;
using SpreadCheetah;
using SpreadCheetah.Styling;

namespace NpgsqlRestClient;

public class ExcelTableFormatHandler : ITableFormatHandler
{
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// Worksheet name. When null, uses the routine name.
    /// </summary>
    public string? SheetName { get; set; } = null;

    /// <summary>
    /// Excel Format Code for DateTime cells. When null, uses SpreadCheetah default (yyyy-MM-dd HH:mm:ss).
    /// Uses Excel Format Codes (not .NET format strings). Examples: "yyyy-mm-dd", "dd/mm/yyyy hh:mm".
    /// </summary>
    public string? DateTimeFormat { get; set; } = null;

    /// <summary>
    /// Excel Format Code for numeric cells. When null, uses Excel default (General).
    /// Uses Excel Format Codes (not .NET format strings). Examples: "#,##0.00", "0.00", "#,##0".
    /// </summary>
    public string? NumericFormat { get; set; } = null;

    public async Task RenderAsync(
        NpgsqlDataReader reader,
        Routine routine,
        RoutineEndpoint endpoint,
        PipeWriter writer,
        HttpContext context,
        ulong bufferRows,
        Dictionary<string, string>? customParameters,
        CancellationToken cancellationToken)
    {
        var columnCount = routine.ColumnCount;

        var fileName = routine.Name.Trim('"').Replace('/', '_');
        if (customParameters is not null && customParameters.TryGetValue("excel_file_name", out var customFileName))
        {
            fileName = customFileName;
            if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".xlsx";
            }
        }
        else
        {
            fileName += ".xlsx";
        }
        context.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{fileName}\"";

        var stream = writer.AsStream(leaveOpen: true);
        SpreadCheetahOptions? options = DateTimeFormat is not null
            ? new SpreadCheetahOptions { DefaultDateTimeFormat = NumberFormat.Custom(DateTimeFormat) }
            : null;
        await using var spreadsheet = await Spreadsheet.CreateNewAsync(stream, options, cancellationToken);

        var headerStyle = spreadsheet.AddStyle(new Style { Font = { Bold = true } });

        var sheetName = SheetName ?? routine.Name.Trim('"');
        if (customParameters is not null && customParameters.TryGetValue("excel_sheet", out var customSheetName))
        {
            sheetName = customSheetName;
        }
        if (sheetName.Length > 31)
        {
            sheetName = sheetName[..31];
        }
        await spreadsheet.StartWorksheetAsync(sheetName, token: cancellationToken);

        await spreadsheet.AddHeaderRowAsync(routine.ColumnNames, headerStyle, cancellationToken);

        var descriptors = routine.ColumnsTypeDescriptor;

        if (NumericFormat is not null)
        {
            var numericStyle = spreadsheet.AddStyle(new Style { Format = NumberFormat.Custom(NumericFormat) });
            await WriteStyledRowsAsync(spreadsheet, reader, columnCount, descriptors, numericStyle, cancellationToken);
        }
        else
        {
            await WriteDataRowsAsync(spreadsheet, reader, columnCount, descriptors, cancellationToken);
        }

        await spreadsheet.FinishAsync(cancellationToken);
    }

    private static async Task WriteDataRowsAsync(
        Spreadsheet spreadsheet,
        NpgsqlDataReader reader,
        int columnCount,
        TypeDescriptor[] descriptors,
        CancellationToken cancellationToken)
    {
        var cells = new DataCell[columnCount];
        while (await reader.ReadAsync(cancellationToken))
        {
            for (int i = 0; i < columnCount; i++)
            {
                if (reader.IsDBNull(i))
                {
                    cells[i] = default;
                    continue;
                }
                var value = reader.GetValue(i);
                cells[i] = value switch
                {
                    int v => new DataCell(v),
                    long v => new DataCell(v),
                    double v => new DataCell(v),
                    float v => new DataCell(v),
                    decimal v => new DataCell(v),
                    bool v => new DataCell(v),
                    DateTime v => new DataCell(v),
                    string s => ParseStringCell(s, descriptors[i]),
                    _ => new DataCell(value.ToString() ?? "")
                };
            }
            await spreadsheet.AddRowAsync(cells, cancellationToken);
        }
    }

    private static async Task WriteStyledRowsAsync(
        Spreadsheet spreadsheet,
        NpgsqlDataReader reader,
        int columnCount,
        TypeDescriptor[] descriptors,
        StyleId numericStyle,
        CancellationToken cancellationToken)
    {
        var cells = new Cell[columnCount];
        while (await reader.ReadAsync(cancellationToken))
        {
            for (int i = 0; i < columnCount; i++)
            {
                if (reader.IsDBNull(i))
                {
                    cells[i] = default;
                    continue;
                }
                var value = reader.GetValue(i);
                var dataCell = value switch
                {
                    int v => new DataCell(v),
                    long v => new DataCell(v),
                    double v => new DataCell(v),
                    float v => new DataCell(v),
                    decimal v => new DataCell(v),
                    bool v => new DataCell(v),
                    DateTime v => new DataCell(v),
                    string s => ParseStringCell(s, descriptors[i]),
                    _ => new DataCell(value.ToString() ?? "")
                };
                cells[i] = IsNumericValue(value, descriptors[i])
                    ? new Cell(dataCell, numericStyle)
                    : new Cell(dataCell);
            }
            await spreadsheet.AddRowAsync(cells, cancellationToken);
        }
    }

    private static bool IsNumericValue(object value, TypeDescriptor descriptor) =>
        value is int or long or double or float or decimal
        || (value is string && descriptor.IsNumeric);

    private static DataCell ParseStringCell(string value, TypeDescriptor descriptor)
    {
        if (descriptor.IsNumeric && double.TryParse(value, CultureInfo.InvariantCulture, out var d))
        {
            return new DataCell(d);
        }
        if (descriptor.IsBoolean)
        {
            if (bool.TryParse(value, out var b))
            {
                return new DataCell(b);
            }
            // PostgreSQL text representation: "t"/"f"
            if (string.Equals(value, "t", StringComparison.OrdinalIgnoreCase))
            {
                return new DataCell(true);
            }
            if (string.Equals(value, "f", StringComparison.OrdinalIgnoreCase))
            {
                return new DataCell(false);
            }
        }
        if ((descriptor.IsDateTime || descriptor.IsDate) && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return new DataCell(dt);
        }
        return new DataCell(value);
    }
}
