using NpgsqlRest;

namespace NpgsqlRestClient;

/// <summary>The custom parameters the Excel table format reads, as annotation reference data.</summary>
public static class TableFormatAnnotations
{
    public static CommentAnnotation[] All =>
    [
        new()
        {
            Name = "excel_file_name",
            Aliases = ["excel_file_name"],
            Syntax = "excel_file_name = <value>",
            Description = "The file name of the Excel download (.xlsx is added when missing). Default: the routine's name.",
            Owner = "table_format:excel",
            Form = AnnotationForm.KeyValue,
            ValueKind = AnnotationValueKind.Text,
            Example = "excel_file_name = orders-{_year}"
        },
        new()
        {
            Name = "excel_sheet",
            Aliases = ["excel_sheet"],
            Syntax = "excel_sheet = <value>",
            Description = "The name of the worksheet (31 characters at most). Default: the ExcelSheetName setting, else the routine's name.",
            Owner = "table_format:excel",
            Form = AnnotationForm.KeyValue,
            ValueKind = AnnotationValueKind.Text,
            Example = "excel_sheet = Orders"
        },
    ];
}
