using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using NpgsqlRest;
using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

/// <summary>
/// When CommentsMode is OnlyWithHttpTag (default), files without an HTTP tag
/// should be silently skipped — even if they contain invalid SQL.
/// </summary>
public class OnlyWithHttpTagSkipsInvalidTests : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly string _sqlDir;

    public OnlyWithHttpTagSkipsInvalidTests()
    {
        var connectionString = Database.Create();

        _sqlDir = Path.Combine(Path.GetTempPath(), "npgsqlrest_httptag_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_sqlDir);

        // File with NO HTTP tag and INVALID SQL — should be silently skipped
        File.WriteAllText(Path.Combine(_sqlDir, "no_tag_invalid.sql"), """
            selec typo from nonexistent_table;
            """);

        // File with NO HTTP tag and valid SQL — should also be skipped (no HTTP tag)
        File.WriteAllText(Path.Combine(_sqlDir, "no_tag_valid.sql"), """
            select 1 as value;
            """);

        // File WITH HTTP tag and valid SQL — should create endpoint
        File.WriteAllText(Path.Combine(_sqlDir, "has_tag_valid.sql"), """
            -- HTTP GET
            select 1 as value;
            """);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            CommentsMode = CommentsMode.OnlyWithHttpTag,
            EndpointSources =
            [
                new SqlFileSource(new SqlFileSourceOptions
                {
                    FilePattern = _sqlDir.Replace('\\', '/') + "/**/*.sql",
                    CommentsMode = CommentsMode.OnlyWithHttpTag,
                    CommentScope = CommentScope.All,
                    ErrorMode = ParseErrorMode.Exit, // Exit mode — would crash if invalid file is processed
                })
            ],
        });

        _app.StartAsync().GetAwaiter().GetResult();

        var serverAddress = _app.Urls.First();
        _client = new HttpClient { BaseAddress = new Uri(serverAddress), Timeout = TimeSpan.FromHours(1) };
    }

    [Fact]
    public async Task NoHttpTag_InvalidSql_IsSkipped()
    {
        // Should not exist — file has no HTTP tag
        using var response = await _client.GetAsync("/api/no-tag-invalid");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task NoHttpTag_ValidSql_IsSkipped()
    {
        // Should not exist — file has no HTTP tag
        using var response = await _client.GetAsync("/api/no-tag-valid");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WithHttpTag_ValidSql_CreatesEndpoint()
    {
        using var response = await _client.GetAsync("/api/has-tag-valid");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("[1]");
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
        try { if (Directory.Exists(_sqlDir)) Directory.Delete(_sqlDir, true); } catch { }
        GC.SuppressFinalize(this);
    }
}
