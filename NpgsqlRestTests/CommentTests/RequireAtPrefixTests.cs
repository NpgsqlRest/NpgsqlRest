using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void RequireAtPrefixTests()
        {
            script.Append("""
create schema rap;

create function rap.old_style() returns text language sql as $$ select 'old' $$;
comment on function rap.old_style() is 'HTTP GET
authorize';

create function rap.new_style() returns text language sql as $$ select 'new' $$;
comment on function rap.new_style() is '@http get
Returns a greeting.
@authorize';

create function rap.mixed() returns text language sql as $$ select 'mixed' $$;
comment on function rap.mixed() is '@http get
authorize
Disabled until Friday
Note: see the docs
raw = true
for procedure
@X-Test: yes';

create function rap.settings() returns table (a int, b text) language sql as $$ select 1, 'x' $$;
comment on function rap.settings() is '@HTTP GET /api/rap/custom-path
@raw = true
@Content-Type: text/plain
@separator = |';

create function rap.only_text() returns text language sql as $$ select 'text' $$;
comment on function rap.only_text() is 'Just a description, with no annotation.';

create function rap.no_comment() returns text language sql as $$ select 'none' $$;

create function rap.plugin_lines() returns text language sql as $$ select 'plugin' $$;
comment on function rap.plugin_lines() is '@http get
rapplugin without the prefix
@rapplugin with the prefix';

create function rap.only_plugin_text() returns text language sql as $$ select 'plugin' $$;
comment on function rap.only_plugin_text() is 'rapplugin without the prefix';
""");
        }
    }
}

namespace NpgsqlRestTests.Setup
{
    [CollectionDefinition("RequireAtPrefixFixture")]
    public class RequireAtPrefixFixtureCollection : ICollectionFixture<RequireAtPrefixTestFixture> { }

    /// <summary>An application with RequireAtPrefix on: a comment line is an annotation only with the @.</summary>
    public class RequireAtPrefixTestFixture : IDisposable
    {
        private readonly WebApplication _app;
        public HttpClient Client { get; }
        public Dictionary<string, RoutineEndpoint> Endpoints { get; } = new(StringComparer.Ordinal);
        public NotExposedEntry[] NotExposed { get; }
        public RapPluginHandler Plugin { get; } = new();

        public RequireAtPrefixTestFixture()
        {
            Database.Create();
            var connectionString = Database.CreateAdditional("require_at_prefix_test");
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            _app = builder.Build();
            _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
            {
                IncludeSchemas = ["rap"],
                CommentsMode = CommentsMode.OnlyAnnotated,
                RequireAtPrefix = true,
                RequiresAuthorization = false,
                EndpointCreateHandlers = [Plugin],
                EndpointsCreated = endpoints =>
                {
                    foreach (var endpoint in endpoints)
                    {
                        Endpoints[endpoint.Routine.Name] = endpoint;
                    }
                }
            });
            // the report belongs to the last build; another fixture's build replaces it
            NotExposed = EndpointReport.NotExposed;
            _app.StartAsync().GetAwaiter().GetResult();
            Client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
        }

#pragma warning disable CA1816
        public void Dispose()
#pragma warning restore CA1816
        {
            Client.Dispose();
            _app.StopAsync().GetAwaiter().GetResult();
            _app.DisposeAsync().GetAwaiter().GetResult();
        }
    }

    /// <summary>A plugin that claims lines beginning with "rapplugin" and asks for an endpoint, like mcp does.</summary>
    public class RapPluginHandler : IEndpointCreateHandler
    {
        public List<string> Claimed { get; } = [];
        public string[] EndpointRequestingAnnotations => ["rapplugin"];

        public CommentLineResult? HandleCommentLine(RoutineEndpoint endpoint, string line, string[] words, string[] wordsLower)
        {
            if (wordsLower.Length == 0 || wordsLower[0].TrimStart('@') != "rapplugin")
            {
                return null;
            }
            Claimed.Add($"{endpoint.Routine.Name}: {line}");
            return new CommentLineResult("rapplugin", RequestsEndpoint: true);
        }
    }
}

namespace NpgsqlRestTests
{
    using NpgsqlRestTests.Setup;

    /// <summary>
    /// RequireAtPrefix = true: only a line that begins with @ is an annotation. Everything else is text, whatever
    /// word it begins with: the built-in annotations, key = value, response headers, tags and plugin lines.
    /// </summary>
    [Collection("RequireAtPrefixFixture")]
    public class RequireAtPrefixTests(RequireAtPrefixTestFixture test)
    {
        [Fact]
        public async Task A_comment_in_the_old_form_creates_no_endpoint()
        {
            using var response = await test.Client.GetAsync("/api/rap/old-style");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            test.Endpoints.ContainsKey("old_style").Should().BeFalse();

            var entry = test.NotExposed.Single(e => e.Name == "old_style");
            entry.Kind.Should().Be("function");
            entry.Schema.Should().Be("rap");
            entry.Reason.Should().Be("unprefixed_annotations");
            entry.Message.Should().Be("Its comment has an HTTP line without the @ prefix, and RequireAtPrefix is on, so the line is text. Write the annotations with @ (@http ...), or set RequireAtPrefix to false.");
            entry.Diagnostics.Should().BeEquivalentTo(
                ["Function rap.old_style: no endpoint is created, because its comment has an HTTP line without the @ prefix and RequireAtPrefix is on. Write its annotations with @ (@http ...), or set RequireAtPrefix to false."]);
        }

        [Fact]
        public async Task A_comment_with_prefixes_is_read_as_before()
        {
            using var response = await test.Client.GetAsync("/api/rap/new-style");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var endpoint = test.Endpoints["new_style"];
            endpoint.RequiresAuthorization.Should().BeTrue();
            endpoint.AnnotationLines.Should().Equal("@http get", "@authorize");
            endpoint.UnhandledCommentLines.Should().Equal("Returns a greeting.");
            endpoint.UnknownAnnotationLines.Should().BeNull();
            endpoint.Diagnostics.Should().BeNull();
        }

        [Fact]
        public async Task Lines_without_the_prefix_are_text_whatever_they_begin_with()
        {
            using var response = await test.Client.GetAsync("/api/rap/mixed");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be("mixed");
            response.Headers.Contains("Note").Should().BeFalse();
            response.Headers.GetValues("X-Test").Should().Equal("yes");

            var endpoint = test.Endpoints["mixed"];
            endpoint.RequiresAuthorization.Should().BeFalse();
            endpoint.Raw.Should().BeFalse();
            endpoint.AnnotationLines.Should().Equal("@http get", "@X-Test: yes");
            endpoint.UnhandledCommentLines.Should().Equal("authorize", "Disabled until Friday", "Note: see the docs", "raw = true", "for procedure");
            // the two that restrict access when they are annotations are said
            endpoint.Diagnostics.Should().Equal(
                "Function rap.mixed mapped to GET /api/rap/mixed: \"authorize\" is read as text, not as the annotation authorize, because annotations need the @ prefix (RequireAtPrefix is on). Write @authorize, or reword the line if it is a sentence.",
                "Function rap.mixed mapped to GET /api/rap/mixed: \"Disabled until Friday\" is read as text, not as the annotation disabled, because annotations need the @ prefix (RequireAtPrefix is on). Write @disabled, or reword the line if it is a sentence.");
        }

        [Fact]
        public async Task Key_value_and_header_lines_work_with_the_prefix()
        {
            using var response = await test.Client.GetAsync("/api/rap/custom-path");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
            (await response.Content.ReadAsStringAsync()).Should().Be("1|\"x\"");

            var endpoint = test.Endpoints["settings"];
            endpoint.Raw.Should().BeTrue();
            endpoint.AnnotationLines.Should().Equal("@HTTP GET /api/rap/custom-path", "@raw = true", "@Content-Type: text/plain", "@separator = |");
            endpoint.UnhandledCommentLines.Should().BeNull();
        }

        [Fact]
        public void A_comment_without_annotations_is_not_exposed_and_the_message_names_the_prefixed_form()
        {
            var text = test.NotExposed.Single(e => e.Name == "only_text");
            text.Reason.Should().Be("no_http_annotation");
            text.Message.Should().Be("Its comment has no @http annotation. Under this CommentsMode an endpoint is created only for a comment that has one.");
            text.Diagnostics.Should().BeNull();

            var none = test.NotExposed.Single(e => e.Name == "no_comment");
            none.Reason.Should().Be("no_comment");
            none.Message.Should().Be("It has no comment. Under this CommentsMode an endpoint is created only for a comment with the @http annotation.");
        }

        [Fact]
        public void A_plugin_is_offered_only_the_lines_with_the_prefix()
        {
            test.Plugin.Claimed.Should().Equal("plugin_lines: @rapplugin with the prefix");
            test.Endpoints["plugin_lines"].UnhandledCommentLines.Should().Equal("rapplugin without the prefix");
            // a plugin line without the prefix does not ask for an endpoint either
            test.Endpoints.ContainsKey("only_plugin_text").Should().BeFalse();
            test.NotExposed.Single(e => e.Name == "only_plugin_text").Reason.Should().Be("no_http_annotation");
        }

        [Fact]
        public void Every_routine_is_either_an_endpoint_or_reported()
        {
            test.Endpoints.Keys.Order().Should().Equal("mixed", "new_style", "plugin_lines", "settings");
            test.NotExposed.Select(e => e.Name).Order().Should().Equal("no_comment", "old_style", "only_plugin_text", "only_text");
        }
    }
}
