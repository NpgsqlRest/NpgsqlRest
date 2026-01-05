namespace NpgsqlRest.UploadHandlers;

public class UploadHandlerOptions
{
    // General settings for upload handlers.

    public bool StopAfterFirstSuccess { get; set; } = false;
    public string[]? IncludedMimeTypePatterns { get; set; } = null;
    public string[]? ExcludedMimeTypePatterns { get; set; } = null;
    public int BufferSize { get; set; } = 8192;
    public int TextTestBufferSize { get; set; } = 4096;
    public int TextNonPrintableThreshold { get; set; } = 5;
    public AllowedImageTypes AllowedImageTypes { get; set; } = 
        AllowedImageTypes.Jpeg | AllowedImageTypes.Png | AllowedImageTypes.Gif | AllowedImageTypes.Bmp | AllowedImageTypes.Tiff | AllowedImageTypes.Webp;


    // large_object settings for upload handlers.

    public bool LargeObjectEnabled { get; set; } = true;
    public string LargeObjectKey { get; set; } = "large_object";
    public bool LargeObjectCheckText { get; set; } = false;
    public bool LargeObjectCheckImage { get; set; } = false;


    // file_system settings for upload handlers.

    public bool FileSystemEnabled { get; set; } = true;
    public string FileSystemKey { get; set; } = "file_system";
    public string FileSystemPath { get; set; } = "./";
    public bool FileSystemUseUniqueFileName { get; set; } = true;
    public bool FileSystemCreatePathIfNotExists { get; set; } = false;
    public bool FileSystemCheckText { get; set; } = false;
    public bool FileSystemCheckImage { get; set; } = false;

    // csv settings for upload handlers.

    public bool CsvUploadEnabled { get; set; } = true;
    public string CsvUploadKey { get; set; } = "csv";
    public bool CsvUploadCheckFileStatus { get; set; } = true;
    public string CsvUploadDelimiterChars { get; set; } = ",";
    public bool CsvUploadHasFieldsEnclosedInQuotes { get; set; } = true;
    public bool CsvUploadSetWhiteSpaceToNull { get; set; } = true;
    public string CsvUploadRowCommand { get; set; } = "call process_csv_row($1,$2,$3,$4)";

    // Row command user claims settings.
    // When set, authenticated user claims are included in the row metadata JSON parameter
    // under this key name. Set to null to disable. Example: "claims" adds {"claims": {...}} to metadata.

    public string? RowCommandUserClaimsKey { get; set; } = "claims";
}
