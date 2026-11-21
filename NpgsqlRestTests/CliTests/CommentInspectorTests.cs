using NpgsqlRestClient;

namespace NpgsqlRestTests.CliTests;

/// <summary>
/// npgsqlrest --parse and --parse-comment: how a comment is read, line by line, by the parsers the server runs.
/// </summary>
[Collection("TestFixture")]
public class CommentInspectorTests
{
    public CommentInspectorTests()
    {
        // what the client registers at start: the annotations of its plugins and handlers
        AnnotationReference.Register();
    }

    private static string Describe(CommentInspector.Result result) =>
        string.Join("\n", result.Lines.Select(l => $"{l.Number} {l.Kind} [{l.Name}|{l.Owner}|{l.Form}] {l.Text}"));

    [Fact]
    public void A_routine_comment_in_the_default_reading()
    {
        var result = CommentInspector.ParseComment(
            "Lists the users.\nHTTP GET /api/users\n@authorize admin\n\ntimeout = 30s\nCache-Control: no-store\n@mcp List users\n@no_such_thing here\nDisabled until Friday",
            requireAtPrefix: false, CommentsMode.OnlyAnnotated);

        result.Source.Should().Be("comment");
        // "Disabled until Friday" is the disabled annotation in this reading. Its words are read as tags, which
        // this routine does not have, so it disables nothing: but it is not the sentence its author wrote either.
        result.Endpoint.Should().BeTrue();
        result.Reason.Should().BeNull();
        Describe(result).Should().Be(string.Join("\n",
            "1 text [||] Lists the users.",
            "2 annotation [http|core|keyword] HTTP GET /api/users",
            "3 annotation [authorize|core|keyword] @authorize admin",
            "5 annotation [timeout|core|key_value] timeout = 30s",
            "6 annotation [Cache-Control|core|header] Cache-Control: no-store",
            "7 annotation [mcp|mcp|keyword] @mcp List users",
            "8 unknown_annotation [no_such_thing||] @no_such_thing here",
            "9 annotation [disabled|core|keyword] Disabled until Friday"));
    }

    [Fact]
    public void The_same_comment_with_the_prefix_required()
    {
        var result = CommentInspector.ParseComment(
            "Lists the users.\nHTTP GET /api/users\n@authorize admin\n\ntimeout = 30s\nCache-Control: no-store\n@mcp List users\n@no_such_thing here\nDisabled until Friday",
            requireAtPrefix: true, CommentsMode.OnlyAnnotated);

        result.RequireAtPrefix.Should().BeTrue();
        result.Endpoint.Should().BeFalse();
        result.Reason.Should().Be("unprefixed_annotations");
        result.Message.Should().Be("Its comment has an HTTP line without the @ prefix, and RequireAtPrefix is on, so the line is text. Write the annotations with @ (@http ...), or set RequireAtPrefix to false.");
        Describe(result).Should().Be(string.Join("\n",
            "1 text [||] Lists the users.",
            "2 text [||] HTTP GET /api/users",
            "3 annotation [authorize|core|keyword] @authorize admin",
            "5 text [||] timeout = 30s",
            "6 text [||] Cache-Control: no-store",
            "7 annotation [mcp|mcp|keyword] @mcp List users",
            "8 unknown_annotation [no_such_thing||] @no_such_thing here",
            "9 text [||] Disabled until Friday"));
    }

    [Fact]
    public void A_prefixed_comment_is_an_endpoint_in_both_readings()
    {
        foreach (var requireAtPrefix in new[] { false, true })
        {
            var result = CommentInspector.ParseComment("@http get\nA description.\n@X-Frame-Options: DENY\n@raw = true", requireAtPrefix, CommentsMode.OnlyAnnotated);
            result.Endpoint.Should().BeTrue();
            result.Reason.Should().BeNull();
            Describe(result).Should().Be(string.Join("\n",
                "1 annotation [http|core|keyword] @http get",
                "2 text [||] A description.",
                "3 annotation [X-Frame-Options|core|header] @X-Frame-Options: DENY",
                "4 annotation [raw|core|key_value] @raw = true"));
        }
    }

    [Theory]
    [InlineData("", "no_comment")]
    [InlineData("Just words.", "no_http_annotation")]
    [InlineData("HTTP GET\ndisabled", "disabled")]
    public void Why_a_comment_gives_no_endpoint(string comment, string reason)
    {
        var result = CommentInspector.ParseComment(comment, false, CommentsMode.OnlyAnnotated);
        result.Endpoint.Should().BeFalse();
        result.Reason.Should().Be(reason);
    }

    [Fact]
    public void Under_parse_all_a_comment_without_annotations_is_an_endpoint()
    {
        var result = CommentInspector.ParseComment("Just words.", false, CommentsMode.ParseAll);
        result.Endpoint.Should().BeTrue();
        Describe(result).Should().Be("1 text [||] Just words.");
    }

    [Fact]
    public void A_sql_file_with_line_numbers_and_the_lines_the_source_reads()
    {
        var sql = string.Join("\n",
            "/*",                                   // 1
            "HTTP GET /api/orders",                 // 2
            "Orders of a customer.",                // 3
            "@param $1 customerId int",             // 4
            "*/",                                   // 5
            "-- @result customer",                  // 6
            "select * from customers where id = $1; -- @single", // 7
            "",                                     // 8
            "-- returns order_row",                 // 9
            "select 1 as \"a--b\" from orders where customer_id = $1;", // 10
            "-- @tsclient_module = orders");        // 11
        var result = CommentInspector.ParseSqlFile(sql, "/x/orders.sql", false, CommentsMode.OnlyAnnotated);

        result.Source.Should().Be("sql_file");
        result.File.Should().Be("/x/orders.sql");
        result.Statements.Should().Be(2);
        result.Errors.Should().BeEmpty();
        result.Endpoint.Should().BeTrue();
        Describe(result).Should().Be(string.Join("\n",
            "2 annotation [http|core|keyword] HTTP GET /api/orders",
            "3 text [||] Orders of a customer.",
            "4 annotation [param|core|keyword] @param $1 customerId int",
            "6 annotation [result|core|keyword] @result customer",
            "7 annotation [single|core|keyword] @single",
            "9 annotation [returns|core|keyword] returns order_row",
            "11 annotation [tsclient_module|tsclient|key_value] @tsclient_module = orders"));
    }

    [Fact]
    public void A_sql_file_with_the_prefix_required()
    {
        var sql = "-- HTTP GET\n-- @http post\n-- single\nselect 1;\n-- returns order_row\nselect 2;";
        var result = CommentInspector.ParseSqlFile(sql, null, true, CommentsMode.OnlyAnnotated);
        result.Endpoint.Should().BeTrue();
        Describe(result).Should().Be(string.Join("\n",
            "1 text [||] HTTP GET",
            "2 annotation [http|core|keyword] @http post",
            "3 text [||] single",
            "5 text [||] returns order_row"));
    }

    [Fact]
    public void A_sql_file_without_a_statement_or_with_an_error()
    {
        var empty = CommentInspector.ParseSqlFile("-- HTTP GET\n", null, false, CommentsMode.OnlyAnnotated);
        empty.Endpoint.Should().BeFalse();
        empty.Reason.Should().Be("empty_file");
        empty.Message.Should().Be("The file has no SQL statement.");

        var noHttp = CommentInspector.ParseSqlFile("-- a migration\nselect 1;", null, false, CommentsMode.OnlyAnnotated);
        noHttp.Endpoint.Should().BeFalse();
        noHttp.Reason.Should().Be("no_http_annotation");
    }
}
