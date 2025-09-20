using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using NpgsqlRest;
using NpgsqlRest.Auth;

namespace NpgsqlRestClient;

public class AppStaticFileMiddleware(RequestDelegate next, IWebHostEnvironment hostingEnv)
{
    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));
    private readonly IWebHostEnvironment _hostingEnv = hostingEnv ?? throw new ArgumentNullException(nameof(hostingEnv));
    private static readonly FileExtensionContentTypeProvider _fileTypeProvider = new();

    private static string[]? _parsePatterns = default!;
    private static ILogger? _logger = default!;

    private static readonly ConcurrentDictionary<string, bool> _pathInParsePattern = new();
    private static ConcurrentDictionary<string, bool> _pathInAuthorizePattern = null!;
    // New cache for parsed file contents
    private static readonly ConcurrentDictionary<string, (byte[] Content, DateTimeOffset LastModified)> _parsedFileCache = new();
    private static bool _cacheParsedFiles = true;
    private static IAntiforgery? _antiforgery;
    private static DefaultResponseParser? _parser;

    private static string[]? _headers;
    private static string[]? _authorizePaths;
    private static string? _unauthorizedRedirectPath;
    private static string? _unauthorizedReturnToQueryParameter;
    private static bool _checkAuthorize = false;
    private static string[] _defaultFileNames = default!;
    
    public static void ConfigureStaticFileMiddleware(
        bool parse,
        string[]? parsePatterns,
        NpgsqlRestAuthenticationOptions options,
        bool cacheParsedFiles,
        string? antiforgeryFieldNameTag,
        string? antiforgeryTokenTag,
        IAntiforgery? antiforgery,
        string[]? headers,
        string[]? authorizePaths,
        string? unauthorizedRedirectPath,
        string? unautorizedReturnToQueryParameter,
        string[]? availableClaimTypes,
        ILogger? logger)
    {
        _parsePatterns = parse == false || parsePatterns == null || parsePatterns.Length == 0 ? null : parsePatterns?.Where(p => !string.IsNullOrEmpty(p)).ToArray();
        if (parse is false || _parsePatterns is null)
        {
            _parser = null;
        }
        else
        {
            _parser = new DefaultResponseParser(options, antiforgeryFieldNameTag, antiforgeryTokenTag, availableClaimTypes);
        }

        _antiforgery = antiforgery;
        _logger = logger;
        _cacheParsedFiles = cacheParsedFiles;
        _headers = headers;
        _authorizePaths = authorizePaths;
        _unauthorizedRedirectPath = unauthorizedRedirectPath;
        _unauthorizedReturnToQueryParameter = unautorizedReturnToQueryParameter;
        _checkAuthorize = authorizePaths is not null && authorizePaths.Length > 0;
        if (_checkAuthorize is true)
        {
            _pathInAuthorizePattern = new();
            var dfo = new DefaultFilesOptions();
            _defaultFileNames = [.. dfo.DefaultFileNames];
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string method = context.Request.Method;
        bool isGet = HttpMethods.IsGet(method);
        if (!isGet && !HttpMethods.IsHead(method))
        {
            await _next(context);
            return;
        }
        PathString path = context.Request.Path; // Cache PathString
        IFileInfo fileInfo = _hostingEnv.WebRootFileProvider.GetFileInfo(path);
        if (!fileInfo.Exists || fileInfo.IsDirectory)
        {
            await _next(context);
            return;
        }

        var pathString = path.ToString();
        long length = fileInfo.Length;
        DateTimeOffset lastModified = fileInfo.LastModified.ToUniversalTime();
        bool isInParsePattern = false;

        if (_checkAuthorize is true)
        {
            if (_pathInAuthorizePattern.TryGetValue(pathString, out bool isInAuthPattern) is false)
            {
                isInAuthPattern = false;
                for (int i = 0; i < _authorizePaths?.Length; i++)
                {
                    if (Parser.IsPatternMatch(pathString, _authorizePaths[i]))
                    {
                        isInAuthPattern = true;
                        break;
                    }
                }
                _pathInAuthorizePattern.TryAdd(pathString, isInAuthPattern);
            }
            if (isInAuthPattern is true)
            {
                if (context.User.Identity is not null && context.User.Identity.IsAuthenticated is false)
                {
                    context.Response.Clear();

                    if (string.IsNullOrEmpty(_unauthorizedRedirectPath) is false)
                    {
                        if (string.IsNullOrEmpty(_unauthorizedReturnToQueryParameter) is false)
                        {
                            context.Response.Redirect(new StringBuilder()
                                .Append(_unauthorizedRedirectPath)
                                .Append(_unauthorizedRedirectPath.Contains('?') ? "&" : "?")
                                .Append(_unauthorizedReturnToQueryParameter)
                                .Append('=')
                                .Append(Uri.EscapeDataString(string.Concat(GetOriginalRequestPath(pathString), context.Request.QueryString.ToString())))
                                .ToString());
                        }
                        else
                        {
                            context.Response.Redirect(_unauthorizedRedirectPath);
                        }
                    }
                    else
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    }
                    return;
                }
            }
        }

        if (isGet)
        {
            if (_pathInParsePattern.TryGetValue(pathString, out isInParsePattern) is false)
            {
                isInParsePattern = false;
                for (int i = 0; i < _parsePatterns?.Length; i++)
                {
                    if (Parser.IsPatternMatch(pathString, _parsePatterns[i]))
                    {
                        isInParsePattern = true;
                        break;
                    }
                }
                _pathInParsePattern.TryAdd(pathString, isInParsePattern);
            }
        }

        AntiforgeryTokenSet? tokenSet = null;
        if (_antiforgery is not null)
        {
            if (pathString.EndsWith(".html") is true || pathString.EndsWith(".htm") is true)
            {
                tokenSet = _antiforgery.GetAndStoreTokens(context);
            }
        }
        
        string contentType = fileInfo.PhysicalPath != null && _fileTypeProvider.TryGetContentType(fileInfo.PhysicalPath, out var ct)
            ? ct
            : "application/octet-stream";
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = contentType;

        if (isInParsePattern is false)
        {
            long etagHash = lastModified.ToFileTime() ^ length;
            string etagString = string.Concat("\"", Convert.ToString(etagHash, 16), "\"");

            var ifNoneMatch = context.Request.Headers[HeaderNames.IfNoneMatch];
            if (!string.IsNullOrEmpty(ifNoneMatch) &&
                System.Net.Http.Headers.EntityTagHeaderValue.TryParse(ifNoneMatch, out var clientEtag) &&
                string.Equals(etagString, clientEtag.ToString(), StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            var ifModifiedSince = context.Request.Headers[HeaderNames.IfModifiedSince];
            if (!string.IsNullOrEmpty(ifModifiedSince) && DateTimeOffset.TryParse(ifModifiedSince, out var since) && since >= lastModified)
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }
            context.Response.Headers[HeaderNames.LastModified] = lastModified.ToString("R");
            context.Response.Headers[HeaderNames.ETag] = etagString;
            context.Response.Headers[HeaderNames.AcceptRanges] = "bytes";
        }
        else
        {
            if (_headers is not null)
            {
                for (int i = 0; i < _headers.Length; i++)
                {
                    if (string.IsNullOrEmpty(_headers[i]))
                    {
                        continue;
                    }
                    var parts = _headers[i].Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                    {
                        continue;
                    }
                    context.Response.Headers[parts[0]] = parts[1];
                }
            }
        }
        if (isGet)
        {
            try
            {
                byte[] buffer;
                if (_parser is null)
                {
                    context.Response.ContentLength = length;
                    using var fileStream = new FileStream(fileInfo.PhysicalPath!, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 8192, useAsync: true);
                    await fileStream.CopyToAsync(context.Response.Body, context.RequestAborted);
                    return;
                }

                if (isInParsePattern is false)
                {
                    context.Response.ContentLength = length;
                    using var fileStream = new FileStream(fileInfo.PhysicalPath!, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 8192, useAsync: true);
                    await fileStream.CopyToAsync(context.Response.Body, context.RequestAborted);
                    return;
                }

                // Check cache for parsed files
                if (isInParsePattern && _cacheParsedFiles && _parsedFileCache.TryGetValue(pathString, out var cached) && cached.LastModified >= lastModified)
                {
                    buffer = cached.Content;
                }
                else
                {
                    // Read file from disk
                    using var fileStream = new FileStream(fileInfo.PhysicalPath!, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 8192, useAsync: true);
                    buffer = new byte[(int)fileInfo.Length];
                    await fileStream.ReadExactlyAsync(buffer, context.RequestAborted);

                    // Cache the file content if enabled
                    if (_cacheParsedFiles)
                    {
                        _parsedFileCache[pathString] = (buffer, lastModified);
                    }
                }

                int charCount = Encoding.UTF8.GetCharCount(buffer);
                char[] chars = ArrayPool<char>.Shared.Rent(charCount);
                try
                {
                    Encoding.UTF8.GetChars(buffer, 0, buffer.Length, chars, 0);
                    ReadOnlySpan<char> result = _parser.Parse(new ReadOnlySpan<char>(chars, 0, charCount), context, tokenSet);
                    var writer = PipeWriter.Create(context.Response.Body);
                    try
                    {
                        int maxBytesNeeded = Encoding.UTF8.GetMaxByteCount(result.Length);
                        Memory<byte> memory = writer.GetMemory(maxBytesNeeded);
                        int actualBytesWritten = Encoding.UTF8.GetBytes(result, memory.Span);
                        writer.Advance(actualBytesWritten);
                        context.Response.ContentLength = actualBytesWritten;
                        await writer.FlushAsync(context.RequestAborted);
                    }
                    finally
                    {
                        await writer.CompleteAsync();
                    }
                }
                finally
                {
                    ArrayPool<char>.Shared.Return(chars);
                }
            }
            catch (IOException ex)
            {
                _logger?.LogError(ex, "Failed to serve static file {Path}", path);

                context.Response.Clear();
                await _next(context);
                return;
            }
        }

        // HEAD request completes here
    }

    private static string GetOriginalRequestPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length < 2)
        {
            return path;
        }
        ReadOnlySpan<char> pathSpan = path.AsSpan();
        for (int i = 0; i < _defaultFileNames.Length; i++)
        {
            string defaultFile = _defaultFileNames[i];
            if (pathSpan.EndsWith(defaultFile.AsSpan(), StringComparison.OrdinalIgnoreCase) &&
                pathSpan.Length > defaultFile.Length && pathSpan[^(defaultFile.Length + 1)] == '/')
            {
                return path[..^defaultFile.Length];
            }
        }
        return path;
    }
}