namespace NpgsqlRest.UploadHandlers;

[Flags]
public enum AllowedImageTypes { Jpeg = 1, Png = 2, Gif = 4, Bmp = 8, Tiff = 16, Webp = 32 }

public static class FileCheckExtensions
{
    public const string CheckTextParam = "check_text";
    public const string CheckImageParam = "check_image";

    public const string TestBufferSizeParam = "test_buffer_size";
    public const string NonPrintableThresholdParam = "non_printable_threshold";

    public static async Task<UploadFileStatus> CheckTextContentStatus(
        this IFormFile formFile,
        int testBufferSize = 4096,
        int nonPrintableThreshold = 5,
        bool checkNewLines = true)
    {
        int length = formFile.Length < testBufferSize ? (int)formFile.Length : testBufferSize;

        if (length == 0)
        {
            return UploadFileStatus.Empty;
        }

        using var fileStream = formFile.OpenReadStream();
        byte[] buffer = new byte[length];
        int bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, length));

        if (bytesRead == 0)
        {
            return UploadFileStatus.Empty;
        }

        int nonPrintableCount = 0;
        int newLineCount = 0;

        for (int i = 0; i < bytesRead; i++)
        {
            // Check for null byte - immediate binary indicator
            if (buffer[i] == 0)
            {
                return UploadFileStatus.ProbablyBinary;
            }

            // Count newlines (LF character) if we're checking for them
            if (checkNewLines && buffer[i] == 10) // ASCII LF (Line Feed)
            {
                newLineCount++;
            }

            // Count non-printable characters
            else if (buffer[i] < 32 && buffer[i] != 9 && buffer[i] != 13)
            {
                nonPrintableCount++;
            }
        }

        // Check if binary based on non-printable characters
        if (nonPrintableCount > nonPrintableThreshold)
        {
            return UploadFileStatus.ProbablyBinary;
        }

        // Only check for newlines if the parameter is true
        if (checkNewLines)
        {
            if (newLineCount > 0)
            {
                return UploadFileStatus.Ok;
            }
            return UploadFileStatus.NoNewLines;
        }
        else
        {
            // If we're not checking for newlines, consider it OK if it passes binary checks
            return UploadFileStatus.Ok;
        }
    }

    public static async Task<bool> IsImage(this IFormFile file, AllowedImageTypes allowedTypes)
    {
        if (file == null || file.Length == 0)
            return false;

        using var fileStream = file.OpenReadStream();
        var buffer = new byte[12];
        int bytesRead = await fileStream.ReadAsync(buffer);

        return (allowedTypes.HasFlag(AllowedImageTypes.Jpeg) && IsJpeg(buffer, bytesRead)) ||
                (allowedTypes.HasFlag(AllowedImageTypes.Png) && IsPng(buffer, bytesRead)) ||
                (allowedTypes.HasFlag(AllowedImageTypes.Gif) && IsGif(buffer, bytesRead)) ||
                (allowedTypes.HasFlag(AllowedImageTypes.Bmp) && IsBmp(buffer, bytesRead)) ||
                (allowedTypes.HasFlag(AllowedImageTypes.Tiff) && IsTiff(buffer, bytesRead)) ||
                (allowedTypes.HasFlag(AllowedImageTypes.Webp) && IsWebp(buffer, bytesRead));
    }

    public static AllowedImageTypes? ParseImageTypes(this string csvInput, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(csvInput))
        {
            return null;
        }
        AllowedImageTypes result = 0;
        foreach (string type in csvInput.ToLowerInvariant().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (type)
            {
                case "jpeg":
                case "jpg":
                    result |= AllowedImageTypes.Jpeg;
                    break;
                case "png":
                    result |= AllowedImageTypes.Png;
                    break;
                case "gif":
                    result |= AllowedImageTypes.Gif;
                    break;
                case "bmp":
                    result |= AllowedImageTypes.Bmp;
                    break;
                case "tiff":
                    result |= AllowedImageTypes.Tiff;
                    break;
                case "webp":
                    result |= AllowedImageTypes.Webp;
                    break;
                default:
                    (Logger ?? logger)?.LogWarning("Unknown image type: {Type}", type);
                    break;
            }
        }
        return result;
    }

    private static bool IsJpeg(byte[] buffer, int bytesRead)
    {
        if (bytesRead < 3)
        {
            return false;
        }
        return buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF;
    }

    private static bool IsPng(byte[] buffer, int bytesRead)
    {
        if (bytesRead < 4)
        {
            return false;
        }
        return buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47;
    }

    private static bool IsGif(byte[] buffer, int bytesRead)
    {
        if (bytesRead < 6)
        {
            return false;
        }
        return buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x38 &&
            (buffer[4] == 0x37 || buffer[4] == 0x39) && buffer[5] == 0x61; // GIF87a or GIF89a
    }

    private static bool IsBmp(byte[] buffer, int bytesRead)
    {
        if (bytesRead < 2)
        {
            return false;
        }
        return buffer[0] == 0x42 && buffer[1] == 0x4D;
    }

    private static bool IsTiff(byte[] buffer, int bytesRead)
    {
        if (bytesRead < 4)
        {
            return false;
        }
        return (buffer[0] == 0x49 && buffer[1] == 0x49 && buffer[2] == 0x2A && buffer[3] == 0x00) || // Little-endian
            (buffer[0] == 0x4D && buffer[1] == 0x4D && buffer[2] == 0x00 && buffer[3] == 0x2A);   // Big-endian
    }

    private static bool IsWebp(byte[] buffer, int bytesRead)
    {
        if (bytesRead < 12)
        {
            return false;
        }
        return buffer[0] == 0x52 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x46 && // "RIFF"
               buffer[8] == 0x57 && buffer[9] == 0x45 && buffer[10] == 0x42 && buffer[11] == 0x50;  // "WEBP"
    }
}
