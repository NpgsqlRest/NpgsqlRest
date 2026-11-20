using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace NpgsqlRestTests.Setup;

[CollectionDefinition("UnhandledCommentLinesFixture")]
public class UnhandledCommentLinesFixtureCollection : ICollectionFixture<UnhandledCommentLinesTestFixture> { }

/// <summary>
/// Fixture for the neutral RoutineEndpoint.UnhandledCommentLines core feature — the comment lines
/// that core did NOT recognize as built-in directives, exposed for plugins (e.g. NpgsqlRest.Mcp) to
/// parse their own annotations and/or derive a description. Scoped to an isolated `cmt` schema
/// (excluded from every other fixture's schema list) and captures parsed endpoints via the
/// EndpointsCreated callback so tests can assert the metadata directly.
/// </summary>
public class UnhandledCommentLinesTestFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public HttpClient Client => _client;

    /// <summary>Fully-parsed endpoints captured at build time, keyed by routine name.</summary>
    public Dictionary<string, RoutineEndpoint> Endpoints { get; } = new(StringComparer.Ordinal);

    public UnhandledCommentLinesTestFixture()
    {
        Database.Create();
        var connectionString = Database.CreateAdditional("unhandled_comment_lines_test");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); // random available port

        _app = builder.Build();

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            IncludeSchemas = ["cmt"],
            CommentsMode = CommentsMode.ParseAll,
            EndpointCreateHandlers = [new CmtPluginHandler()],
            // upload handlers under names with capitals: an annotation's words are lower case
            UploadOptions = new()
            {
                Enabled = true,
                DefaultUploadHandlerOptions = new() { CsvUploadKey = "Csv", LargeObjectKey = "LO" }
            },
            EndpointsCreated = endpoints =>
            {
                foreach (var endpoint in endpoints)
                {
                    Endpoints[endpoint.Routine.Name] = endpoint;
                }
            }
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
    }
}

/// <summary>A plugin that claims the comment line "cmtplugin" and remembers that it did.</summary>
public class CmtPluginHandler : IEndpointCreateHandler
{
    public CommentLineResult? HandleCommentLine(RoutineEndpoint endpoint, string line, string[] words, string[] wordsLower)
    {
        if (wordsLower.Length == 0 || wordsLower[0].TrimStart('@') != "cmtplugin")
        {
            return null;
        }
        endpoint.Items["cmtplugin"] = true;
        return new CommentLineResult("cmtplugin");
    }

    public CommentAnnotation[] Annotations =>
    [
        new() { Name = "cmtplugin", Aliases = ["cmtplugin"], Syntax = "cmtplugin", Description = "Test plugin annotation.", Owner = "cmtplugin", ValueKind = AnnotationValueKind.None, Example = "cmtplugin" },
        new() { Name = "cmt_key", Aliases = ["cmt_key"], Syntax = "cmt_key = <value>", Description = "Test plugin key.", Owner = "cmtplugin", Form = AnnotationForm.KeyValue, Example = "cmt_key = x" },
    ];
}
