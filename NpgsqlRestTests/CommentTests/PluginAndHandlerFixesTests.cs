using NpgsqlRest.DartClient;
using NpgsqlRest.OpenAPI;
using NpgsqlRest.TsClient;
using NpgsqlRest.UploadHandlers;
using NpgsqlRestClient.Testing;
using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

/// <summary>Fixes in the plugins and handlers (3.22.0): OpenAPI tags, client generator files, upload handler names, test file includes.</summary>
[Collection("UnhandledCommentLinesFixture")]
public class PluginAndHandlerFixesTests(UnhandledCommentLinesTestFixture test)
{
    // ----- OpenAPI tags ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("openapi tag partner", new[] { "partner" })]
    // a tag of several words: used to be split into "Partner" and "API"
    [InlineData("openapi tag Partner API", new[] { "Partner API" })]
    [InlineData("@openapi tags Partner API, Billing", new[] { "Partner API", "Billing" })]
    [InlineData("openapi tags a,b ,  c", new[] { "a", "b", "c" })]
    [InlineData("OPENAPI\tTAG\tReports", new[] { "Reports" })]
    [InlineData("openapi tags one, , two,", new[] { "one", "two" })]
    public void Openapi_tags_are_separated_by_commas(string line, string[] expected)
    {
        var endpoint = test.Endpoints["unhandled_none"];
        endpoint.Items.Remove("openapi:tags");
        var words = line.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries);

        var result = new OpenApi(new OpenApiOptions()).HandleCommentLine(endpoint, line, words, [.. words.Select(w => w.ToLowerInvariant())]);

        result.Should().NotBeNull();
        (endpoint.Items["openapi:tags"] as string[]).Should().Equal(expected);
        endpoint.Items.Remove("openapi:tags");
    }

    [Theory]
    [InlineData("openapi tag")]
    [InlineData("openapi tags ,")]
    public void Openapi_tag_without_a_value_sets_nothing(string line)
    {
        var endpoint = test.Endpoints["unhandled_none"];
        endpoint.Items.Remove("openapi:tags");
        var words = line.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries);

        var result = new OpenApi(new OpenApiOptions()).HandleCommentLine(endpoint, line, words, [.. words.Select(w => w.ToLowerInvariant())]);

        result.Should().NotBeNull("the line is the plugin's, also when it sets nothing");
        endpoint.Items.ContainsKey("openapi:tags").Should().BeFalse();
    }

    // ----- one file or several: the TypeScript and Dart client generators ---------------------------------------

    private RoutineEndpoint[] TwoEndpoints => [test.Endpoints["unhandled_none"], test.Endpoints["unhandled_mixed"]];

    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "npgsqlrest_client_files_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string[] FilesOf(string dir) => [.. Directory.GetFiles(dir).Select(Path.GetFileName).Order()!];

    [Fact]
    public void TsClient_writes_one_file_when_not_by_schema_and_no_endpoint_names_a_module()
    {
        // used to log "FilePath doesn't contain {0} formatter and BySchema options is true" and write nothing
        var dir = NewDir();
        try
        {
            new TsClient(new TsClientOptions { CreateSeparateTypeFile = false, FilePath = Path.Combine(dir, "api.ts"), BySchema = false, FileOverwrite = true }).Cleanup(TwoEndpoints);
            FilesOf(dir).Should().Equal("api.ts");
            File.ReadAllText(Path.Combine(dir, "api.ts")).Should().Contain("apiCmtUnhandledNone").And.Contain("apiCmtUnhandledMixed");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TsClient_writes_one_file_when_modules_are_named_but_the_path_has_no_place_for_them()
    {
        var dir = NewDir();
        var endpoints = TwoEndpoints;
        var saved = endpoints[0].CustomParameters;
        endpoints[0].CustomParameters = new(StringComparer.OrdinalIgnoreCase) { ["tsclient_module"] = "special" };
        try
        {
            new TsClient(new TsClientOptions { CreateSeparateTypeFile = false, FilePath = Path.Combine(dir, "api.ts"), BySchema = false, FileOverwrite = true }).Cleanup(endpoints);
            FilesOf(dir).Should().Equal("api.ts");
        }
        finally { endpoints[0].CustomParameters = saved; Directory.Delete(dir, true); }
    }

    [Fact]
    public void TsClient_splits_by_module_when_the_path_has_a_place_for_it()
    {
        // used to write everything into a file literally named "{0}.ts"
        var dir = NewDir();
        var endpoints = TwoEndpoints;
        var saved = endpoints[0].CustomParameters;
        endpoints[0].CustomParameters = new(StringComparer.OrdinalIgnoreCase) { ["TsClient_Module"] = "special" };
        try
        {
            new TsClient(new TsClientOptions { CreateSeparateTypeFile = false, FilePath = Path.Combine(dir, "{0}.ts"), BySchema = false, FileOverwrite = true }).Cleanup(endpoints);
            FilesOf(dir).Should().Equal("cmt.ts", "special.ts");
            File.ReadAllText(Path.Combine(dir, "special.ts")).Should().Contain("apiCmtUnhandledNone").And.NotContain("apiCmtUnhandledMixed");
            File.ReadAllText(Path.Combine(dir, "cmt.ts")).Should().Contain("apiCmtUnhandledMixed").And.NotContain("apiCmtUnhandledNone");
        }
        finally { endpoints[0].CustomParameters = saved; Directory.Delete(dir, true); }
    }

    [Fact]
    public void TsClient_by_schema_is_unchanged()
    {
        var dir = NewDir();
        try
        {
            new TsClient(new TsClientOptions { CreateSeparateTypeFile = false, FilePath = Path.Combine(dir, "{0}.ts"), BySchema = true, FileOverwrite = true }).Cleanup(TwoEndpoints);
            FilesOf(dir).Should().Equal("cmt.ts");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DartClient_writes_one_file_when_not_by_schema_and_no_endpoint_names_a_module()
    {
        var dir = NewDir();
        try
        {
            new DartClient(new DartClientOptions { FilePath = Path.Combine(dir, "api.dart"), BySchema = false, FileOverwrite = true }).Cleanup(TwoEndpoints);
            FilesOf(dir).Should().Equal("api.dart");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DartClient_splits_by_module_when_the_path_has_a_place_for_it()
    {
        var dir = NewDir();
        var endpoints = TwoEndpoints;
        var saved = endpoints[0].CustomParameters;
        endpoints[0].CustomParameters = new(StringComparer.OrdinalIgnoreCase) { ["dartclient_module"] = "special" };
        try
        {
            new DartClient(new DartClientOptions { FilePath = Path.Combine(dir, "{0}.dart"), BySchema = false, FileOverwrite = true }).Cleanup(endpoints);
            FilesOf(dir).Should().Equal("cmt.dart", "special.dart");
        }
        finally { endpoints[0].CustomParameters = saved; Directory.Delete(dir, true); }
    }

    // ----- upload handler names ------------------------------------------------------------------------------

    [Fact]
    public void Upload_handlers_are_found_by_name_in_any_case()
    {
        var options = new NpgsqlRestUploadOptions { Enabled = true };
        options.DefaultUploadHandlerOptions.CsvUploadKey = "Csv";
        options.DefaultUploadHandlerOptions.LargeObjectKey = "LO";

        var handlers = options.CreateUploadHandlers()!;

        handlers.Keys.Should().BeEquivalentTo(["LO", "file_system", "Csv"]);
        // an annotation's words are lower case: "upload for csv, lo"
        handlers.ContainsKey("csv").Should().BeTrue();
        handlers.ContainsKey("lo").Should().BeTrue();
        handlers.ContainsKey("FILE_SYSTEM").Should().BeTrue();
        handlers.ContainsKey("excel").Should().BeFalse();
    }

    [Fact]
    public void Upload_parameters_are_known_under_a_configured_handler_name()
    {
        var renamed = UploadAnnotations.WithHandlerKey(UploadAnnotations.Core, "csv", "spreadsheet");

        renamed.Select(a => a.Name).Should().Equal(
            "check_format", "test_buffer_size", "non_printable_threshold", "delimiters",
            "has_fields_enclosed_in_quotes", "set_white_space_to_null", "row_command");
        renamed.Should().OnlyContain(a => a.Owner == "upload:spreadsheet" && a.Form == AnnotationForm.KeyValue);
        renamed.Single(a => a.Name == "delimiters").Aliases.Should().Equal("delimiters", "spreadsheet_delimiters");

        // the default name: nothing to add, in any case
        UploadAnnotations.WithHandlerKey(UploadAnnotations.Core, "csv", "csv").Should().BeEmpty();
        UploadAnnotations.WithHandlerKey(UploadAnnotations.Core, "csv", "CSV").Should().BeEmpty();

        CommentAnnotations.Register(renamed);
        CommentAnnotations.IsKnownCustomParameter("spreadsheet_delimiters").Should().BeTrue();
        CommentAnnotations.IsKnownCustomParameter("@Spreadsheet_Row_Command").Should().BeTrue();
        CommentAnnotations.IsKnownCustomParameter("spreadsheet_oid").Should().BeFalse();
    }

    // ----- test files: include lines -------------------------------------------------------------------------

    [Theory]
    [InlineData("\\ir fixtures/users.sql", "fixtures/users.sql", true)]
    [InlineData("\\i global/seed.sql", "global/seed.sql", false)]
    [InlineData("\\IR fixtures/users.sql;", "fixtures/users.sql", true)]
    // a comment after the path used to become part of it ("include not found")
    [InlineData("\\ir fixtures/users.sql   -- the test users", "fixtures/users.sql", true)]
    [InlineData("\\ir fixtures/users.sql;\t-- the test users", "fixtures/users.sql", true)]
    [InlineData("\\ir 'my fixtures/data.sql'", "my fixtures/data.sql", true)]
    [InlineData("\\ir 'my fixtures/data.sql';  -- quoted, with a comment", "my fixtures/data.sql", true)]
    [InlineData("\\ir 'odd -- name.sql'", "odd -- name.sql", true)]
    // not a comment: no space before the dashes
    [InlineData("\\ir fixtures/a--b.sql", "fixtures/a--b.sql", true)]
    public void Include_line(string line, string path, bool relativeToFile)
    {
        SqlTestFileParser.TryParseIncludeLine(line, out var actualPath, out var actualRelative).Should().BeTrue();
        actualPath.Should().Be(path);
        actualRelative.Should().Be(relativeToFile);
    }

    [Theory]
    [InlineData("\\ir")]
    [InlineData("\\ir   -- only a comment")]
    [InlineData("\\include x.sql")]
    [InlineData("\\irx.sql")]
    [InlineData("select 1")]
    public void Not_an_include_line(string line)
    {
        SqlTestFileParser.TryParseIncludeLine(line, out _, out _).Should().BeFalse();
    }
}
