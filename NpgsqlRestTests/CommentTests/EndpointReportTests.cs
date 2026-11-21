using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using NpgsqlRest.HttpFiles;
using NpgsqlRest.OpenAPI;
using NpgsqlRest.TsClient;

namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void EndpointReportTests()
        {
            script.Append("""
create schema rpt;
create schema rpt_hidden;

create function rpt.shown(_id int) returns text language sql as $$ select 'shown ' || _id $$;
comment on function rpt.shown(int) is 'HTTP GET
Shows one thing.';

create function rpt.shown_too() returns table (id int, name text) language sql as $$ select 1, 'a' $$;
comment on function rpt.shown_too() is '@http post';

-- the name of this one begins like the first: its request line must not be taken for the other's
create function rpt.show() returns text language sql as $$ select 'show' $$;
comment on function rpt.show() is 'HTTP GET';

create function rpt.tabbed() returns text language sql as $$ select 'tabbed' $$;
comment on function rpt.tabbed() is E'@http\tget\n@authorize\tadmin,\tmanager\n@timeout\t30s';

create function rpt.header_at() returns text language sql as $$ select 'header' $$;
comment on function rpt.header_at() is 'HTTP GET
@X-Mark: yes
X-Plain: also';

create function rpt.no_http() returns text language sql as $$ select 'x' $$;
comment on function rpt.no_http() is 'Only a description.
authorize';

create function rpt.no_comment() returns text language sql as $$ select 'x' $$;

create function rpt.switched_off() returns text language sql as $$ select 'x' $$;
comment on function rpt.switched_off() is 'HTTP GET
disabled';

create function rpt.for_other() returns text language sql as $$ select 'x' $$;
comment on function rpt.for_other() is 'for procedure
HTTP GET';

create function rpt.rpt_left_out() returns text language sql as $$ select 'x' $$;
comment on function rpt.rpt_left_out() is 'HTTP GET';

create function rpt_hidden.wanted(_a int, _b text) returns text language sql as $$ select 'x' $$;
comment on function rpt_hidden.wanted(int, text) is '
  @http get
';

-- no HTTP line: nobody expects an endpoint, so it is not reported
create function rpt_hidden.not_wanted() returns text language sql as $$ select 'x' $$;
comment on function rpt_hidden.not_wanted() is 'A helper. It says http://example.com in a sentence.';
""");
        }
    }
}

namespace NpgsqlRestTests.Setup
{
    [CollectionDefinition("EndpointReportFixture")]
    public class EndpointReportFixtureCollection : ICollectionFixture<EndpointReportTestFixture> { }

    /// <summary>
    /// What a build leaves behind for tools (EndpointReport): everything that is not an endpoint with the reason,
    /// and the files generated for each endpoint.
    /// </summary>
    public class EndpointReportTestFixture : IDisposable
    {
        private readonly WebApplication _app;
        public HttpClient Client { get; }
        public string OutputPath { get; } = Path.Combine(Path.GetTempPath(), "NpgsqlRestTests", "EndpointReport_" + Guid.NewGuid().ToString("N")[..8]);
        public Dictionary<string, RoutineEndpoint> Endpoints { get; } = new(StringComparer.Ordinal);
        public NotExposedEntry[] NotExposed { get; }
        public Dictionary<string, GeneratedFileEntry[]> Generated { get; } = new(StringComparer.Ordinal);
        public GeneratedFileEntry[] GeneratedFiles { get; }

        public EndpointReportTestFixture()
        {
            Database.Create();
            var connectionString = Database.CreateAdditional("endpoint_report_test");
            Directory.CreateDirectory(OutputPath);
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            _app = builder.Build();
            EndpointReport.IncludeFilteredOut = true;
            try
            {
                _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
                {
                    IncludeSchemas = ["rpt"],
                    NameNotSimilarTo = "rpt[_]left%",
                    CommentsMode = CommentsMode.OnlyAnnotated,
                    RequiresAuthorization = false,
                    EndpointCreateHandlers =
                    [
                        new TsClient(new TsClientOptions
                        {
                            FilePath = Path.Combine(OutputPath, "client.ts"),
                            FileOverwrite = true,
                            IncludeHost = false,
                            CreateSeparateTypeFile = true,
                            HeaderLines = [],
                            ReactQuery = new() { Enabled = true, FilePath = Path.Combine(OutputPath, "clientHooks.ts") },
                        }),
                        new HttpFile(new HttpFileOptions
                        {
                            Option = HttpFileOption.File,
                            NamePattern = Path.Combine(OutputPath, "requests"),
                            FileOverwrite = true,
                            CommentHeader = CommentHeader.None
                        }),
                        new OpenApi(new OpenApiOptions
                        {
                            FileName = Path.Combine(OutputPath, "openapi.json"),
                            FileOverwrite = true,
                            AddCurrentServer = false
                        }),
                    ],
                    EndpointsCreated = endpoints =>
                    {
                        foreach (var endpoint in endpoints)
                        {
                            Endpoints[endpoint.Routine.Name] = endpoint;
                        }
                    }
                });
            }
            finally
            {
                EndpointReport.IncludeFilteredOut = false;
            }
            // the report belongs to the last build; another fixture's build replaces it
            NotExposed = EndpointReport.NotExposed;
            GeneratedFiles = EndpointReport.GeneratedFiles;
            foreach (var (name, endpoint) in Endpoints)
            {
                Generated[name] = EndpointReport.GeneratedFor(endpoint);
            }
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
            try { Directory.Delete(OutputPath, true); } catch { /* temp files */ }
        }
    }
}

namespace NpgsqlRestTests
{
    using NpgsqlRestTests.Setup;

    [Collection("EndpointReportFixture")]
    public class EndpointReportTests(EndpointReportTestFixture test)
    {
        private NotExposedEntry Entry(string name) => test.NotExposed.Single(e => e.Name == name && e.Schema is "rpt" or "rpt_hidden");

        [Fact]
        public void Every_routine_with_a_reason_to_be_looked_at_is_an_endpoint_or_reported()
        {
            test.Endpoints.Keys.Order().Should().Equal("header_at", "show", "shown", "shown_too", "tabbed");
            // (the database of the test run has other schemas; their routines with an HTTP line are left out by
            // this application's IncludeSchemas too, and reported the same way)
            test.NotExposed.Where(e => e.Schema is "rpt" or "rpt_hidden").Select(e => $"{e.Schema}.{e.Name}: {e.Reason}").Should().BeEquivalentTo(
            [
                "rpt.for_other: tag_mismatch",
                "rpt.no_comment: no_comment",
                "rpt.no_http: no_http_annotation",
                "rpt.rpt_left_out: excluded_by_filter",
                "rpt.switched_off: disabled",
                "rpt_hidden.wanted: excluded_by_filter"
            ]);
            test.NotExposed.Where(e => e.Schema is not ("rpt" or "rpt_hidden")).Should().OnlyContain(e => e.Reason == "excluded_by_filter");
        }

        [Fact]
        public void A_routine_without_a_comment()
        {
            var entry = Entry("no_comment");
            entry.Kind.Should().Be("function");
            entry.Definition.Should().Be("function rpt.no_comment()\nreturns text\n");
            entry.SourceFile.Should().BeNull();
            entry.Message.Should().Be("It has no comment. Under this CommentsMode an endpoint is created only for a comment with the HTTP annotation.");
        }

        [Fact]
        public void A_comment_without_the_http_annotation()
        {
            Entry("no_http").Message.Should().Be("Its comment has no HTTP annotation. Under this CommentsMode an endpoint is created only for a comment that has one.");
        }

        [Fact]
        public void A_disabled_routine()
        {
            Entry("switched_off").Message.Should().Be("Its comment has the disabled annotation.");
        }

        [Fact]
        public void An_http_annotation_under_a_tag_of_another_kind()
        {
            Entry("for_other").Message.Should().Be("Its HTTP annotation comes after a \"for\" line that does not name this routine's tags, so it does not apply.");
        }

        [Fact]
        public void A_routine_left_out_by_a_schema_filter()
        {
            var entry = Entry("wanted");
            entry.Kind.Should().Be("function");
            entry.Schema.Should().Be("rpt_hidden");
            entry.Definition.Should().Be("function rpt_hidden.wanted(_a integer, _b text)");
            entry.Message.Should().Be("Its schema, rpt_hidden, is left out by the IncludeSchemas setting.");
        }

        [Fact]
        public void A_routine_left_out_by_a_name_filter()
        {
            Entry("rpt_left_out").Message.Should().Be("Its name is left out by the NameNotSimilarTo setting.");
        }

        [Fact]
        public void A_filtered_out_routine_without_an_http_annotation_is_not_reported()
        {
            test.NotExposed.Should().NotContain(e => e.Name == "not_wanted");
            test.NotExposed.Should().NotContain(e => e.Schema == "pg_catalog" || e.Schema == "information_schema");
        }

        [Fact]
        public async Task A_tab_separates_words_like_a_space()
        {
            var endpoint = test.Endpoints["tabbed"];
            endpoint.Method.Should().Be(Method.GET);
            endpoint.RequiresAuthorization.Should().BeTrue();
            endpoint.AuthorizeRoles.Should().BeEquivalentTo(["admin", "manager"]);
            endpoint.CommandTimeout.Should().Be(TimeSpan.FromSeconds(30));
            endpoint.UnknownAnnotationLines.Should().BeNull();

            using var response = await test.Client.GetAsync("/api/rpt/tabbed");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task A_header_line_written_with_the_prefix_has_no_at_in_its_name()
        {
            using var response = await test.Client.GetAsync("/api/rpt/header-at");
            (await response.Content.ReadAsStringAsync()).Should().Be("header");
            response.Headers.GetValues("X-Mark").Should().Equal("yes");
            response.Headers.GetValues("X-Plain").Should().Equal("also");
            response.Headers.Select(h => h.Key).Should().NotContain(name => name.StartsWith('@'));
        }

        private static string LineOf(GeneratedFileEntry entry) => File.ReadAllLines(entry.File)[entry.Line!.Value - 1];

        [Fact]
        public void The_typescript_function_of_an_endpoint_and_its_line()
        {
            var client = test.Generated["shown"].Single(g => g.Generator == "tsclient" && g.Role == "client");
            client.File.Should().Be(Path.Combine(test.OutputPath, "client.ts"));
            client.Symbol.Should().Be("rptShown");
            LineOf(client).Should().Be("export async function rptShown(");

            var hooks = test.Generated["shown"].Single(g => g.Generator == "tsclient" && g.Role == "hooks");
            hooks.Symbol.Should().Be("useRptShown");
            LineOf(hooks).Should().Be("export function useRptShown(");

            var mutation = test.Generated["shown_too"].Single(g => g.Generator == "tsclient" && g.Role == "hooks");
            LineOf(mutation).Should().Be("export function useRptShownTooMutation(");
        }

        [Fact]
        public void The_request_of_an_endpoint_in_the_http_file_is_its_own_not_one_that_begins_alike()
        {
            var show = test.Generated["show"].Single(g => g.Generator == "httpfiles");
            show.File.Should().Be(Path.Combine(test.OutputPath, "requests.http"));
            show.Role.Should().Be("requests");
            show.Symbol.Should().Be("GET {{host}}/api/rpt/show");
            LineOf(show).Should().Be("GET {{host}}/api/rpt/show");

            var shown = test.Generated["shown"].Single(g => g.Generator == "httpfiles");
            LineOf(shown).Should().Be("GET {{host}}/api/rpt/shown?id=1");
            var shownToo = test.Generated["shown_too"].Single(g => g.Generator == "httpfiles");
            LineOf(shownToo).Should().Be("POST {{host}}/api/rpt/shown-too");
        }

        [Fact]
        public void The_operation_of_an_endpoint_in_the_openapi_document()
        {
            var operation = test.Generated["shown"].Single(g => g.Generator == "openapi");
            operation.File.Should().Be(Path.Combine(test.OutputPath, "openapi.json"));
            operation.Role.Should().Be("document");
            operation.Symbol.Should().Be("rpt_shown_get");
            // the document is written on one line
            operation.Line.Should().Be(1);
            File.ReadAllText(operation.File).Should().Contain("\"operationId\":\"rpt_shown_get\"");
        }

        [Fact]
        public void Every_endpoint_has_its_files_and_every_file_is_listed_once()
        {
            foreach (var name in test.Endpoints.Keys)
            {
                test.Generated[name].Select(g => $"{g.Generator}/{g.Role}").Order().Should().Equal(
                    "httpfiles/requests", "openapi/document", "tsclient/client", "tsclient/hooks");
                test.Generated[name].Should().OnlyContain(g => g.Line > 0 && File.Exists(g.File));
            }
            test.GeneratedFiles.Select(g => $"{g.Generator}/{g.Role}: {Path.GetFileName(g.File)}").Order().Should().Equal(
                "httpfiles/requests: requests.http",
                "openapi/document: openapi.json",
                "tsclient/client: client.ts",
                "tsclient/hooks: clientHooks.ts",
                "tsclient/types: clientTypes.d.ts");
            test.GeneratedFiles.Should().OnlyContain(g => g.Symbol == null && g.Line == null);
        }
    }
}
