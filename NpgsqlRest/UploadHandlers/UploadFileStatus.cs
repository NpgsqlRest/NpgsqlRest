namespace NpgsqlRest.UploadHandlers;

public enum UploadFileStatus
{ 
    Empty,
    ProbablyBinary,
    InvalidImage,
    InvalidFormat,
    NoNewLines,
    InvalidMimeType,
    Ignored,
    Ok,
    /// <summary>
    /// The target file name or path is not allowed (directory traversal or a path outside the upload directory).
    /// </summary>
    InvalidFileName
}
