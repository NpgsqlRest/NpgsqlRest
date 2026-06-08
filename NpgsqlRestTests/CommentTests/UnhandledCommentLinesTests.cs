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

-- prose + an unknown (plugin-style) annotation are unhandled; HTTP and authorize are consumed
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
");
    }
}

[Collection("UnhandledCommentLinesFixture")]
public class UnhandledCommentLinesTests(UnhandledCommentLinesTestFixture test)
{
    [Fact]
    public void Unhandled_lines_capture_prose_and_unknown_annotations_in_order()
    {
        var e = test.Endpoints["unhandled_mixed"];
        e.UnhandledCommentLines.Should().Equal("This is a human description.", "@custom_plugin_flag");
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
