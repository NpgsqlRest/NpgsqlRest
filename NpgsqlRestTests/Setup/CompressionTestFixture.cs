using System.IO.Compression;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using NpgsqlRest.Auth;
using NpgsqlRestClient;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("CompressionTestFixture")]
public class CompressionTestFixtureCollection : ICollectionFixture<CompressionTestFixture> { }

/// <summary>
/// Test fixture for ResponseCompression and StaticFiles tests.
/// Creates a web application with compression enabled to verify:
/// - Static files are compressed correctly
/// - MIME types are handled properly
/// - Middleware ordering works correctly
/// </summary>
public class CompressionTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly string _webRootPath;

    public HttpClient Client => _client;
    public string WebRootPath => _webRootPath;

    /// <summary>
    /// Default MIME types that should be compressed (matches Builder.cs defaults)
    /// </summary>
    public static readonly string[] DefaultCompressibleMimeTypes =
    [
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
        "application/font-woff2"
    ];

    public CompressionTestFixture()
    {
        // Ensure the database is created and get connection string
        var connectionString = Database.Create();

        // Create temp directory for static files
        _webRootPath = Path.Combine(Path.GetTempPath(), "NpgsqlRestTests", "CompressionTestStaticFiles", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_webRootPath);

        // Create test static files
        CreateTestStaticFiles();

        // Use WebApplicationOptions to set WebRootPath (can't change after builder is created)
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            WebRootPath = _webRootPath
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0"); // Use random available port

        // Add response compression with both Brotli and Gzip
        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = DefaultCompressibleMimeTypes;
        });

        builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Fastest;
        });

        builder.Services.Configure<GzipCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Fastest;
        });

        _app = builder.Build();

        // CRITICAL: ResponseCompression must be before static files middleware
        _app.UseResponseCompression();

        // Configure static file middleware
        _app.UseDefaultFiles();
        AppStaticFileMiddleware.ConfigureStaticFileMiddleware(
            parse: false,
            parsePatterns: null,
            options: new NpgsqlRestAuthenticationOptions(),
            cacheParsedFiles: false,
            antiforgeryFieldNameTag: null,
            antiforgeryTokenTag: null,
            antiforgery: null,
            headers: null,
            authorizePaths: null,
            unauthorizedRedirectPath: null,
            unautorizedReturnToQueryParameter: null,
            availableClaimTypes: null,
            logger: null);
        _app.UseMiddleware<AppStaticFileMiddleware>();

        // Add NpgsqlRest for API compression testing
        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            IncludeSchemas = ["public"],
            CommentsMode = CommentsMode.ParseAll,
        });

        _app.StartAsync().GetAwaiter().GetResult();

        var serverAddress = _app.Urls.First();

        // IMPORTANT: Create HttpClient that does NOT auto-decompress responses
        // This allows us to verify the Content-Encoding header is set correctly
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.None
        };
        _client = new HttpClient(handler) { BaseAddress = new Uri(serverAddress) };
        _client.Timeout = TimeSpan.FromMinutes(5);
    }

    private void CreateTestStaticFiles()
    {
        // Create test.html - enough content to be worth compressing
        var htmlContent = """
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>Compression Test</title>
            </head>
            <body>
                <h1>Response Compression Test Page</h1>
                <p>This is a test HTML file used to verify that response compression is working correctly.</p>
                <p>The file needs to be large enough that compression provides a benefit.</p>
                <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.</p>
                <p>Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.</p>
                <p>Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur.</p>
            </body>
            </html>
            """;
        File.WriteAllText(Path.Combine(_webRootPath, "test.html"), htmlContent);

        // Create test.js - tests text/javascript MIME type
        var jsContent = """
            // Compression Test JavaScript File
            // This file is used to verify that text/javascript MIME type is compressed correctly

            function compressionTest() {
                console.log('Testing response compression for JavaScript files');
                const data = {
                    message: 'This is test data to make the file larger',
                    values: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
                    nested: {
                        level1: {
                            level2: {
                                level3: 'Deep nested content for compression testing'
                            }
                        }
                    }
                };
                return JSON.stringify(data, null, 2);
            }

            function anotherFunction() {
                const items = ['apple', 'banana', 'cherry', 'date', 'elderberry', 'fig', 'grape'];
                return items.map(item => item.toUpperCase()).join(', ');
            }

            export { compressionTest, anotherFunction };
            """;
        File.WriteAllText(Path.Combine(_webRootPath, "test.js"), jsContent);

        // Create test.json - tests application/json MIME type for static files
        var jsonContent = """
            {
                "compressionTest": true,
                "description": "This JSON file is used to verify that static JSON files are compressed correctly",
                "testData": {
                    "items": [
                        {"id": 1, "name": "Item One", "description": "First test item for compression"},
                        {"id": 2, "name": "Item Two", "description": "Second test item for compression"},
                        {"id": 3, "name": "Item Three", "description": "Third test item for compression"},
                        {"id": 4, "name": "Item Four", "description": "Fourth test item for compression"},
                        {"id": 5, "name": "Item Five", "description": "Fifth test item for compression"}
                    ],
                    "metadata": {
                        "created": "2024-01-01T00:00:00Z",
                        "version": "1.0.0",
                        "purpose": "Testing response compression middleware"
                    }
                }
            }
            """;
        File.WriteAllText(Path.Combine(_webRootPath, "test.json"), jsonContent);

        // Create test.css - tests text/css MIME type
        var cssContent = """
            /* Compression Test CSS File */
            /* This file is used to verify that CSS files are compressed correctly */

            body {
                font-family: Arial, sans-serif;
                margin: 0;
                padding: 20px;
                background-color: #f5f5f5;
            }

            .container {
                max-width: 1200px;
                margin: 0 auto;
                padding: 20px;
                background-color: white;
                border-radius: 8px;
                box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
            }

            h1, h2, h3 {
                color: #333;
                margin-bottom: 1rem;
            }

            p {
                line-height: 1.6;
                color: #666;
            }

            .button {
                display: inline-block;
                padding: 10px 20px;
                background-color: #007bff;
                color: white;
                border: none;
                border-radius: 4px;
                cursor: pointer;
            }

            .button:hover {
                background-color: #0056b3;
            }
            """;
        File.WriteAllText(Path.Combine(_webRootPath, "test.css"), cssContent);
    }

#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
    public void Dispose()
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
    {
        _client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();

        // Clean up temp directory
        try
        {
            if (Directory.Exists(_webRootPath))
            {
                Directory.Delete(_webRootPath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
