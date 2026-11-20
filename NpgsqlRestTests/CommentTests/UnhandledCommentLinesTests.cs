using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    // Test functions for the neutral UnhandledCommentLines core feature, in an isolated `cmt`
    // schema (the shared Program/IncludeSchemas list excludes `cmt`, so these never appear elsewhere).
    public static void UnhandledCommentLinesTests()
    {
        script.Append(@"
create schema if not exists cmt;

-- prose is unhandled; a line written as an annotation that nothing reads is kept apart; HTTP and authorize are consumed
create function cmt.unhandled_mixed() returns text language sql as 'select ''mixed''';
comment on function cmt.unhandled_mixed() is '
HTTP GET
This is a human description.
@custom_plugin_flag
authorize';

-- only built-in directives -> nothing unhandled
create function cmt.unhandled_none() returns text language sql as 'select ''none''';
comment on function cmt.unhandled_none() is '
HTTP GET
authorize';

-- custom parameters: one an upload handler reads, one written as an annotation that nothing reads,
-- one without the @ that nothing reads (left alone: a line of that shape may be anything)
create function cmt.unknown_keys() returns text language sql as 'select ''keys''';
comment on function cmt.unknown_keys() is '
HTTP GET
@row_command = select 1
@tsclinet_modul = users
plain_key = some value';

-- a warning about the comment is kept with the endpoint
create function cmt.with_diagnostic() returns text language sql as 'select ''diagnostic''';
comment on function cmt.with_diagnostic() is '
HTTP GET
@row_command = select {not_substituted}';
");
    }
}

[Collection("UnhandledCommentLinesFixture")]
public class UnhandledCommentLinesTests(UnhandledCommentLinesTestFixture test)
{
    [Fact]
    public void Unhandled_lines_are_the_prose_and_unknown_annotations_are_kept_apart()
    {
        var e = test.Endpoints["unhandled_mixed"];
        // prose: what OpenAPI and MCP publish as the description
        e.UnhandledCommentLines.Should().Equal("This is a human description.");
        // written as an annotation, read by nothing: never part of the description
        e.UnknownAnnotationLines.Should().Equal("@custom_plugin_flag");
        e.AnnotationLines.Should().Equal("HTTP GET", "authorize");
    }

    [Fact]
    public void Custom_parameter_written_as_an_annotation_that_nothing_reads_is_reported()
    {
        var e = test.Endpoints["unknown_keys"];
        // all three are stored, as before
        e.CustomParameters.Should().Equal(new Dictionary<string, string>
        {
            ["row_command"] = "select 1",
            ["tsclinet_modul"] = "users",
            ["plain_key"] = "some value",
        });
        // only the one written with @ and read by nothing is called unknown
        e.UnknownAnnotationLines.Should().Equal("@tsclinet_modul = users");
        e.Diagnostics.Should().ContainSingle()
            .Which.Should().EndWith("\"@tsclinet_modul = users\" - nothing reads a custom parameter named tsclinet_modul. Check the spelling (npgsqlrest --annotations lists what is read).");
        e.UnhandledCommentLines.Should().BeNull();
    }

    [Fact]
    public void Warnings_about_a_comment_are_kept_with_the_endpoint()
    {
        var e = test.Endpoints["with_diagnostic"];
        e.Diagnostics.Should().HaveCount(2);
        e.Diagnostics![0].Should().Contain("custom parameter @row_command is SQL");
        e.Diagnostics[1].Should().Contain("uses a parameter placeholder 'not_substituted'");
        test.Endpoints["unhandled_none"].Diagnostics.Should().BeNull();
    }

    [Fact]
    public void Reference_knows_core_keys_patterns_and_prefixed_upload_keys()
    {
        CommentAnnotations.IsKnown("authorize").Should().BeTrue();
        CommentAnnotations.IsKnown("@Authorize").Should().BeTrue();
        CommentAnnotations.IsKnown("command_timeout").Should().BeTrue();
        CommentAnnotations.IsKnown("result").Should().BeTrue();
        CommentAnnotations.IsKnown("sse_publish").Should().BeTrue();
        CommentAnnotations.IsKnown("csv_delimiters").Should().BeTrue();
        CommentAnnotations.IsKnown("row_command").Should().BeTrue();
        // a key = value line: only what is read in that form
        CommentAnnotations.IsKnownCustomParameter("@timeout").Should().BeTrue();
        CommentAnnotations.IsKnownCustomParameter("csv_row_command").Should().BeTrue();
        CommentAnnotations.IsKnownCustomParameter("authorize").Should().BeFalse();
        CommentAnnotations.IsKnown("no_such_annotation").Should().BeFalse();
        // read in a type's comment, not an endpoint's
        CommentAnnotations.IsKnown("retry_delay").Should().BeFalse();
    }

    [Fact]
    public void Every_reference_entry_is_complete()
    {
        var all = CommentAnnotations.All;
        all.Should().NotBeEmpty();
        foreach (var a in all)
        {
            a.Name.Should().NotBeNullOrWhiteSpace();
            a.Syntax.Should().NotBeNullOrWhiteSpace(a.Name);
            a.Description.Should().NotBeNullOrWhiteSpace(a.Name);
            a.Owner.Should().NotBeNullOrWhiteSpace(a.Name);
            a.Contexts.Should().NotBe(AnnotationContext.None, a.Name);
            a.Example.Should().NotBeNullOrWhiteSpace(a.Name);
            if (a.ValueKind == AnnotationValueKind.Enum)
            {
                a.Values.Should().NotBeNullOrEmpty(a.Name);
            }
        }
        all.Select(a => a.Owner + "/" + a.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Built_in_directives_are_not_unhandled()
    {
        var e = test.Endpoints["unhandled_none"];
        e.UnhandledCommentLines.Should().BeNull();
    }

    [Fact]
    public void Items_property_bag_is_writable_and_round_trips_typed_values()
    {
        var e = test.Endpoints["unhandled_mixed"];
        e.Items["test:flag"] = true;
        e.Items["test:tags"] = new[] { "a", "b" };

        e.Items.TryGetValue("test:flag", out var flag).Should().BeTrue();
        flag.Should().Be(true);
        (e.Items["test:tags"] as string[]).Should().Equal("a", "b");
    }
}
