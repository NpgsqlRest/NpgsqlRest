using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;

namespace NpgsqlRestClient;

/// <summary>
/// Response compression as the client configures it (the ResponseCompression configuration section).
/// </summary>
public static class ResponseCompressionSetup
{
    /// <summary>True when this build can compress with Zstandard (.NET 11 and later).</summary>
    public static bool ZstandardSupported =>
#if NET11_0_OR_GREATER
        true;
#else
        false;
#endif

    public static readonly string[] DefaultMimeTypes = [
        "text/plain",
        "text/css",
        "application/javascript",
        "text/javascript",
        "text/html",
        "application/xml",
        "text/xml",
        "application/json",
        "text/json",
        "image/svg+xml",
        "font/woff",
        "font/woff2",
        "application/font-woff",
        "application/font-woff2"];

    /// <summary>
    /// The Zstandard quality that stands for a compression level: the algorithm's default for Optimal, its
    /// fastest regular level for Fastest, and a high level short of the "ultra" ones (20 and up, which take far
    /// more memory on both sides) for SmallestSize.
    /// </summary>
    public static int ZstandardQualityFor(CompressionLevel level) => level switch
    {
        CompressionLevel.Fastest => 1,
        CompressionLevel.SmallestSize => 19,
        CompressionLevel.NoCompression => 1,
        _ => 3
    };

    /// <summary>
    /// Registers response compression. Providers are tried in the order Zstandard, Brotli, Gzip among the
    /// encodings the client accepts with equal preference. Returns false, registering nothing, when no
    /// algorithm is on.
    /// </summary>
    public static bool Add(
        IServiceCollection services,
        bool enableForHttps,
        bool useZstandard,
        bool useBrotli,
        bool useGzip,
        CompressionLevel level,
        int? zstandardQuality = null,
        IEnumerable<string>? mimeTypes = null,
        IEnumerable<string>? excludedMimeTypes = null)
    {
        useZstandard = useZstandard && ZstandardSupported;
        if (useZstandard is false && useBrotli is false && useGzip is false)
        {
            return false;
        }

        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = enableForHttps;
#if NET11_0_OR_GREATER
            if (useZstandard is true)
            {
                options.Providers.Add<ZstandardCompressionProvider>();
            }
#endif
            if (useBrotli is true)
            {
                options.Providers.Add<BrotliCompressionProvider>();
            }
            if (useGzip is true)
            {
                options.Providers.Add<GzipCompressionProvider>();
            }
            options.MimeTypes = mimeTypes?.ToArray() ?? DefaultMimeTypes;
            options.ExcludedMimeTypes = excludedMimeTypes?.ToArray() ?? [];
        });

#if NET11_0_OR_GREATER
        if (useZstandard is true)
        {
            var quality = zstandardQuality ?? ZstandardQualityFor(level);
            services.Configure<ZstandardCompressionProviderOptions>(options =>
            {
                options.CompressionOptions.Quality = quality;
            });
        }
#endif
        if (useBrotli is true)
        {
            services.Configure<BrotliCompressionProviderOptions>(options =>
            {
                options.Level = level;
            });
        }
        if (useGzip is true)
        {
            services.Configure<GzipCompressionProviderOptions>(options =>
            {
                options.Level = level;
            });
        }
        return true;
    }
}
