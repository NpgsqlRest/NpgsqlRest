namespace NpgsqlRest.UploadHandlers;

/// <summary>
/// The custom parameters (<c>key = value</c>) the upload handlers read, as annotation reference data.
/// Every key may also be written with its handler's name in front (<c>csv_delimiters</c>); the plain key wins
/// when both are present.
/// </summary>
public static class UploadAnnotations
{
    private const AnnotationValueKind Bool = AnnotationValueKind.Boolean;
    private const AnnotationValueKind Int = AnnotationValueKind.Integer;
    private const AnnotationValueKind Text = AnnotationValueKind.Text;

    private static CommentAnnotation P(string handler, string key, AnnotationValueKind kind, string? def, string description, string example, string[]? handlers = null) =>
        new()
        {
            Name = key,
            Aliases = [key, .. (handlers ?? [handler]).Select(h => string.Concat(h, "_", key))],
            Syntax = string.Concat(key, " = <", kind switch
            {
                Bool => "true|false",
                Int => "number",
                AnnotationValueKind.Sql => "sql",
                AnnotationValueKind.List => "list",
                AnnotationValueKind.Path => "path",
                AnnotationValueKind.Name => "name",
                _ => "value"
            }, ">"),
            Description = description,
            Owner = handlers is null ? string.Concat("upload:", handler) : "upload",
            Form = AnnotationForm.KeyValue,
            ValueKind = kind,
            Default = def,
            Example = example
        };

    private static readonly string[] All = ["large_object", "file_system", "csv", "excel"];

    /// <summary>The parameters of the handlers the library has: shared, large_object, file_system and csv.</summary>
    public static CommentAnnotation[] Core =>
    [
        P("", "stop_after_first_success", Bool, "false", "With several upload handlers: a file that one handler processed successfully is skipped by the handlers after it.", "stop_after_first_success = true", All),
        P("", "included_mime_types", AnnotationValueKind.List, null, "Accept only files whose MIME type matches one of these patterns (* and ? are wildcards). Others are rejected.", "included_mime_types = image/*, application/pdf", All),
        P("", "excluded_mime_types", AnnotationValueKind.List, null, "Reject files whose MIME type matches any of these patterns (* and ? are wildcards).", "excluded_mime_types = application/x-msdownload", All),
        P("", "buffer_size", Int, "8192", "Size in bytes of the chunks a file is read and written in (large_object and file_system).", "buffer_size = 65536", All),
        P("", "fallback_handler", AnnotationValueKind.Name, null, "The upload handler that takes over when csv or excel cannot process the file.", "fallback_handler = file_system", ["csv", "excel"]),

        P("large_object", "oid", Int, null, "The OID of the large object to create. Without it PostgreSQL assigns one.", "oid = {_oid}"),
        P("large_object", "check_text", Bool, "false", "Reject files that are not text (binary content).", "check_text = true"),
        P("large_object", "check_image", Text, "false", "Reject files that are not images, by their first bytes: true for any supported type, or a list of jpg, jpeg, png, gif, bmp, tiff, webp.", "check_image = jpg, png"),
        P("large_object", "test_buffer_size", Int, "4096", "How many bytes of the file the text check reads.", "test_buffer_size = 8192"),
        P("large_object", "non_printable_threshold", Int, "5", "How many non-printable bytes the text check tolerates before the file counts as binary.", "non_printable_threshold = 0"),

        P("file_system", "path", AnnotationValueKind.Path, null, "The directory the file is saved in. Default: the configured FileSystemPath.", "path = /var/uploads/{_folder}"),
        P("file_system", "file", Text, null, "The name the file is saved under (relative, no '..'). Default: the uploaded file's own name. With unique_name only its extension is used.", "file = {_name}.pdf"),
        P("file_system", "unique_name", Bool, "true", "Save the file under a generated unique name that keeps the extension.", "unique_name = false"),
        P("file_system", "create_path", Bool, null, "Create the directory when it does not exist. Default: the configured FileSystemCreatePathIfNotExists.", "create_path = true"),
        P("file_system", "check_text", Bool, "false", "Reject files that are not text (binary content).", "check_text = true"),
        P("file_system", "check_image", Text, "false", "Reject files that are not images, by their first bytes: true for any supported type, or a list of jpg, jpeg, png, gif, bmp, tiff, webp.", "check_image = true"),
        P("file_system", "test_buffer_size", Int, "4096", "How many bytes of the file the text check reads.", "test_buffer_size = 8192"),
        P("file_system", "non_printable_threshold", Int, "5", "How many non-printable bytes the text check tolerates before the file counts as binary.", "non_printable_threshold = 0"),

        P("csv", "check_format", Bool, "true", "Check that the file is text with at least one line break before it is processed.", "check_format = false"),
        P("csv", "test_buffer_size", Int, "4096", "How many bytes of the file the format check reads.", "test_buffer_size = 8192"),
        P("csv", "non_printable_threshold", Int, "5", "How many non-printable bytes the format check tolerates before the file counts as binary.", "non_printable_threshold = 0"),
        P("csv", "delimiters", Text, ",", "The field delimiter characters; every character of the value is one. \\t is a tab.", "delimiters = ;\\t"),
        P("csv", "has_fields_enclosed_in_quotes", Bool, "true", "Fields may be enclosed in double quotes.", "has_fields_enclosed_in_quotes = false"),
        P("csv", "set_white_space_to_null", Bool, "true", "Empty and whitespace-only fields are passed as NULL.", "set_white_space_to_null = false"),
        P("csv", "row_command", AnnotationValueKind.Sql, "call process_csv_row($1,$2,$3,$4)", "The SQL command run for every row: $1 is the row number (from 1), $2 the fields as text[], $3 the previous row's result, $4 the upload metadata. {placeholders} are never substituted in it.", "row_command = select import_row($1, $2, $3, $4)"),
    ];

    /// <summary>
    /// The same parameters under another handler name. A handler's key is configurable (LargeObjectKey,
    /// FileSystemKey, CsvUploadKey, the client's ExcelKey), and its parameters may be prefixed with that key.
    /// </summary>
    public static CommentAnnotation[] WithHandlerKey(CommentAnnotation[] annotations, string defaultKey, string configuredKey)
    {
        if (string.Equals(defaultKey, configuredKey, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }
        var owner = string.Concat("upload:", defaultKey);
        return [.. annotations
            .Where(a => a.Owner == owner)
            .Select(a => new CommentAnnotation
            {
                Name = a.Name,
                Aliases = [a.Name, string.Concat(configuredKey, "_", a.Name)],
                Syntax = a.Syntax,
                Description = a.Description,
                Owner = string.Concat("upload:", configuredKey),
                Form = a.Form,
                Contexts = a.Contexts,
                ValueKind = a.ValueKind,
                Values = a.Values,
                Default = a.Default,
                Example = a.Example
            })];
    }

    /// <summary>The parameters of the excel handler, which the npgsqlrest client adds.</summary>
    public static CommentAnnotation[] Excel =>
    [
        P("excel", "sheet_name", Text, null, "The sheet to process (case-insensitive). Default: the first sheet. Ignored with all_sheets.", "sheet_name = Orders"),
        P("excel", "all_sheets", Bool, "false", "Process every sheet of the workbook.", "all_sheets = true"),
        P("excel", "time_format", Text, "HH:mm:ss", "The .NET format that cells holding only a time are passed in.", "time_format = HH:mm"),
        P("excel", "date_format", Text, "yyyy-MM-dd", "The .NET format that cells holding a date without a time are passed in.", "date_format = dd.MM.yyyy"),
        P("excel", "datetime_format", Text, "yyyy-MM-dd HH:mm:ss", "The .NET format that cells holding a date and a time are passed in.", "datetime_format = yyyy-MM-ddTHH:mm:ss"),
        P("excel", "row_is_json", Bool, "false", "Pass the row ($2) as a JSON object keyed by cell reference ({\"A1\": ...}) instead of text[].", "row_is_json = true"),
        P("excel", "row_command", AnnotationValueKind.Sql, "call process_excel_row($1,$2,$3,$4)", "The SQL command run for every non-empty row: $1 is the row number, $2 the cells, $3 the previous row's result, $4 the upload metadata. {placeholders} are never substituted in it.", "row_command = select import_row($1, $2, $3, $4)"),
    ];
}
