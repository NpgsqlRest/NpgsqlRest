using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using NpgsqlRest;
using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

/// <summary>
/// Comments in SQL files: a sentence that begins with "Returns" or "result" is prose, not an annotation; the
/// source's own annotation lines are never published as the description; and "@http" is an HTTP tag.
/// ErrorMode is Exit on purpose: a comment taken for a broken @returns would end the test process.
/// </summary>
public class SqlFileCommentProseTests : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly string _sqlDir;
    private readonly Dictionary<string, RoutineEndpoint> _endpoints = new(StringComparer.Ordinal);

    public SqlFileCommentProseTests()
    {
        var connectionString = Database.Create();

        _sqlDir = Path.Combine(Path.GetTempPath(), "npgsqlrest_prose_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_sqlDir);

        // a sentence, several words: never an annotation
        File.WriteAllText(Path.Combine(_sqlDir, "prose_sentence.sql"), """
            -- HTTP GET
            -- Returns all active users
            select 1 as value;
            """);

        // one word after "Returns": has the shape of the annotation, but is no type. Taken as a comment.
        File.WriteAllText(Path.Combine(_sqlDir, "prose_one_word.sql"), """
            -- HTTP GET
            -- Returns everything
            select 2 as value;
            """);

        // the annotation, without the prefix, with a real type
        File.WriteAllText(Path.Combine(_sqlDir, "returns_scalar.sql"), """
            -- HTTP GET
            -- returns integer
            select 3;
            """);

        // the tag written with the optional @, and a single statement without a semicolon
        File.WriteAllText(Path.Combine(_sqlDir, "at_http.sql"), """
            -- @http GET
            -- @single
            select 4 as value, 'four' as name
            """);

        // the source's own lines next to prose
        File.WriteAllText(Path.Combine(_sqlDir, "multi_named.sql"), """
            -- HTTP GET
            -- The users and their count.
            -- result of the two queries below
            -- @define_param tenant integer
            select 5 as value;
            -- @result total
            select 6 as value;
            -- @skip
            select 7 as value;
            """);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
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
                    ErrorMode = ParseErrorMode.Exit,
                })
            ],
            EndpointsCreated = endpoints =>
            {
                foreach (var endpoint in endpoints)
                {
                    _endpoints[endpoint.Routine.Name] = endpoint;
                }
            }
        });

        _app.StartAsync().GetAwaiter().GetResult();
        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    private async Task<string> GetAsync(string path)
    {
        using var response = await _client.GetAsync(path);
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, content);
        return content;
    }

    [Fact]
    public async Task Sentence_starting_with_Returns_is_prose()
    {
        (await GetAsync("/api/prose-sentence")).Should().Be("[1]");
        var e = _endpoints["prose_sentence"];
        e.UnhandledCommentLines.Should().Equal("Returns all active users");
        e.AnnotationLines.Should().Equal("HTTP GET");
        e.Routine.SourceFile.Should().EndWith("prose_sentence.sql");
    }

    [Fact]
    public async Task One_word_after_Returns_that_is_no_type_is_taken_as_a_comment()
    {
        // used to be "@returns type 'everything' is not a recognized PostgreSQL type" - and, with ErrorMode Exit, the end
        (await GetAsync("/api/prose-one-word")).Should().Be("[2]");
        _endpoints["prose_one_word"].UnhandledCommentLines.Should().Equal("Returns everything");
    }

    [Fact]
    public async Task Returns_with_a_real_type_is_the_annotation_also_without_the_prefix()
    {
        (await GetAsync("/api/returns-scalar")).Should().Be("[3]");
        var e = _endpoints["returns_scalar"];
        e.UnhandledCommentLines.Should().BeNull();
        e.AnnotationLines.Should().Equal("HTTP GET", "returns integer");
    }

    [Fact]
    public async Task At_http_is_an_http_tag_and_a_header_without_a_semicolon_is_read()
    {
        (await GetAsync("/api/at-http")).Should().Be("{\"value\":4,\"name\":\"four\"}");
    }

    [Fact]
    public async Task The_sources_own_annotation_lines_are_not_the_description()
    {
        (await GetAsync("/api/multi-named")).Should().Be("{\"result1\":[5],\"total\":[6]}");
        var e = _endpoints["multi_named"];
        // prose only: what OpenAPI and MCP publish
        e.UnhandledCommentLines.Should().Equal("The users and their count.", "result of the two queries below");
        e.UnknownAnnotationLines.Should().BeNull();
        e.AnnotationLines.Should().BeEquivalentTo(["HTTP GET", "@define_param tenant integer", "@result total", "@skip"]);
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
