using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using NpgsqlRest.SqlFileSource;
using NpgsqlRest.TsClient;
using NpgsqlRestTests.SqlFileSourceTests;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("SqlFileSourceFixture")]
public class SqlFileSourceFixtureCollection : ICollectionFixture<SqlFileSourceTestFixture> { }

/// <summary>
/// Test fixture for SqlFileSource endpoint tests.
/// Creates SQL files in a temp directory and starts a web application
/// with SqlFileSource configured to scan them.
/// </summary>
public class SqlFileSourceTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly string _sqlDir;
    private readonly string _tsClientDir;

    public HttpClient Client => _client;
    public string SqlDir => _sqlDir;
    public string TsClientDir => _tsClientDir;

    public SqlFileSourceTestFixture()
    {
        var connectionString = Database.Create();

        // Create temp directory for SQL files
        _sqlDir = Path.Combine(Path.GetTempPath(), "npgsqlrest_sql_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_sqlDir);
        var subDir = Path.Combine(_sqlDir, "sub");
        Directory.CreateDirectory(subDir);

        // Write SQL files for testing
        SqlFiles.WriteAll(_sqlDir, subDir);

        // TsClient output directory
        _tsClientDir = Path.Combine(Path.GetTempPath(), "npgsqlrest_tsclient_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tsClientDir);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        _app = builder.Build();

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            CommentsMode = CommentsMode.ParseAll,
            EndpointSources =
            [
                new SqlFileSource(new SqlFileSourceOptions
                {
                    FilePattern = _sqlDir.Replace('\\', '/') + "/**/*.sql",
                    CommentsMode = CommentsMode.ParseAll,
                    CommentScope = CommentScope.All,
                    ErrorMode = ParseErrorMode.Skip,
                })
            ],
            EndpointCreateHandlers =
            [
                new TsClient(new TsClientOptions
                {
                    FilePath = Path.Combine(_tsClientDir, "{0}.ts"),
                    BySchema = true,
                    IncludeStatusCode = false,
                })
            ],
        });

        _app.StartAsync().GetAwaiter().GetResult();

        var serverAddress = _app.Urls.First();
        _client = new HttpClient { BaseAddress = new Uri(serverAddress) };
        _client.Timeout = TimeSpan.FromHours(1);
    }

#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
    public void Dispose()
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
    {
        _client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();

        // Clean up temp directories
        try { if (Directory.Exists(_sqlDir)) Directory.Delete(_sqlDir, true); } catch { }
        try { if (Directory.Exists(_tsClientDir)) Directory.Delete(_tsClientDir, true); } catch { }
    }
}
